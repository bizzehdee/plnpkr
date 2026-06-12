using PlanningPoker.Core.Integrations;

namespace PlanningPoker.Integrations;

/// <summary>Resolves the registered <see cref="IOAuthFlow"/> for a provider, or null if none. See #4.</summary>
public sealed class OAuthFlowProvider : IOAuthFlowProvider
{
    private readonly IReadOnlyDictionary<IntegrationProvider, IOAuthFlow> _flows;

    public OAuthFlowProvider(IEnumerable<IOAuthFlow> flows) =>
        _flows = flows.ToDictionary(f => f.Provider);

    public IOAuthFlow? For(IntegrationProvider provider) =>
        _flows.TryGetValue(provider, out var flow) ? flow : null;
}
