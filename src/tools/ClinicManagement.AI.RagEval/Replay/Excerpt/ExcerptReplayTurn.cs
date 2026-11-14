namespace ClinicManagement.AI.RagEval;

public static class ExcerptReplayTurn
{
    public static async Task<ExcerptReplayRow> RunAsync(BlindAnswerRuntime runtime, ExcerptReplayTransport transport,
        string question, CancellationToken ct = default)
    {
        transport.BeginTurn();
        try { await runtime.AnswerAsync(question, true, ct); }
        catch (ExcerptPromptCapturedException) { }

        var response = await runtime.AnswerAsync(question, false, ct);
        var answer = string.IsNullOrEmpty(response.Message) ? response.Reply ?? string.Empty : response.Message;
        if (string.IsNullOrWhiteSpace(answer)) return new(answer, ExcerptRoute.Empty, null, transport.PromptPassages);
        if (response.DialogueOutcome == "KnowledgeExtractive")
            return new(answer, ExcerptRoute.DocumentExcerpt, null, transport.PromptPassages);
        var policy = response.DialogueOutcome ?? (response.SafetyNotice is not null ? "InputPrivacy" : response.PrimaryIntent)
            ?? "DeterministicFallback";
        return new(answer, ExcerptRoute.PolicyResponse, policy, transport.PromptPassages);
    }
}
