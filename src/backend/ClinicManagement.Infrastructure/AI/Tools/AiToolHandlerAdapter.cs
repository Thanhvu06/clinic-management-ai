using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.Infrastructure.AI.Tools;

public sealed class AiToolHandlerAdapter : IAiToolHandler
{
    private readonly PatientCopilotToolHandler _dispatcher;
    public AiToolHandlerAdapter(PatientCopilotToolHandler dispatcher, AiToolDefinition definition)
    {
        _dispatcher = dispatcher;
        Definition = definition;
    }

    public AiToolDefinition Definition { get; }

    public AiToolArgumentValidationResult ValidateArguments(AiToolInvocation invocation, AiToolExecutionContext context) =>
        _dispatcher.ValidateArguments(invocation, context);

    public Task<AiToolExecutionResult> ExecuteAsync(AiToolInvocation invocation, AiToolExecutionContext context, CancellationToken cancellationToken = default) =>
        _dispatcher.ExecuteAsync(invocation, context, cancellationToken);
}
