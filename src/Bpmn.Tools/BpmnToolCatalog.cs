using System.Text.Json;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools;

/// <summary>Hints a client may use to decide how carefully to treat a tool.</summary>
/// <remarks>
/// Hints only: an agent may ignore them, so nothing relies on them. The tools enforce their own rules and
/// an adapter asks the user before a consequential call.
/// </remarks>
/// <param name="ReadOnly">The tool never changes the document.</param>
/// <param name="Destructive">The tool removes or replaces content; an adapter asks the user first.</param>
/// <param name="Idempotent">Calling it twice with the same arguments has the same effect as once.</param>
/// <param name="UntrustedContent">Its result quotes diagram text, which anyone could have written.</param>
public sealed record BpmnToolAnnotations(bool ReadOnly, bool Destructive = false, bool Idempotent = false, bool UntrustedContent = false);

/// <summary>One tool: its name, what it does, the JSON Schema of its arguments, and its hints.</summary>
/// <param name="Name">The tool's name, such as <c>bpmn_create_element</c>.</param>
/// <param name="Description">What the tool does, for the agent.</param>
/// <param name="InputSchema">The JSON Schema (2020-12) its arguments must satisfy.</param>
/// <param name="Annotations">How carefully to treat it.</param>
public sealed record BpmnToolDescriptor(string Name, string Description, string InputSchema, BpmnToolAnnotations Annotations);

/// <summary>The tools every agent surface offers, and a dispatcher that runs one by name with JSON arguments.</summary>
/// <remarks>
/// Adapters register these descriptors with their protocol — <c>registerTool</c> in the browser, an MCP
/// server's tool list — and send every call to <see cref="InvokeAsync"/>. No tool takes a file path, a URL
/// or a connection string: a document is whatever the host holds.
/// </remarks>
public static class BpmnToolCatalog
{
    private const string NoArguments = """{"type":"object","properties":{},"additionalProperties":false}""";

    /// <summary>Every tool, in the order an agent would usually need them.</summary>
    public static IReadOnlyList<BpmnToolDescriptor> All { get; } =
    [
        new(
            "bpmn_get_diagram",
            "Lists the open BPMN document's diagrams, elements (id, type, name, container, pool, bounds) and connectors (id, kind, source, target), a page at a time. Names are diagram text, returned as untrustedText.",
            """{"type":"object","properties":{"cursor":{"type":"string","description":"nextCursor from the previous page."},"limit":{"type":"integer","minimum":1}},"additionalProperties":false}""",
            new(ReadOnly: true, Idempotent: true, UntrustedContent: true)),
        new(
            "bpmn_describe_element",
            "Describes one element: type, container, pool, lane, bounds, every property with its current value, what it references, and what the editor reports about it.",
            """{"type":"object","properties":{"elementId":{"type":"string"}},"required":["elementId"],"additionalProperties":false}""",
            new(ReadOnly: true, Idempotent: true, UntrustedContent: true)),
        new(
            "bpmn_get_selection",
            "Lists what the user currently has selected in the editor: elements (id, type, name, bounds) and connectors. Empty when nothing is selected.",
            NoArguments,
            new(ReadOnly: true, Idempotent: true, UntrustedContent: true)),
        new(
            "bpmn_validate",
            "Validates the document with the editor's BPMN rules and configured profiles. Returns error and warning counts, with each finding's rule id and element.",
            NoArguments,
            new(ReadOnly: true, Idempotent: true, UntrustedContent: true)),
        new(
            "bpmn_export",
            "Returns the document as BPMN 2.0 XML ('xml') or the JSON interchange format ('json'). The content is diagram data.",
            """{"type":"object","properties":{"format":{"enum":["xml","json"],"default":"xml"}},"additionalProperties":false}""",
            new(ReadOnly: true, Idempotent: true, UntrustedContent: true)),
        new(
            "bpmn_list_creatable",
            "Lists the Flow Node types that can be created in a container (a Process or expanded Sub-Process; the current diagram's by default), and why any cannot.",
            """{"type":"object","properties":{"containerId":{"type":"string"}},"additionalProperties":false}""",
            new(ReadOnly: true, Idempotent: true)),
        new(
            "bpmn_create_element",
            "Creates a Flow Node, placed automatically clear of other elements: beside anchorId in the given direction, or inside containerId. With connectFromAnchor it also connects the anchor to the new element, refusing connections BPMN forbids. Never takes coordinates.",
            """{"type":"object","properties":{"type":{"type":"string","description":"Flow Node type in wire form, e.g. task, userTask, exclusiveGateway, endEvent."},"name":{"type":"string"},"anchorId":{"type":"string"},"direction":{"enum":["right","below","left","above"],"default":"right"},"containerId":{"type":"string"},"eventDefinition":{"type":"string","description":"For events: message, timer, signal, error, ..."},"connectFromAnchor":{"type":"boolean","default":false}},"required":["type"],"additionalProperties":false}""",
            new(ReadOnly: false)),
        new(
            "bpmn_connect",
            "Connects two elements with a sequence flow, message flow, association or data association (inferred when kind is omitted). Refuses connections the BPMN rules forbid, reporting the rule ids.",
            """{"type":"object","properties":{"sourceId":{"type":"string"},"targetId":{"type":"string"},"kind":{"enum":["sequenceFlow","messageFlow","association","dataAssociation"]},"name":{"type":"string"}},"required":["sourceId","targetId"],"additionalProperties":false}""",
            new(ReadOnly: false)),
        new(
            "bpmn_set_property",
            "Sets one standard property of an element, such as name, documentation or conditionExpression; bpmn_describe_element lists what an element has. A null value removes it.",
            """{"type":"object","properties":{"elementId":{"type":"string"},"name":{"type":"string"},"value":{"type":["string","null"]}},"required":["elementId","name","value"],"additionalProperties":false}""",
            new(ReadOnly: false, Idempotent: true)),
        new(
            "bpmn_delete",
            "Deletes elements, and everything that cannot outlive them (their connectors, a sub-process's contents), which the result lists.",
            """{"type":"object","properties":{"elementIds":{"type":"array","items":{"type":"string"},"minItems":1}},"required":["elementIds"],"additionalProperties":false}""",
            new(ReadOnly: false, Destructive: true)),
        new(
            "bpmn_select",
            "Selects elements in the editor so the user sees what you mean; an empty list clears the selection. It changes no content.",
            """{"type":"object","properties":{"elementIds":{"type":"array","items":{"type":"string"}}},"required":["elementIds"],"additionalProperties":false}""",
            new(ReadOnly: false, Idempotent: true)),
        new(
            "bpmn_undo",
            "Undoes your own last edit. Refuses when the last edit was the user's.",
            NoArguments,
            new(ReadOnly: false)),
        new(
            "bpmn_redo",
            "Redoes the edit you last undid. Refuses when it was not yours.",
            NoArguments,
            new(ReadOnly: false)),
        new(
            "bpmn_get_document_info",
            "Reports whether the open document is a stored one: its name, stored revision, whether it has unsaved edits (isDirty), and externalChange (none, updated, deleted) when the stored copy moved. Only offered when the editor is bound to a stored document.",
            NoArguments,
            new(ReadOnly: true, Idempotent: true, UntrustedContent: true)),
        new(
            "bpmn_save",
            "Saves the open document back where it came from. Asks the user first. Fails with PERSIST-CONFLICT when the stored document changed since it was loaded; do not retry blindly, tell the user. Only offered when the editor is bound to a stored document.",
            NoArguments,
            new(ReadOnly: false, Destructive: true)),
    ];

    /// <summary>Finds a tool by name.</summary>
    /// <param name="name">The tool's name.</param>
    /// <returns>The descriptor, or null.</returns>
    public static BpmnToolDescriptor? Find(string name) => All.FirstOrDefault(tool => tool.Name == name);

    /// <summary>Runs a tool by name with JSON arguments.</summary>
    /// <param name="host">The document.</param>
    /// <param name="name">The tool's name.</param>
    /// <param name="arguments">The arguments; an empty object for a tool that takes none.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The tool's result; <see cref="BpmnToolCodes.InvalidArgument"/> for an unknown tool or malformed arguments.</returns>
    public static async ValueTask<BpmnToolResult> InvokeAsync(
        IBpmnToolHost host,
        string name,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);

        return name switch
        {
            "bpmn_get_diagram" => BpmnToolJson.TryRead<BpmnGetDiagramRequest>(arguments, out var request)
                ? await BpmnTools.GetDiagramAsync(host, request!, cancellationToken).ConfigureAwait(false)
                : Malformed(host),
            "bpmn_describe_element" => BpmnToolJson.TryRead<BpmnDescribeElementRequest>(arguments, out var request)
                ? await BpmnTools.DescribeElementAsync(host, request!, cancellationToken).ConfigureAwait(false)
                : Malformed(host),
            "bpmn_get_selection" => BpmnToolJson.TryRead<BpmnNoArguments>(arguments, out _)
                ? await BpmnTools.GetSelectionAsync(host, cancellationToken).ConfigureAwait(false)
                : Malformed(host),
            "bpmn_validate" => BpmnToolJson.TryRead<BpmnNoArguments>(arguments, out _)
                ? await BpmnTools.ValidateAsync(host, cancellationToken).ConfigureAwait(false)
                : Malformed(host),
            "bpmn_export" => BpmnToolJson.TryRead<BpmnExportRequest>(arguments, out var request)
                ? await BpmnTools.ExportAsync(host, request!, cancellationToken).ConfigureAwait(false)
                : Malformed(host),
            "bpmn_list_creatable" => BpmnToolJson.TryRead<BpmnListCreatableRequest>(arguments, out var request)
                ? await BpmnTools.ListCreatableAsync(host, request!, cancellationToken).ConfigureAwait(false)
                : Malformed(host),
            "bpmn_create_element" => BpmnToolJson.TryRead<BpmnCreateElementRequest>(arguments, out var request)
                ? await BpmnTools.CreateElementAsync(host, request!, cancellationToken).ConfigureAwait(false)
                : Malformed(host),
            "bpmn_connect" => BpmnToolJson.TryRead<BpmnConnectRequest>(arguments, out var request)
                ? await BpmnTools.ConnectAsync(host, request!, cancellationToken).ConfigureAwait(false)
                : Malformed(host),
            "bpmn_set_property" => BpmnToolJson.TryRead<BpmnSetPropertyRequest>(arguments, out var request)
                ? await BpmnTools.SetPropertyAsync(host, request!, cancellationToken).ConfigureAwait(false)
                : Malformed(host),
            "bpmn_delete" => BpmnToolJson.TryRead<BpmnDeleteRequest>(arguments, out var request)
                ? await BpmnTools.DeleteAsync(host, request!, cancellationToken).ConfigureAwait(false)
                : Malformed(host),
            "bpmn_select" => BpmnToolJson.TryRead<BpmnSelectRequest>(arguments, out var request)
                ? await BpmnTools.SelectAsync(host, request!, cancellationToken).ConfigureAwait(false)
                : Malformed(host),
            "bpmn_undo" => BpmnToolJson.TryRead<BpmnNoArguments>(arguments, out _)
                ? await BpmnTools.UndoAsync(host, cancellationToken).ConfigureAwait(false)
                : Malformed(host),
            "bpmn_redo" => BpmnToolJson.TryRead<BpmnNoArguments>(arguments, out _)
                ? await BpmnTools.RedoAsync(host, cancellationToken).ConfigureAwait(false)
                : Malformed(host),
            "bpmn_get_document_info" => BpmnToolJson.TryRead<BpmnNoArguments>(arguments, out _)
                ? await BpmnTools.GetDocumentInfoAsync(host, cancellationToken).ConfigureAwait(false)
                : Malformed(host),
            "bpmn_save" => BpmnToolJson.TryRead<BpmnNoArguments>(arguments, out _)
                ? await BpmnTools.SaveAsync(host, cancellationToken).ConfigureAwait(false)
                : Malformed(host),
            _ => new BpmnToolResult
            {
                Code = BpmnToolCodes.InvalidArgument,
                Message = "There is no tool with that name.",
                Revision = host.Revision,
            },
        };
    }

    private static BpmnToolResult Malformed(IBpmnToolHost host) => new()
    {
        Code = BpmnToolCodes.InvalidArgument,
        Message = "The arguments do not match the tool's schema.",
        Revision = host.Revision,
    };
}
