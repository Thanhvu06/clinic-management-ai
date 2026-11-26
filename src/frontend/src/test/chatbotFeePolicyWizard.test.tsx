import { act, render, renderHook, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { BookingWizard } from "../components/BookingWizard";
import { classifyPatientTypedIntent, isBookingPolicyQuestion, isConsultationPriceQuestion } from "../components/copilot/patientTypedIntent";
import { ChatProvider } from "../contexts/ChatContext";
import { useAiBookingFlow } from "../hooks/useAiBookingFlow";
import type { AiBookingWizardResponse } from "../types/ai";

const post = vi.hoisted(() => vi.fn());
vi.mock("../api/axiosClient", () => ({ default: { post, get: vi.fn() } }));
vi.mock("../auth/AuthContext", () => ({
    useAuth: () => ({ user: { userId: "patient-fee-policy-test", role: "Patient" }, identityVersion: 1, isAuthenticated: true })
}));

const wrapper = ({ children }: { children: ReactNode }) => <MemoryRouter><ChatProvider>{children}</ChatProvider></MemoryRouter>;
const chatReply = { success: true, data: { message: "Chi phí khám chuyên khoa Nhi khoa là 180.000 đ.", urgency: "ROUTINE", actions: [] } };
const wizardState = (overrides: Partial<AiBookingWizardResponse>): AiBookingWizardResponse => ({
    step: "doctor", title: "Chọn bác sĩ", message: "Chọn bác sĩ.", options: [], canGoBack: false, actions: [], suggestions: [],
    assistantMode: "Ready", providerWasCalled: false, ...overrides
});

beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    post.mockReset();
    HTMLElement.prototype.scrollIntoView = vi.fn();
});

describe("E1 colloquial fee questions", () => {
    it.each([
        "tui muốn bik khám nhi thì mất bn tiền v",
        "tui muốn khám nhi bn tiền",
        "khám da liễu bao nhiu vậy",
        "khám tai mũi họng giá sao ạ",
        "khám tim mạch hết nhiêu",
        "muốn khám nhãn khoa thì tính bn"
    ])("routes \"%s\" to the fee answer instead of the wizard", text => {
        expect(classifyPatientTypedIntent(text)).toBeNull();
        expect(isConsultationPriceQuestion(text)).toBe(true);
    });

    it("keeps dose and duration questions out of the fee route", () => {
        expect(isConsultationPriceQuestion("khám xong bao nhiu ngày có kết quả")).toBe(false);
        expect(isConsultationPriceQuestion("uống thuốc bao nhiu viên")).toBe(false);
    });

    it("sends the reproduction sentence to chat even while a wizard is open", async () => {
        post.mockResolvedValueOnce({ success: true, data: wizardState({ textToken: "doctor-text", options: [{ token: "t", label: "Bác sĩ A" }] }) })
            .mockResolvedValueOnce(chatReply);
        const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
        await act(async () => result.current.handleSendMessage("đặt lịch khám nhi"));
        await act(async () => result.current.handleSendMessage("tui muốn bik khám nhi thì mất bn tiền v"));
        expect(post.mock.calls[1][0]).toBe("/ai/chat");
    });
});

describe("E4 booking policy questions", () => {
    it.each([
        "lỡ hẹn mấy lần thì bị khóa đặt lịch online vậy, mở lại kiểu gì",
        "tại sao tui bị khóa đặt lịch online",
        "quy định đặt lịch online khi vắng mặt là sao",
        "bị khóa đặt lịch thì mở lại kiểu gì",
        "đặt lịch rồi không đến mấy lần thì bị khóa"
    ])("answers \"%s\" from policy instead of opening the wizard", text => {
        expect(isBookingPolicyQuestion(text)).toBe(true);
        expect(classifyPatientTypedIntent(text)).not.toBe("booking");
    });

    it.each([
        "đặt lịch khám nội tổng quát chiều mai giúp tôi",
        "tôi muốn đặt lịch khám da liễu",
        "đặt lịch khám thần kinh sáng mai"
    ])("still opens the wizard for a real booking request: %s", text => {
        expect(isBookingPolicyQuestion(text)).toBe(false);
        expect(classifyPatientTypedIntent(text)).toBe("booking");
    });

    it("posts the reproduction question to chat, not to the booking wizard", async () => {
        post.mockResolvedValueOnce(chatReply);
        const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
        await act(async () => result.current.handleSendMessage("lỡ hẹn mấy lần thì bị khóa đặt lịch online vậy, mở lại kiểu gì"));
        expect(post).toHaveBeenCalledTimes(1);
        expect(post.mock.calls[0][0]).toBe("/ai/chat");
        expect(result.current.wizard).toBeNull();
    });
});

describe("E5 wizard in chat", () => {
    it("shows the online-booking lock with the reception contact and no steps", () => {
        const message = "Bạn đã vắng mặt ít nhất 3 lần trong 90 ngày. Tạm khóa đặt lịch online đến 24/12/2026 10:00. Vui lòng liên hệ lễ tân để đặt lịch.";
        render(<BookingWizard busy={false} onStep={vi.fn()} state={wizardState({
            step: "stopped", title: "Tạm khóa đặt lịch online", message, errorCode: "ONLINE_BOOKING_BLOCKED"
        })} />);
        expect(screen.getByText(message)).toBeInTheDocument();
        expect(screen.getByRole("link", { name: "Liên hệ lễ tân" })).toHaveAttribute("href", "/locations");
        expect(screen.queryByText(/Bước \d/)).not.toBeInTheDocument();
        expect(screen.queryByRole("button", { name: "Bắt đầu lại đặt lịch" })).not.toBeInTheDocument();
    });

    it("sends the original sentence so the server can preselect tomorrow afternoon", async () => {
        post.mockResolvedValueOnce({ success: true, data: wizardState({ step: "doctor" }) });
        const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
        await act(async () => result.current.handleSendMessage("đặt lịch khám nội tổng quát chiều mai giúp tôi"));
        expect(post).toHaveBeenCalledExactlyOnceWith("/ai/booking-wizard", expect.objectContaining({
            step: "start", hint: "nội tổng quát", typedMessage: "đặt lịch khám nội tổng quát chiều mai giúp tôi"
        }), expect.anything());
    });

    it("offers the confirm button named in the review prompt as the only next step", async () => {
        const payload = { specialtyId: 1, doctorId: 2, slotId: 3, slotDate: "2026-11-27", startTime: "14:00", endTime: "14:30",
            reason: "Khám tổng quát", draftVersion: 1, confirmationId: "conf-1", sessionId: "sess-1", contextSnapshotId: "snap-1", draftId: "draft-1" };
        const review = { id: "review", type: "ReviewBooking" as const, label: "Xem tóm tắt thông tin khám", style: "secondary" as const, requiresAuthentication: true, requiresConfirmation: false, payload };
        const confirm = { id: "confirm", type: "ConfirmBooking" as const, label: "Xác nhận đặt lịch", style: "primary" as const, requiresAuthentication: true, requiresConfirmation: true, payload };
        post.mockResolvedValueOnce({ success: true, data: wizardState({ step: "review", message: "Bạn vui lòng bấm nút \"Xác nhận đặt lịch\" bên dưới.", reviewAction: review, confirmAction: confirm }) });
        const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
        await act(async () => result.current.handleWizardStep("reason", "reason-token", "Khám tổng quát"));
        const actions = result.current.messages.at(-1)?.actions ?? [];
        expect(actions.map(action => action.label)).toEqual(["Xác nhận đặt lịch"]);
        expect(actions[0].type).toBe("ConfirmBooking");
    });
});

describe("E6 no free slots", () => {
    it("keeps the doctor step and shows back plus reception contact", () => {
        render(<BookingWizard busy={false} onStep={vi.fn()} state={wizardState({
            step: "doctor", errorCode: "NO_AVAILABLE_SLOTS", canGoBack: true, backToken: "back-specialty",
            message: "Chuyên khoa Thần kinh không có lịch trống trong 14 ngày tới. Bạn có thể quay lại chọn chuyên khoa khác hoặc liên hệ lễ tân để được hỗ trợ."
        })} />);
        expect(screen.getByText(/Bước 2\//)).toBeInTheDocument();
        expect(screen.getByRole("button", { name: "Quay lại bước trước" })).toBeEnabled();
        expect(screen.getByRole("link", { name: "Liên hệ lễ tân" })).toHaveAttribute("href", "/locations");
    });
});
