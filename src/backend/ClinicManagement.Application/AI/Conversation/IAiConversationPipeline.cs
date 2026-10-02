using ClinicManagement.Application.AI.Interfaces;

namespace ClinicManagement.Application.AI.Conversation;

public interface IAiConversationPipeline
{
    AiConversationAnalysis Analyze(string? rawMessage, IntentClassificationContext? context = null);
}
