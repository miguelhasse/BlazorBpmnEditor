using global::FeelSharp;
using global::FeelSharp.Api;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

internal sealed class FeelSharpSyntax
{
    private readonly IFeelEngine _engine = new FeelEngineBuilder()
        .WithAutoDiscovery(false).WithExternalFunctions(false).Build();

    internal string? Validate(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }
        lock (_engine)
        {
            var result = _engine.ParseExpression(expression);
            return result.IsSuccess ? null : result.Error;
        }
    }

    internal static string Normalize(string expression) => expression.StartsWith('=') ? expression[1..] : expression;
}
