import type { AiCopilotResponse } from "../../api/aiCopilotApi";
import type { AiBookingWizardRequest, ChatMessage } from "../../types/ai";

export function wizardTextFields(step: AiBookingWizardRequest["step"], text?: string): Pick<AiBookingWizardRequest, "text" | "typedMessage"> {
    if (!text) return {};
    if (step === "text" && text.length <= 200) return { text };
    return { typedMessage: text };
}

export function patientCopilotTurn(response: AiCopilotResponse): ChatMessage {
    if (response.intent === "EmergencyEscalation" && response.providerState === "SafetyBlocked") {
        return { role: "model", content: response.message, urgency: "EMERGENCY", providerState: response.providerState };
    }
    return {
        role: "model",
        content: response.message,
        providerState: response.providerState,
        executionMode: response.executionMode,
        fallbackActive: response.fallbackActive,
        providerWasCalled: response.providerWasCalled,
        providerAttemptCount: response.providerAttemptCount,
        primaryIntent: response.intent,
        clarificationPrompt: response.clarification ?? undefined,
        copilotCards: response.cards ?? [],
        suggestionChips: response.suggestions ?? []
    };
}
