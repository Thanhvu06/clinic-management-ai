import type { MutableRefObject } from "react";
import type { AiCopilotResponse } from "../../api/aiCopilotApi";
import type { AiBookingWizardRequest, AiBookingWizardResponse, AiSuggestionItem, ChatMessage } from "../../types/ai";
import { normalizeTypedText } from "./typedIntentText";

export function typedReasonOption(wizard: AiBookingWizardResponse | null, text: string): string | undefined {
    if (wizard?.step !== "reason") return undefined;
    const normalized = normalizeTypedText(text);
    return wizard.options.find(option => normalizeTypedText(option.label) === normalized)?.token;
}

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
        dialogueOutcome: response.errorCode === 'SELF_HARM_RISK' ? 'SafetySelfHarm' : undefined,
        clarificationPrompt: response.clarification ?? undefined,
        copilotCards: response.cards ?? [],
        suggestionChips: response.suggestions ?? []
    };
}

// A repeated lock card is identical to the one already on screen, so the click would look unanswered;
// echo the chip as the user's turn, as a typed booking request does, before the card is shown again.
export async function startBookingFromChip(
    suggestion: AiSuggestionItem,
    start: () => Promise<void>,
    lastResponse: MutableRefObject<AiBookingWizardResponse | null>,
    appendMessage: (message: ChatMessage) => void
): Promise<void> {
    lastResponse.current = null;
    await start();
    const started = lastResponse.current as AiBookingWizardResponse | null;
    const label = suggestion.label.trim();
    if (started?.errorCode === "ONLINE_BOOKING_BLOCKED" && label) appendMessage({ role: "user", content: label });
}
