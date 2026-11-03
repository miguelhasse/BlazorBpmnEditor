using global::FeelSharp;
using global::FeelSharp.Api;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

internal sealed class FeelSharpSyntax
{
    private readonly Lazy<IFeelEngine> _engine = new(() => new FeelEngineBuilder()
        .WithAutoDiscovery(false).WithExternalFunctions(false).Build());

    internal bool IsInitialized => _engine.IsValueCreated;

    internal string? Validate(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }
        var engine = _engine.Value;
        lock (engine)
        {
            var result = engine.ParseExpression(expression);
            return result.IsSuccess ? null : result.Error;
        }
    }

    internal static string Normalize(string expression) => expression.StartsWith('=') ? expression[1..] : expression;
}
