using System.Globalization;
using System.Text;
using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools;

/// <summary>
/// The agent tools: read, validate, export, create, connect, edit and delete, over any
/// <see cref="IBpmnToolHost"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every edit goes through the host, and so through the editor's own command and validation spine; the
/// tools add no second implementation of any edit. What they add is what an agent needs and a user at the
/// canvas does not: placement without coordinates (<see cref="BpmnPlacement"/>), the connection rules the
/// canvas applies during a drag (<see cref="BpmnConnectionRules"/>), limits, and results that keep
/// diagram text (<see cref="BpmnUntrustedText"/>) apart from the tool's own words.
/// </para>
/// <para>
/// Nothing here asks the user anything. An adapter that can ask — the browser module — does so before
/// calling a consequential tool such as <see cref="DeleteAsync"/>.
/// </para>
/// </remarks>
public static partial class BpmnTools
{
    private static readonly Dictionary<string, BpmnFlowNodeType> FlowNodeTypes =
        Enum.GetValues<BpmnFlowNodeType>().ToDictionary(Wire, StringComparer.Ordinal);

    private static readonly Dictionary<string, BpmnEventDefinitionType> EventDefinitions =
        Enum.GetValues<BpmnEventDefinitionType>().ToDictionary(Wire, StringComparer.Ordinal);

    private static readonly Dictionary<string, BpmnConnectorKind> ConnectorKinds =
        Enum.GetValues<BpmnConnectorKind>().ToDictionary(Wire, StringComparer.Ordinal);

    // --- reading ---------------------------------------------------------------------------------

    /// <summary>Lists the document's diagrams, elements and connectors, a page at a time.</summary>
    /// <param name="host">The document.</param>
    /// <param name="request">Which page.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>One page, elements first, then connectors.</returns>
    public static async ValueTask<BpmnToolResult<BpmnDiagramSummary>> GetDiagramAsync(
        IBpmnToolHost host,
        BpmnGetDiagramRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(request);
        if (await host.GetDocumentAsync(cancellationToken).ConfigureAwait(false) is not { } document) return NoDocument<BpmnDiagramSummary>(host);

        var start = 0;
        if (request.Cursor is not null && (!int.TryParse(request.Cursor, NumberStyles.None, CultureInfo.InvariantCulture, out start) || start < 0))
        {
            return Failed<BpmnDiagramSummary>(host, BpmnToolCodes.InvalidArgument, "The cursor was not produced by this tool.");
        }

        var limit = Math.Clamp(request.Limit ?? host.Limits.MaxElements, 1, host.Limits.MaxElements);
        var shapes = ShapeIndex(document, host.ActiveDiagramId);
        var elements = Elements(document, shapes, host.Limits).ToList();
        var connectors = Connectors(document, host.Limits).ToList();
        var total = elements.Count + connectors.Count;

        var pageElements = elements.Skip(start).Take(limit).ToList();
        var pageConnectors = connectors.Skip(Math.Max(0, start - elements.Count)).Take(limit - pageElements.Count).ToList();
        var next = start + pageElements.Count + pageConnectors.Count;

        var diagrams = document.Diagrams
            .Select(diagram => new BpmnDiagramInfo(diagram.Id, Text(diagram.Name, host.Limits), diagram.Plane.BpmnElement))
            .ToList();

        return Succeeded(host, new BpmnDiagramSummary(
            diagrams,
            pageElements,
            pageConnectors,
            total,
            next < total ? next.ToString(CultureInfo.InvariantCulture) : null));
    }

    /// <summary>Describes one element: its properties, references and what the editor says about it.</summary>
    /// <param name="host">The document.</param>
    /// <param name="request">Which element.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The description, or <see cref="BpmnToolCodes.NotFound"/>.</returns>
    public static async ValueTask<BpmnToolResult<BpmnElementDescription>> DescribeElementAsync(
        IBpmnToolHost host,
        BpmnDescribeElementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(request);
        if (await host.GetDocumentAsync(cancellationToken).ConfigureAwait(false) is not { } document) return NoDocument<BpmnElementDescription>(host);

        if (string.IsNullOrEmpty(request.ElementId) || BpmnElementInspector.Describe(document, request.ElementId, host.IsReadOnly) is not { } snapshot)
        {
            return Failed<BpmnElementDescription>(host, BpmnToolCodes.NotFound, "There is no element with that id.");
        }

        var limits = host.Limits;
        return Succeeded(host, new BpmnElementDescription(
            snapshot.ElementId,
            snapshot.ElementType,
            CamelCase(snapshot.Category.ToString()),
            Text(snapshot.Label, limits),
            snapshot.ContainerId,
            snapshot.PoolId,
            snapshot.LaneId,
            ToolBounds(snapshot.Bounds),
            [.. snapshot.Properties.Select(property => new BpmnPropertyInfo(
                property.Name,
                CamelCase(property.Kind.ToString()),
                Text(property.Value, limits),
                property.IsReadOnly,
                [.. property.Choices.Select(choice => choice.Value)]))],
            [.. snapshot.References.Select(reference => new BpmnReferenceInfo(reference.Name, reference.TargetId, reference.IsResolved))],
            [.. snapshot.Diagnostics.Select(diagnostic => ToolDiagnostic(diagnostic, limits))],
            snapshot.Capabilities.CanConnect,
            snapshot.Capabilities.CanDelete));
    }

    /// <summary>Validates the document with the editor's rules and the host's profiles.</summary>
    /// <param name="host">The document.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Counts, with the findings in <see cref="BpmnToolResult.Diagnostics"/>.</returns>
    public static async ValueTask<BpmnToolResult<BpmnValidationSummary>> ValidateAsync(
        IBpmnToolHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (await host.GetDocumentAsync(cancellationToken).ConfigureAwait(false) is not { } document) return NoDocument<BpmnValidationSummary>(host);

        var report = BpmnValidator.Validate(document, host.Profiles);
        var findings = report.Diagnostics;

        return Succeeded(
            host,
            new BpmnValidationSummary(
                findings.Count(finding => finding.Severity == BpmnSeverity.Error),
                findings.Count(finding => finding.Severity == BpmnSeverity.Warning),
                findings.Count > host.Limits.MaxElements),
            [.. findings.Take(host.Limits.MaxElements).Select(finding => ToolDiagnostic(finding, host.Limits))]);
    }

    /// <summary>Serializes the document as BPMN 2.0 XML or the JSON interchange format.</summary>
    /// <param name="host">The document.</param>
    /// <param name="request">Which format.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The content, or <see cref="BpmnToolCodes.TooLarge"/> over the export limit.</returns>
    public static async ValueTask<BpmnToolResult<BpmnExportData>> ExportAsync(
        IBpmnToolHost host,
        BpmnExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(request);
        if (await host.GetDocumentAsync(cancellationToken).ConfigureAwait(false) is not { } document) return NoDocument<BpmnExportData>(host);

        string content;
        bool lossless;
        IReadOnlyList<BpmnToolDiagnostic> diagnostics = [];
        try
        {
            switch (request.Format)
            {
                case "xml":
                    var written = BpmnXmlWriter.Write(document, BpmnWriteOptions.Fidelity);
                    content = written.Xml;
                    lossless = written.IsLossless;
                    diagnostics = [.. written.Diagnostics.Take(host.Limits.MaxElements).Select(finding => new BpmnToolDiagnostic(
                    "EXPORT",
                    CamelCase(finding.Severity.ToString()),
                    Text(finding.Message, host.Limits),
                    finding.ElementId))];
                    break;

                case "json":
                    content = BpmnJsonWriter.Write(document);
                    lossless = true;
                    break;

                default:
                    return Failed<BpmnExportData>(host, BpmnToolCodes.InvalidArgument, "The format must be 'xml' or 'json'.");
            }
        }
        catch (Exception exception) when (exception is BpmnJsonException or System.Xml.XmlException or ArgumentException or InvalidOperationException)
        {
            // Content the document carried (an extension name that is not a valid XML name, a tree nested too deeply) can be unwritable in one format and fine in the other.
            return Failed<BpmnExportData>(host, BpmnToolCodes.InvalidArgument, $"The document cannot be written as {request.Format}: {Text(exception.Message, host.Limits)}");
        }

        if (Encoding.UTF8.GetByteCount(content) > host.Limits.MaxExportBytes)
        {
            return Failed<BpmnExportData>(host, BpmnToolCodes.TooLarge, "The document is larger than this call may return.");
        }

        return Succeeded(host, new BpmnExportData(request.Format, new BpmnUntrustedText(content), lossless), diagnostics);
    }

    /// <summary>Lists the Flow Nodes that may be created in a container, with the reason for any that may not.</summary>
    /// <param name="host">The document.</param>
    /// <param name="request">Which container.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The palette's Flow Node entries for that container.</returns>
    public static async ValueTask<BpmnToolResult<BpmnCreatableList>> ListCreatableAsync(
        IBpmnToolHost host,
        BpmnListCreatableRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(request);
        if (await host.GetDocumentAsync(cancellationToken).ConfigureAwait(false) is not { } document) return NoDocument<BpmnCreatableList>(host);

        var containerId = request.ContainerId ?? DefaultContainer(document, host.ActiveDiagramId);
        var entries = BpmnStencil.GroupsFor(document, containerId, host.ActiveDiagramId)
            .SelectMany(group => group.Entries)
            .Where(entry => entry.Kind == BpmnStencilKind.FlowNode)
            .Select(entry => new BpmnCreatableEntry(
                BpmnElementInspector.WireNameOf(entry.Type),
                entry.Label,
                entry.EventDefinition == BpmnEventDefinitionType.None ? null : Wire(entry.EventDefinition),
                entry.IsEnabled,
                entry.DisabledReason))
            .ToList();

        return Succeeded(host, new BpmnCreatableList(containerId, entries));
    }

    /// <summary>Lists what the user has selected, for hosts that have a selection.</summary>
    /// <param name="host">The document; an <see cref="IBpmnSelectionHost"/> to have a selection at all.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The selected elements and connectors; empty when the host has no selection.</returns>
    public static async ValueTask<BpmnToolResult<BpmnSelection>> GetSelectionAsync(
        IBpmnToolHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (await host.GetDocumentAsync(cancellationToken).ConfigureAwait(false) is not { } document) return NoDocument<BpmnSelection>(host);

        var selectedIds = (host as IBpmnSelectionHost)?.SelectedIds ?? [];
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in selectedIds) order.TryAdd(id, order.Count);
        var shapes = ShapeIndex(document, host.ActiveDiagramId);

        // A selection can be far larger than a page; it is cut to the call's limit like every list.
        // and kept in the order it was made in, so the last one selected is the last one listed.
        var elements = Elements(document, shapes, host.Limits).Where(element => order.ContainsKey(element.Id))
            .OrderBy(element => order[element.Id]).Take(host.Limits.MaxElements).ToList();
        var connectors = Connectors(document, host.Limits).Where(connector => order.ContainsKey(connector.Id))
            .OrderBy(connector => order[connector.Id]).Take(host.Limits.MaxElements).ToList();

        return Succeeded(host, new BpmnSelection(elements, connectors, order.Count));
    }

    // --- shared helpers --------------------------------------------------------------------------

    private static IEnumerable<BpmnElementSummary> Elements(
        BpmnDocument document,
        Dictionary<string, (BpmnShape Shape, string DiagramId)> shapes,
        BpmnToolLimits limits)
    {
        BpmnElementSummary Summary(string id, string type, string? name, string? containerId, string? poolId)
        {
            var (shape, diagramId) = shapes.TryGetValue(id, out var found) ? found : default;
            return new BpmnElementSummary(id, type, Text(name, limits), containerId, poolId, ToolBounds(shape?.Bounds), diagramId);
        }

        foreach (var pool in document.Collaboration?.Participants ?? []) yield return Summary(pool.Id, "participant", pool.Name, pool.ProcessRef, pool.Id);
        foreach (var lane in document.Lanes) yield return Summary(lane.Id, "lane", lane.Name, null, null);
        foreach (var node in document.FlowNodes) yield return Summary(node.Id, BpmnElementInspector.WireNameOf(node.Type), node.Name, node.ContainerId, node.PoolId);
        foreach (var data in document.DataObjectReferences) yield return Summary(data.Id, "dataObjectReference", data.Name, data.ContainerId, null);
        foreach (var store in document.DataStoreReferences) yield return Summary(store.Id, "dataStoreReference", store.Name, store.ContainerId, null);
        foreach (var note in document.TextAnnotations) yield return Summary(note.Id, "textAnnotation", note.Text, note.ContainerId, null);
        foreach (var group in document.Groups) yield return Summary(group.Id, "group", group.Name, group.ContainerId, null);
    }

    private static IEnumerable<BpmnConnectorSummary> Connectors(BpmnDocument document, BpmnToolLimits limits)
    {
        foreach (var flow in document.SequenceFlows) yield return new(flow.Id, "sequenceFlow", flow.SourceRef, flow.TargetRef, Text(flow.Name, limits));
        foreach (var flow in document.MessageFlows) yield return new(flow.Id, "messageFlow", flow.SourceRef, flow.TargetRef, Text(flow.Name, limits));
        foreach (var association in document.Associations) yield return new(association.Id, "association", association.SourceRef, association.TargetRef, null);
        foreach (var association in document.DataAssociations)
        {
            yield return new(association.Id, "dataAssociation", association.SourceRefs.Count > 0 ? association.SourceRefs[0] : string.Empty, association.TargetRef, null);
        }
    }

    // The shape that draws each element, preferring the active diagram when an element is drawn twice.
    private static Dictionary<string, (BpmnShape Shape, string DiagramId)> ShapeIndex(BpmnDocument document, string? activeDiagramId)
    {
        var index = new Dictionary<string, (BpmnShape, string)>(StringComparer.Ordinal);
        foreach (var diagram in document.Diagrams.OrderBy(diagram => diagram.Id == activeDiagramId ? 0 : 1))
        {
            foreach (var shape in diagram.Plane.Shapes) index.TryAdd(shape.BpmnElement, (shape, diagram.Id));
        }

        return index;
    }

    // The container a call with none in mind works in: what the active diagram draws.
    private static string? DefaultContainer(BpmnDocument document, string? activeDiagramId)
    {
        var diagram = document.Diagrams.FirstOrDefault(candidate => candidate.Id == activeDiagramId) ?? (document.Diagrams.Count > 0 ? document.Diagrams[0] : null);
        var drawn = diagram?.Plane.BpmnElement;

        if (drawn is not null && document.Processes.Any(process => process.Id == drawn)) return drawn;
        if (drawn is not null && document.FindFlowNode(drawn) is { FlowElementsOwned: true }) return drawn;
        if (drawn is not null && document.Collaboration?.Id == drawn)
        {
            return document.Collaboration.Participants.Select(pool => pool.ProcessRef).FirstOrDefault(id => id is not null);
        }

        return document.Processes.Count > 0 ? document.Processes[0].Id : null;
    }

    private static BpmnUntrustedText? Text(string? text, BpmnToolLimits limits) => BpmnUntrustedText.From(text, limits.MaxTextLength);

    private static BpmnToolBounds? ToolBounds(Bounds? bounds) =>
        bounds is { } b ? new BpmnToolBounds(b.X, b.Y, b.Width, b.Height) : null;

    private static BpmnToolDiagnostic ToolDiagnostic(BpmnDiagnostic finding, BpmnToolLimits limits) =>
        new(finding.RuleId, CamelCase(finding.Severity.ToString()), Text(finding.Message, limits), finding.ElementId, finding.RelatedId);

    private static BpmnToolDiagnostic ToolDiagnostic(BpmnEditDiagnostic finding, BpmnToolLimits limits) =>
        new(finding.Code, CamelCase(finding.Severity.ToString()), Text(finding.Message, limits), finding.ElementId);

    private static string CamelCase(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    // Flow node types are named by BpmnElementInspector.WireNameOf; this covers the other enums.
    private static string Wire<TEnum>(TEnum value) where TEnum : struct, Enum => JsonSerializer.Serialize(value).Trim('"');

    private static BpmnToolResult<TData> Succeeded<TData>(IBpmnToolHost host, TData data, IReadOnlyList<BpmnToolDiagnostic>? diagnostics = null) =>
        new() { Ok = true, Data = data, Diagnostics = diagnostics ?? [], Revision = host.Revision, IsDirty = DirtyOf(host) };

    private static BpmnToolResult<TData> Failed<TData>(
        IBpmnToolHost host,
        string code,
        string message,
        IReadOnlyList<BpmnToolDiagnostic>? diagnostics = null) =>
        new() { Ok = false, Code = code, Message = message, Diagnostics = diagnostics ?? [], Revision = host.Revision, IsDirty = DirtyOf(host) };

    private static BpmnToolResult<TData> NoDocument<TData>(IBpmnToolHost host) =>
        Failed<TData>(host, BpmnToolCodes.NoDocument, "No document is open.");
}
