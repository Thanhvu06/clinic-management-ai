import { act, renderHook, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { MemoryRouter, useLocation } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { isSafeClientRoute } from "../components/copilot/patientPresentation";
import {
    classifyPatientTypedIntent, isConsultationPriceQuestion, isMedicinePriceQuestion, isNewBookingRequest
} from "../components/copilot/patientTypedIntent";
import { ChatProvider } from "../contexts/ChatContext";
import { useAiBookingFlow } from "../hooks/useAiBookingFlow";
import type { AiAction, AiBookingWizardResponse } from "../types/ai";

const post = vi.hoisted(() => vi.fn());
vi.mock("../api/axiosClient", () => ({ default: { post, get: vi.fn() } }));
vi.mock("../auth/AuthContext", () => ({
    useAuth: () => ({ user: { userId: "patient-lock-cancel-fee", role: "Patient" }, identityVersion: 1, isAuthenticated: true })
}));

function LocationProbe() {
    return <output data-testid="current-path">{useLocation().pathname}</output>;
}
const wrapper = ({ children }: { children: ReactNode }) =>
    <MemoryRouter><ChatProvider><LocationProbe />{children}</ChatProvider></MemoryRouter>;
const chatReply = (message: string, actions: AiAction[] = []) => ({ success: true, data: { message, urgency: "ROUTINE", actions } });
const wizardState = (overrides: Partial<AiBookingWizardResponse>): AiBookingWizardResponse => ({
    step: "doctor", title: "Chọn bác sĩ", message: "Chọn bác sĩ.", options: [], canGoBack: true, actions: [], suggestions: [],
    assistantMode: "Ready", providerWasCalled: false, ...overrides
});
const stroke = "ba tôi tự nhiên méo miệng, nói ngọng, tay trái yếu hẳn từ nửa tiếng trước";

beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    post.mockReset();
    HTMLElement.prototype.scrollIntoView = vi.fn();
});

async function sendToChat(text: string) {
    post.mockResolvedValueOnce(chatReply("ok"));
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(async () => result.current.handleSendMessage(text));
    return result;
}

async function openWizard(step: "doctor" | "day") {
    post.mockResolvedValueOnce({ success: true, data: wizardState({ step, textToken: `${step}-text`, options: [{ token: "t", label: "Bác sĩ Lan" }] }) });
    const hook = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(async () => hook.result.current.handleSendMessage("đặt lịch khám nội tổng quát"));
    expect(hook.result.current.wizard?.step).toBe(step);
    return hook.result;
}

describe("G1/G2/G5 lock, fee and deadline questions are answered by chat", () => {
    it.each([
        "nhập sai mật khẩu mấy lần thì bị khóa tài khoản",
        "quên mật khẩu đăng nhập sai nhiều lần có bị khóa không",
        "hủy lịch có bị mất phí không",
        "hủy lịch có mất tiền không",
        "hủy hẹn có bị phạt không",
        "hủy hẹn trước mấy tiếng thì không sao",
        "hủy lịch trước bao lâu thì được",
        "hủy hẹn sát giờ có được không",
        "hủy lịch sát giờ có bị khóa đặt lịch không"
    ])("%s", async text => {
        expect(classifyPatientTypedIntent(text)).not.toBe("booking");
        const result = await sendToChat(text);
        expect(post).toHaveBeenCalledTimes(1);
        expect(post.mock.calls[0][0]).toBe("/ai/chat");
        expect(post.mock.calls[0][1]).toMatchObject({ message: text });
        expect(post.mock.calls[0][1].intent).toBeUndefined();
        expect(result.current.wizard).toBeNull();
    });

    it.each(["hủy lịch hẹn ngày mai của tôi", "tôi muốn hủy lịch khám"])("sends the real cancel command %s to the server cancel flow", async text => {
        await sendToChat(text);
        expect(post.mock.calls[0][0]).toBe("/ai/chat");
        expect(post.mock.calls[0][1]).toMatchObject({ message: text });
    });
});

describe("G3 the particle nhỉ is not Nhi khoa", () => {
    it.each(["thuốc paracetamol bao nhiu tiền nhỉ", "thuốc ho bn tiền nhỉ"])("%s is a medicine price question", text => {
        expect(isConsultationPriceQuestion(text)).toBe(false);
        expect(isMedicinePriceQuestion(text)).toBe(true);
        expect(classifyPatientTypedIntent(text)).toBeNull();
    });

    it.each(["nhi bn tiền vậy", "nhi khoa giá sao", "khám nhi bn tiền"])("%s stays a consultation fee question", text => {
        expect(isConsultationPriceQuestion(text)).toBe(true);
        expect(isMedicinePriceQuestion(text)).toBe(false);
    });

    it("sends the Nội tổng quát fee question with nhỉ to chat", async () => {
        const text = "khám nội tổng quát bao nhiêu tiền nhỉ";
        expect(isMedicinePriceQuestion(text)).toBe(false);
        const result = await sendToChat(text);
        expect(post.mock.calls[0][0]).toBe("/ai/chat");
        expect(result.current.wizard).toBeNull();
    });

    it("does not read nhì or nhỉ as a specialty", () => {
        expect(isConsultationPriceQuestion("giải nhì bao nhiu tiền")).toBe(false);
        expect(isConsultationPriceQuestion("bao nhiu tiền nhỉ")).toBe(false);
    });
});

describe("G6 a new booking sentence while the wizard is open", () => {
    it.each(["doctor", "day"] as const)("restarts the wizard from step %s with the typed day and part", async step => {
        const result = await openWizard(step);
        post.mockResolvedValueOnce({ success: true, data: wizardState({ step: "doctor", message: "Chọn bác sĩ Nhi khoa.", summary: { specialtyName: "Nhi khoa", requiresFacilitySelection: false, reasonProvided: false } }) });
        await act(async () => result.current.handleSendMessage("tôi muốn đặt lịch khám nhi chiều mai"));
        expect(post).toHaveBeenCalledTimes(2);
        expect(post.mock.calls[1][0]).toBe("/ai/booking-wizard");
        expect(post.mock.calls[1][1]).toMatchObject({ step: "start", hint: "nhi", typedMessage: "tôi muốn đặt lịch khám nhi chiều mai" });
        expect(post.mock.calls[1][1].optionToken).toBeUndefined();
        expect(result.current.wizard?.summary?.specialtyName).toBe("Nhi khoa");
    });

    it("keeps a typed answer to the open step on that step", async () => {
        const result = await openWizard("doctor");
        post.mockResolvedValueOnce({ success: true, data: wizardState({ step: "day", title: "Chọn ngày" }) });
        await act(async () => result.current.handleSendMessage("bác sĩ Lan"));
        expect(post.mock.calls[1][1]).toMatchObject({ step: "text", optionToken: "doctor-text", text: "bác sĩ Lan" });
    });

    it("recognises full booking requests but not step answers", () => {
        expect(isNewBookingRequest("tôi muốn đặt lịch khám nhi chiều mai", "doctor")).toBe(true);
        expect(isNewBookingRequest("tôi muốn đặt lịch khám nhi chiều mai", "day")).toBe(true);
        expect(isNewBookingRequest("đặt lịch khám da liễu", "slot")).toBe(true);
        expect(isNewBookingRequest("đặt lịch chiều mai", "specialty")).toBe(true);
        expect(isNewBookingRequest("đặt lịch chiều mai", "day")).toBe(false);
        expect(isNewBookingRequest("đặt lịch khám ngày 22/10/2026", "day")).toBe(false);
        expect(isNewBookingRequest("bác sĩ Lan", "doctor")).toBe(false);
        expect(isNewBookingRequest("ngày mai", "day")).toBe(false);
        expect(isNewBookingRequest("14h", "slot")).toBe(false);
    });

    it("lets the server raise the emergency for a stroke sentence typed while the wizard is open", async () => {
        const result = await openWizard("doctor");
        post.mockResolvedValueOnce({ success: true, data: wizardState({
            step: "stopped", errorCode: "EMERGENCY", message: "Có dấu hiệu đột quỵ. Gọi 115 ngay.",
            actions: [{ id: "act-emergency-115", type: "CallEmergency", label: "Gọi cấp cứu 115", style: "danger", requiresAuthentication: false, requiresConfirmation: false, payload: { targetUrl: "tel:115" } }]
        }) });
        await act(async () => result.current.handleSendMessage(`${stroke}, đặt lịch khám thần kinh`));
        expect(post.mock.calls[1][0]).toBe("/ai/booking-wizard");
        expect(post.mock.calls[1][1]).toMatchObject({ typedMessage: `${stroke}, đặt lịch khám thần kinh` });
        expect(result.current.messages.at(-1)).toMatchObject({ urgency: "EMERGENCY" });
    });
});

describe("G7 medicine price", () => {
    const link: AiAction = {
        id: "act-open-medicine-prices", type: "ViewSpecialty", label: "Mở Bảng giá thuốc", style: "secondary",
        requiresAuthentication: false, requiresConfirmation: false, payload: { targetUrl: "/medicines" }
    } as AiAction;

    it.each(["thuốc paracetamol bao nhiu tiền", "thuốc zyxotamin bao nhiêu tiền"])("sends %s to chat even while the wizard is open", async text => {
        const result = await openWizard("doctor");
        post.mockResolvedValueOnce(chatReply("Giá thuốc trên Bảng giá thuốc của ClinicCare:", [link]));
        await act(async () => result.current.handleSendMessage(text));
        expect(post.mock.calls[1][0]).toBe("/ai/chat");
    });

    it("opens the medicine price page from the answer button", async () => {
        expect(isSafeClientRoute("/medicines")).toBe(true);
        expect(isSafeClientRoute("/medicines/7")).toBe(true);
        expect(isSafeClientRoute("/medicines-evil")).toBe(false);
        const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
        await act(async () => result.current.handleActionClick(link));
        expect(screen.getByTestId("current-path")).toHaveTextContent(/^\/medicines$/);
    });
});
