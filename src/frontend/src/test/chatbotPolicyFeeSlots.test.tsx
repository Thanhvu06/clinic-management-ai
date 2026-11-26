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
    useAuth: () => ({ user: { userId: "patient-policy-fee-slots", role: "Patient" }, identityVersion: 1, isAuthenticated: true })
}));

const wrapper = ({ children }: { children: ReactNode }) => <MemoryRouter><ChatProvider>{children}</ChatProvider></MemoryRouter>;
const chatReply = { success: true, data: { message: "Theo quy định của ClinicCare, 3 lần Vắng mặt trong 90 ngày…", urgency: "ROUTINE", actions: [] } };

beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    post.mockReset();
    HTMLElement.prototype.scrollIntoView = vi.fn();
});

describe("F1/F2 booking rule questions go to chat, not the wizard", () => {
    it.each([
        "lỡ hẹn mấy lần thì bị khóa đặt lịch, mở lại kiểu gì",
        "bị khóa đặt lịch online thì mở lại sao",
        "hủy lịch sát giờ có bị khóa đặt lịch không",
        "lỡ hẹn 2 lần có sao không",
        "lỡ hẹn 1 lần có bị gì không",
        "trễ hẹn 3 lần thì sao"
    ])("%s", async text => {
        expect(classifyPatientTypedIntent(text)).not.toBe("booking");
        post.mockResolvedValueOnce(chatReply);
        const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
        await act(async () => result.current.handleSendMessage(text));
        expect(post).toHaveBeenCalledTimes(1);
        expect(post.mock.calls[0][0]).toBe("/ai/chat");
        expect(result.current.wizard).toBeNull();
    });

    it("keeps the F1 reproduction recognised as a policy question", () => {
        expect(isBookingPolicyQuestion("lỡ hẹn mấy lần thì bị khóa đặt lịch, mở lại kiểu gì")).toBe(true);
    });

    it.each([
        "đặt lịch khám nhi chiều mai",
        "tôi muốn đặt lịch khám tai mũi họng",
        "đặt lịch khám da liễu sáng mai giúp tôi"
    ])("still opens the wizard for a real booking: %s", async text => {
        expect(classifyPatientTypedIntent(text)).toBe("booking");
        post.mockResolvedValueOnce({ success: true, data: { step: "doctor", title: "Chọn bác sĩ", message: "", options: [], canGoBack: true,
            actions: [], suggestions: [], assistantMode: "Ready", providerWasCalled: false } });
        const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
        await act(async () => result.current.handleSendMessage(text));
        expect(post.mock.calls[0][0]).toBe("/ai/booking-wizard");
    });
});

describe("F3 specialty name with a colloquial price", () => {
    it.each(["nhi bn tiền vậy", "nhi khoa giá sao", "da liễu hết nhiêu", "tim mạch bao nhiu tiền"])("%s is a fee question", text => {
        expect(isConsultationPriceQuestion(text)).toBe(true);
        expect(classifyPatientTypedIntent(text)).toBeNull();
    });

    it.each(["thuốc hạ sốt cho bé nhi uống bao nhiu viên", "thuốc ho uống bn ngày thì ngưng"])("%s is not a fee question", text => {
        expect(isConsultationPriceQuestion(text)).toBe(false);
    });
});

describe("F4 chosen doctor without slots", () => {
    it("stays on the doctor step with the other doctors, back and reception contact", () => {
        const state: AiBookingWizardResponse = {
            step: "doctor", title: "Chọn bác sĩ", errorCode: "NO_AVAILABLE_SLOTS", canGoBack: true, backToken: "back",
            message: "Bác sĩ Lý Kim Dung không có lịch trống trong 14 ngày tới. Bạn có thể chọn bác sĩ khác, quay lại chọn chuyên khoa khác hoặc liên hệ lễ tân để được hỗ trợ.",
            options: [{ token: "any", label: "Bác sĩ nào cũng được (lịch gần nhất)" }],
            actions: [], suggestions: [], assistantMode: "Clarifying", providerWasCalled: false
        };
        render(<BookingWizard busy={false} onStep={vi.fn()} state={state} />);
        expect(screen.getByText(/Bước 2\//)).toBeInTheDocument();
        expect(screen.getByText(/Bác sĩ Lý Kim Dung không có lịch trống trong 14 ngày tới/)).toBeInTheDocument();
        expect(screen.getByRole("button", { name: "Bác sĩ nào cũng được (lịch gần nhất)" })).toBeEnabled();
        expect(screen.getByRole("button", { name: "Quay lại bước trước" })).toBeEnabled();
        expect(screen.getByRole("link", { name: "Liên hệ lễ tân" })).toHaveAttribute("href", "/locations");
    });
});
