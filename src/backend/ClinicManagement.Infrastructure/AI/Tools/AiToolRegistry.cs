using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.Infrastructure.AI.Tools;

public sealed class AiToolRegistry : IAiToolRegistry
{
    private readonly IReadOnlyDictionary<string, IAiToolHandler> _handlers;

    public AiToolRegistry(IEnumerable<IAiToolHandler> handlers)
    {
        var map = new Dictionary<string, IAiToolHandler>(StringComparer.OrdinalIgnoreCase);
        foreach (var handler in handlers)
        {
            var name = handler.Definition.Name.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("AI tool name cannot be empty.");
            if (!map.TryAdd(name, handler))
                throw new InvalidOperationException($"Duplicate AI tool registration: {name}");
        }
        _handlers = map;
    }

    public IReadOnlyCollection<AiToolDefinition> GetDefinitions() => _handlers.Values.Select(x => x.Definition).ToArray();

    public bool TryGet(string canonicalName, out AiToolDefinition definition)
    {
        if (_handlers.TryGetValue(canonicalName.Trim(), out var handler))
        {
            definition = handler.Definition;
            return true;
        }
        definition = null!;
        return false;
    }

    public bool TryGetHandler(string canonicalName, out IAiToolHandler handler) =>
        _handlers.TryGetValue(canonicalName.Trim(), out handler!);
}
