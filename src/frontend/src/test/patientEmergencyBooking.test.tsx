import { act, renderHook } from "@testing-library/react";
import type { ReactNode } from "react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, expect, it, vi } from "vitest";
import { ChatProvider } from "../contexts/ChatContext";
import { useAiBookingFlow } from "../hooks/useAiBookingFlow";

const post = vi.hoisted(() => vi.fn());
vi.mock("../api/axiosClient", () => ({ default: { post } }));
vi.mock("../auth/AuthContext", () => ({
    useAuth: () => ({ user: { userId: "patient-emergency-test", role: "Patient" }, identityVersion: 1, isAuthenticated: true })
}));

const wrapper = ({ children }: { children: ReactNode }) => <MemoryRouter><ChatProvider>{children}</ChatProvider></MemoryRouter>;
const emergency = { step: "stopped", errorCode: "EMERGENCY", message: "Hãy gọi 115 ngay.", actions: [] };
const doctorStep = { step: "doctor", textToken: "doctor-text", options: [{ token: "doctor-pick", label: "Chọn bác sĩ" }] };

beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    post.mockReset();
});

it("sends the original booking message and renders the emergency without starting a wizard", async () => {
    const message = "Ông em vừa méo miệng và nói ngọng, có nên đặt lịch khám thần kinh sáng mai không";
    post.mockResolvedValueOnce({ success: true, data: emergency });
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(async () => result.current.handleSendMessage(message));
    expect(post).toHaveBeenCalledExactlyOnceWith("/ai/booking-wizard", expect.objectContaining({
        step: "start", typedMessage: message, hint: "thần kinh"
    }), expect.anything());
    expect(result.current.messages.at(-1)).toMatchObject({ urgency: "EMERGENCY", content: emergency.message });
    expect(result.current.wizard).toBeNull();
});

it("keeps the open wizard and original session after an emergency", async () => {
    const message = "Em tôi vừa chảy máu nhiều ở cổ tay.";
    post.mockResolvedValueOnce({ success: true, data: doctorStep })
        .mockResolvedValueOnce({ success: true, data: emergency })
        .mockResolvedValueOnce({ success: true, data: { step: "day", options: [] } });
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(async () => result.current.handleSendMessage("đặt lịch khám thần kinh sáng mai"));
    const sessionId = post.mock.calls[0][1].sessionId;
    await act(async () => result.current.handleSendMessage(message));
    expect(post.mock.calls[1][1]).toMatchObject({ sessionId, step: "text", optionToken: "doctor-text", text: message });
    expect(result.current.messages.at(-1)).toMatchObject({ urgency: "EMERGENCY" });
    expect(result.current.wizard).toEqual(doctorStep);
    await act(async () => result.current.handleWizardStep("pick", "doctor-pick"));
    expect(post.mock.calls[2][1]).toMatchObject({ sessionId, step: "pick", optionToken: "doctor-pick" });
    expect(post.mock.calls[2][1]).not.toHaveProperty("typedMessage");
    expect(result.current.wizard?.step).toBe("day");
});

it("sends typed data requests with their original message and renders an emergency response", async () => {
    const message = "Xem lịch hẹn của tôi, cậu em vừa méo miệng lúc ăn cơm.";
    post.mockResolvedValueOnce({ success: true, data: {
        message: emergency.message, intent: "EmergencyEscalation", providerState: "SafetyBlocked"
    } });
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(async () => result.current.handleSendMessage(message));
    expect(post).toHaveBeenCalledExactlyOnceWith("/ai/copilot/chat", expect.objectContaining({
        message, suggestionCode: "patient.my_appointments", isTypedInput: true
    }), expect.anything());
    expect(result.current.messages.at(-1)).toMatchObject({ urgency: "EMERGENCY", content: emergency.message });
});
