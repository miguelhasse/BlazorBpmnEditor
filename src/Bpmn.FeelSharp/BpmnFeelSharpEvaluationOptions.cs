using FeelSharp.Api;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Explicit trusted function and mapper allowlist for opt-in interpretation.</summary>
public sealed class BpmnFeelSharpEvaluationOptions
{
    /// <summary>Providers explicitly trusted by the host. Discovery remains disabled.</summary>
    public IList<ICustomFunctionProvider> FunctionProviders { get; } = new List<ICustomFunctionProvider>();

    /// <summary>Mappers explicitly trusted by the host. Rich object access is not sandboxed.</summary>
    public IList<ICustomValueMapper> ValueMappers { get; } = new List<ICustomValueMapper>();
}
