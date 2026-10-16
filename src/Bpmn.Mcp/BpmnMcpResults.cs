using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Tools;

using ModelContextProtocol.Protocol;

namespace Hasseware.AspNetCore.Components.Bpmn.Mcp;

/// <summary>Turns a tool result into what an MCP client receives.</summary>
/// <remarks>
/// The text block is exactly the JSON the browser surface returns, so an agent that has used one has used both,
/// and the same JSON is the structured content, so a client that wants an object gets one. A failure sets
/// <c>isError</c>, and is still the same shape: <c>ok</c>, <c>code</c>, <c>message</c>.
/// </remarks>
public static class BpmnMcpResults
{
    /// <summary>Wraps a result.</summary>
    /// <param name="result">The tool's result.</param>
    /// <returns>The MCP result.</returns>
    public static CallToolResult From(BpmnToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var json = BpmnToolJson.Serialize(result);
        using var document = JsonDocument.Parse(json);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = json }],
            StructuredContent = document.RootElement.Clone(),
            IsError = !result.Ok,
        };
    }
}
