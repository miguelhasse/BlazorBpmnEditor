using System.Globalization;
using System.Text.Json;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>
/// Builds the section 7 property snapshot for one element.
/// </summary>
/// <remarks>
/// <para>
/// One place resolves an element into everything a surface needs to show it: the context menu, the
/// property panel and the inline label editor all read the same snapshot. That is deliberate —
/// section 7 requires the snapshot to be sufficient on its own, and three surfaces each inferring
/// the owner or the Lane from whatever was nearest would be three chances to disagree.
/// </para>
/// <para>
/// It is also the schema: <see cref="BpmnDocumentEditor"/> validates a submitted property value
/// against the very descriptor produced here, so a property's type is declared exactly once.
/// </para>
/// </remarks>
public static class BpmnElementInspector
{
    /// <summary>
    /// Describes an element, or returns <see langword="null"/> when the document has no such id.
    /// </summary>
    /// <param name="document">The document to resolve against.</param>
    /// <param name="elementId">The element to describe.</param>
    /// <param name="isReadOnly">
    /// True in Readonly mode, where section 7 allows inspection but no property binding and no type
    /// switching. Every capability flag is then false and every property is marked read-only, so a
    /// surface cannot offer an edit by consulting the configuration instead of the snapshot.
    /// </param>
    /// <param name="extensionOptions">Optional registered schemas and snapshot group builders.</param>
    /// <param name="templateOptions">Optional element-template registry and entry visibility policy.</param>
    /// <param name="display">
    /// Localizes built-in property, choice, group and type-switch captions. When omitted, the built-in resources
    /// are read for <see cref="CultureInfo.CurrentUICulture"/>. Names, values and identifiers are never translated.
    /// </param>
    /// <returns>The snapshot, or <see langword="null"/>.</returns>
    public static BpmnElementSnapshot? Describe(BpmnDocument document, string elementId, bool isReadOnly = false,
        BpmnExtensionOptions? extensionOptions = null, BpmnTemplateOptions? templateOptions = null,
        BpmnDisplayLocalizer? display = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(elementId);

        if (document.Find(elementId) is not { } element) return null;

        var shape = ShapeOf(document, elementId);
        var edge = EdgeOf(document, elementId);
        var category = CategoryOf(element);

        var properties = Properties(document, element);
        if (isReadOnly) properties = [.. properties.Select(property => property with { IsReadOnly = true })];

        var switchable = isReadOnly || element is not BpmnFlowNode node
            ? []
            : SwitchOptionsFor(document, node);

        var snapshot = new BpmnElementSnapshot
        {
            ElementId = elementId,
            ElementType = TypeNameOf(element),
            Category = category,
            ContainerId = ContainerOf(document, element),
            PoolId = PoolOf(document, element),
            LaneId = LaneOf(document, elementId),
            Label = LabelOf(document, element),
            Properties = properties,
            EventDefinitions = element is BpmnFlowNode flowNode ? flowNode.EventDefinitions : [],
            References = References(document, element),
            Extensions = [.. (ExtrasOf(element)?.Extensions ?? []).Select(extension =>
                new BpmnExtensionSummary(extension.Namespace, extension.LocalName))],
            ExtensionProperties = isReadOnly
                ? [.. ExtensionProperties(element).Select(property => property with { IsReadOnly = true })]
                : ExtensionProperties(element),
            Bounds = shape?.Bounds,
            LabelBounds = shape?.Label?.Bounds ?? edge?.Label?.Bounds,
            Waypoints = edge?.Waypoints ?? [],
            Capabilities = isReadOnly
                ? BpmnElementCapabilities.None
                : CapabilitiesOf(document, element, category, switchable.Count > 1),
            Diagnostics = DiagnosticsFor(document, elementId),
            SwitchableTypes = switchable.Count > 1 ? switchable : [],
        };

        var originalRaw = snapshot.ExtensionProperties;
        BpmnElementSnapshot Finish(BpmnElementSnapshot result)
        {
            if (ExtrasOf(element) is not null)
                result = BpmnElementTemplatePropertiesProvider.Build(document, result, templateOptions, originalRaw);
            if (isReadOnly)
            {
                result = result with
                {
                    ExtensionGroups = result.ExtensionGroups.Select(group => group with
                    { Entries = group.Entries.Select(entry => entry with { IsReadOnly = true }).ToArray() }).ToArray()
                };
            }
            return (display ?? BpmnDisplayLocalizer.CurrentCulture).Snapshot(result);
        }

        if (extensionOptions is null || extensionOptions.Registry.Schemas.Count == 0
            || ExtrasOf(element) is not { } extras) return Finish(snapshot);

        var registry = extensionOptions.Registry;
        if (!extensionOptions.AlwaysOfferRegisteredSchemas)
        {
            registry = new Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema.BpmnExtensionRegistry();
            foreach (var schema in extensionOptions.Registry.Schemas)
                if (document.Definitions.NamespaceDeclarations.Values.Contains(schema.Uri)
                    || extras.Extensions.Any(root => root.Namespace == schema.Uri)
                    || extras.ForeignAttributes.Keys.Any(key => key.StartsWith("{" + schema.Uri + "}", StringComparison.Ordinal)))
                    registry.Register(schema);
        }
        var groups = new List<BpmnPropertyGroup>();
        var claimedRoots = new HashSet<(string Namespace, string LocalName)>();
        foreach (var builder in extensionOptions.GroupBuilders)
        {
            var result = builder.Build(snapshot, element, extras, registry, extensionOptions.LabelProvider);
            groups.AddRange(result.Groups);
            claimedRoots.UnionWith(result.ClaimedRoots);
        }

        var claimedIndices = extras.Extensions.Select((extension, index) => (extension, index))
            .Where(item => claimedRoots.Contains((item.extension.Namespace, item.extension.LocalName)))
            .Select(item => item.index.ToString(CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
        var raw = ExtensionProperties(element).Where(property =>
        {
            if (!property.Name.StartsWith(ExtensionPrefix, StringComparison.Ordinal)) return true;
            var path = property.Name[ExtensionPrefix.Length..];
            var separator = path.IndexOfAny(['/', '@', '#']);
            var rootIndex = separator < 0 ? path : path[..separator];
            return !claimedIndices.Contains(rootIndex);
        });

        return Finish(snapshot with
        {
            ExtensionGroups = isReadOnly
                ? groups.Select(group => group with { Entries = group.Entries.Select(entry => entry with { IsReadOnly = true }).ToArray() }).ToArray()
                : groups,
            ExtensionProperties = isReadOnly
                ? raw.Select(property => property with { IsReadOnly = true }).ToArray()
                : raw.ToArray(),
        });
    }

    /// <summary>The wire name for a flow node type, which is what the canvas and the icons key by.</summary>
    /// <param name="type">The type to name.</param>
    /// <returns>The camelCase BPMN element name.</returns>
    public static string WireNameOf(BpmnFlowNodeType type) => JsonSerializer.Serialize(type).Trim('"');

    /// <summary>The standard properties of one element, without the rest of what <c>Describe</c> works out.</summary>
    /// <remarks>
    /// Describing an element also validates the document, which is far more than a caller comparing
    /// property values needs when it asks about every element in turn.
    /// </remarks>
    /// <param name="document">The document to resolve against.</param>
    /// <param name="elementId">The element.</param>
    /// <returns>The properties, or an empty list when there is no such element.</returns>
    public static IReadOnlyList<BpmnPropertyDescriptor> PropertiesOf(BpmnDocument document, string elementId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(elementId);

        return document.Find(elementId) is { } element ? Properties(document, element) : [];
    }

    // --- identity ---------------------------------------------------------------

    private static string TypeNameOf(object element) => element switch
    {
        BpmnFlowNode node => WireNameOf(node.Type),
        BpmnSequenceFlow => "sequenceFlow",
        BpmnMessageFlow => "messageFlow",
        BpmnAssociation => "association",
        BpmnDataAssociation association =>
            association.Kind == BpmnDataAssociationKind.Input ? "dataInputAssociation" : "dataOutputAssociation",
        BpmnTextAnnotation => "textAnnotation",
        BpmnGroup => "group",
        BpmnDataObjectReference => "dataObjectReference",
        BpmnDataStoreReference => "dataStoreReference",
        BpmnParticipant => "participant",
        BpmnLane => "lane",
        BpmnProcess => "process",
        BpmnCollaboration => "collaboration",
        _ => element.GetType().Name,
    };

    private static BpmnElementCategory CategoryOf(object element) => element switch
    {
        BpmnFlowNode node => CategoryOf(node.Type),
        BpmnSequenceFlow or BpmnMessageFlow or BpmnAssociation or BpmnDataAssociation => BpmnElementCategory.Connector,
        BpmnTextAnnotation or BpmnGroup => BpmnElementCategory.Artifact,
        BpmnDataObjectReference or BpmnDataStoreReference => BpmnElementCategory.Data,
        BpmnParticipant => BpmnElementCategory.Pool,
        BpmnLane => BpmnElementCategory.Lane,
        _ => BpmnElementCategory.Process,
    };

    /// <summary>The family a flow node type belongs to.</summary>
    /// <param name="type">The type to classify.</param>
    /// <returns>Its category.</returns>
    public static BpmnElementCategory CategoryOf(BpmnFlowNodeType type) => type switch
    {
        BpmnFlowNodeType.StartEvent
            or BpmnFlowNodeType.EndEvent
            or BpmnFlowNodeType.IntermediateCatchEvent
            or BpmnFlowNodeType.IntermediateThrowEvent
            or BpmnFlowNodeType.BoundaryEvent => BpmnElementCategory.Event,

        BpmnFlowNodeType.ExclusiveGateway
            or BpmnFlowNodeType.InclusiveGateway
            or BpmnFlowNodeType.ParallelGateway
            or BpmnFlowNodeType.ComplexGateway
            or BpmnFlowNodeType.EventBasedGateway => BpmnElementCategory.Gateway,

        _ => BpmnElementCategory.Activity,
    };

    // --- scope ------------------------------------------------------------------

    private static string? ContainerOf(BpmnDocument document, object element) => element switch
    {
        BpmnFlowNode node => node.ContainerId,
        BpmnSequenceFlow flow => flow.ContainerId,
        BpmnMessageFlow => document.Collaboration?.Id,
        BpmnAssociation association => association.ContainerId,
        BpmnDataAssociation association => association.ContainerId,
        BpmnTextAnnotation annotation => annotation.ContainerId,
        BpmnGroup group => group.ContainerId,
        BpmnDataObjectReference reference => reference.ContainerId,
        BpmnDataStoreReference reference => reference.ContainerId,
        BpmnParticipant => document.Collaboration?.Id,
        BpmnLane lane => document.LaneSets.FirstOrDefault(set => set.Id == lane.LaneSetId)?.ContainerId,
        _ => null,
    };

    private static string? PoolOf(BpmnDocument document, object element) => element switch
    {
        BpmnFlowNode node => node.PoolId,
        BpmnParticipant participant => participant.Id,
        BpmnLane lane => ParticipantForProcess(document, ContainerOf(document, lane)),
        _ => null,
    };

    private static string? ParticipantForProcess(BpmnDocument document, string? processId) =>
        processId is null
            ? null
            : document.Collaboration?.Participants
                .FirstOrDefault(participant => participant.ProcessRef == processId)?.Id;

    /// <summary>
    /// The Lane that claims this element.
    /// </summary>
    /// <remarks>
    /// Read from the Lane's <c>flowNodeRefs</c> rather than from the node's own <c>LaneId</c>
    /// projection. Membership is the Lane's statement about the node (section 2.8), and a snapshot
    /// that reported a stale projection would be exactly the "stale value as a standard field"
    /// section 7 forbids.
    /// </remarks>
    private static string? LaneOf(BpmnDocument document, string elementId) =>
        document.Lanes.FirstOrDefault(lane => lane.FlowNodeRefs.Contains(elementId))?.Id;

    private static string? LabelOf(BpmnDocument document, object element) => element switch
    {
        BpmnFlowNode node => node.Name,
        BpmnSequenceFlow flow => flow.Name,
        BpmnMessageFlow flow => flow.Name,
        BpmnTextAnnotation annotation => annotation.Text,
        BpmnGroup group => group.Name ?? CategoryValueText(document, group.CategoryValueRef),
        BpmnDataObjectReference reference => reference.Name,
        BpmnDataStoreReference reference => reference.Name,
        BpmnParticipant participant => participant.Name,
        BpmnLane lane => lane.Name,
        BpmnProcess process => process.Name,
        BpmnCollaboration collaboration => collaboration.Name,
        _ => null,
    };

    private static string? CategoryValueText(BpmnDocument document, string? categoryValueRef) =>
        categoryValueRef is not null && document.Find(categoryValueRef) is BpmnCategoryValue value
            ? value.Value
            : null;

    // --- DI ---------------------------------------------------------------------

    private static BpmnShape? ShapeOf(BpmnDocument document, string elementId) => document.Diagrams
        .SelectMany(diagram => diagram.Plane.Shapes)
        .FirstOrDefault(shape => shape.BpmnElement == elementId);

    private static BpmnEdge? EdgeOf(BpmnDocument document, string elementId) => document.Diagrams
        .SelectMany(diagram => diagram.Plane.Edges)
        .FirstOrDefault(edge => edge.BpmnElement == elementId);

    // --- diagnostics ------------------------------------------------------------

    /// <summary>
    /// The findings that name this element.
    /// </summary>
    /// <remarks>
    /// Model invariants only for now, which is what the document can answer today; the full
    /// section 5 validator arrives in M6 and plugs in here rather than into every surface.
    /// </remarks>
    private static IReadOnlyList<BpmnEditDiagnostic> DiagnosticsFor(BpmnDocument document, string elementId) =>
    [
        .. BpmnDocumentInvariants.Check(document)
            .Where(violation => violation.ElementId == elementId)
            .Select(violation => new BpmnEditDiagnostic(
                BpmnEditSeverity.Info,
                violation.RuleId,
                violation.Message,
                violation.ElementId)),
    ];

    // --- capabilities -----------------------------------------------------------

    private static BpmnElementCapabilities CapabilitiesOf(
        BpmnDocument document,
        object element,
        BpmnElementCategory category,
        bool canSwitch)
    {
        // A Group categorises; it never owns what it encloses (CT-003), so dragging it somewhere
        // else changes nothing but the Group.
        var isGroup = element is BpmnGroup;

        var canReparent = element switch
        {
            // CT-005: a Boundary Event moves by re-attaching to another host, not by changing
            // container, so offering "reparent" for one would offer the wrong operation.
            BpmnFlowNode { Type: BpmnFlowNodeType.BoundaryEvent } => false,
            BpmnFlowNode or BpmnDataObjectReference or BpmnDataStoreReference or BpmnTextAnnotation => true,
            _ => false,
        };

        var canResize = element switch
        {
            BpmnFlowNode node => CategoryOf(node.Type) == BpmnElementCategory.Activity,
            BpmnParticipant or BpmnLane or BpmnGroup or BpmnTextAnnotation => true,
            _ => false,
        };

        var canConnect = element is not BpmnGroup
            && category is BpmnElementCategory.Event
            or BpmnElementCategory.Activity
            or BpmnElementCategory.Gateway
            or BpmnElementCategory.Data
            or BpmnElementCategory.Artifact
            or BpmnElementCategory.Pool;

        return new BpmnElementCapabilities(
            CanEditLabel: Properties(document, element).Any(property => property.Name is "name" or "text"),
            CanEditProperties: true,
            CanSwitchType: canSwitch,
            CanDelete: element is not BpmnProcess and not BpmnCollaboration,
            CanResize: canResize,
            CanReparent: canReparent && !isGroup,
            CanConnect: canConnect);
    }

    // --- standard properties ----------------------------------------------------

    private static readonly IReadOnlyList<BpmnPropertyChoice> LoopChoices =
    [
        new("none", "None") { LabelResourceKey = "PropertyChoice.loopCharacteristics.none" },
        new("standard", "Loop") { LabelResourceKey = "PropertyChoice.loopCharacteristics.standard" },
        new("multiInstanceSequential", "Multi-instance (sequential)") { LabelResourceKey = "PropertyChoice.loopCharacteristics.multiInstanceSequential" },
        new("multiInstanceParallel", "Multi-instance (parallel)") { LabelResourceKey = "PropertyChoice.loopCharacteristics.multiInstanceParallel" },
    ];

    private static readonly IReadOnlyList<BpmnPropertyChoice> MultiInstanceBehaviorChoices =
    [
        new(string.Empty, "(none)") { LabelResourceKey = "PropertyChoice.Empty" },
        new("All", "All") { LabelResourceKey = "PropertyChoice.multiInstance.behavior.All" },
        new("One", "One") { LabelResourceKey = "PropertyChoice.multiInstance.behavior.One" },
        new("None", "None") { LabelResourceKey = "PropertyChoice.multiInstance.behavior.None" },
        new("Complex", "Complex") { LabelResourceKey = "PropertyChoice.multiInstance.behavior.Complex" },
    ];

    private static readonly IReadOnlyList<BpmnPropertyChoice> DirectionChoices =
    [
        new("none", "None") { LabelResourceKey = "PropertyChoice.associationDirection.none" },
        new("one", "One") { LabelResourceKey = "PropertyChoice.associationDirection.one" },
        new("both", "Both") { LabelResourceKey = "PropertyChoice.associationDirection.both" },
    ];

    private static readonly IReadOnlyList<BpmnPropertyChoice> ProcessTypeChoices =
    [
        new("None", "None") { LabelResourceKey = "PropertyChoice.processType.None" },
        new("Public", "Public") { LabelResourceKey = "PropertyChoice.processType.Public" },
        new("Private", "Private") { LabelResourceKey = "PropertyChoice.processType.Private" },
    ];

    /// <summary>
    /// The standard BPMN properties of an element, in display order.
    /// </summary>
    /// <remarks>
    /// Internal because it is two things at once: what a surface binds to, and the schema
    /// <see cref="BpmnDocumentEditor"/> validates an <see cref="UpdatePropertiesCommand"/> against.
    /// Keeping them the same list is what stops a property from being editable in the menu and
    /// unknown to the editor.
    /// </remarks>
    /// <param name="document">The document, for resolving choice lists.</param>
    /// <param name="element">The element to describe.</param>
    /// <returns>The descriptors.</returns>
    internal static IReadOnlyList<BpmnPropertyDescriptor> Properties(BpmnDocument document, object element)
    {
        var standard = StandardProperties(document, element);

        // Documentation is the one field every BPMN element has and none of them has for its own
        // reasons, so it is appended once here rather than repeated down the switch. It goes last
        // because it is prose: putting a text area above the attributes pushes them off the panel.
        return ExtrasOf(element) is { } extras
            ? [.. standard, new BpmnPropertyDescriptor(
                "documentation",
                "Documentation",
                BpmnPropertyKind.MultilineText,
                extras.Documentation)]
            : standard;
    }

    /// <summary>
    /// Every value the element's preserved extension content holds, addressed by path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept apart from <see cref="Properties"/> on purpose. Section 7 forbids presenting extension
    /// content as a standard field, and the separation is what enforces it: a surface binds two
    /// lists and cannot mix them up, and every extension name carries the
    /// <see cref="ExtensionPrefix"/> so it can never collide with a BPMN attribute.
    /// </para>
    /// <para>
    /// Editable, though. Section 6.1 requires extension content to be preserved whether or not the
    /// editor understands it; that is a statement about not losing it, not about refusing to let
    /// anyone change it. What the editor declines to do is <em>interpret</em> it — every value is
    /// offered as text, with no schema, no choice list and no validation beyond the structure the
    /// XML already has, because a vendor's meaning is the vendor's to know.
    /// </para>
    /// <para>
    /// The path is by child index rather than by name because names repeat: a <c>zeebe:ioMapping</c>
    /// holds several <c>zeebe:input</c> elements and addressing them by name would edit whichever
    /// one happened to be first.
    /// </para>
    /// </remarks>
    /// <param name="element">The element whose extension content to describe.</param>
    /// <returns>One descriptor per attribute and per leaf text value, depth first.</returns>
    internal static IReadOnlyList<BpmnPropertyDescriptor> ExtensionProperties(object element)
    {
        if (ExtrasOf(element) is not { Extensions.Count: > 0 } extras) return [];

        var found = new List<BpmnPropertyDescriptor>();

        Describe(extras.Extensions, string.Empty, string.Empty, found);

        return found;
    }

    /// <summary>The marker that makes an extension path unmistakable for a BPMN attribute.</summary>
    internal const string ExtensionPrefix = "extension:";

    private static void Describe(
        IReadOnlyList<BpmnExtensionElement> elements,
        string path,
        string trail,
        List<BpmnPropertyDescriptor> found)
    {
        for (var index = 0; index < elements.Count; index++)
        {
            var element = elements[index];
            var here = path.Length == 0
                ? index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : $"{path}/{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

            var name = Occurrence(elements, index) is { } ordinal
                ? $"{element.LocalName}[{ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)}]"
                : element.LocalName;
            var label = trail.Length == 0 ? name : $"{trail} / {name}";

            // Ordered by name so the form does not reshuffle itself when a dictionary rehashes.
            foreach (var attribute in element.Attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                found.Add(new BpmnPropertyDescriptor(
                    $"{ExtensionPrefix}{here}@{attribute.Key}",
                    $"{label} @{attribute.Key}",
                    BpmnPropertyKind.Text,
                    attribute.Value));
            }

            // Text is only a value on a leaf; on a container it is the whitespace between children,
            // which is the reader's own rule and has to stay the same one here.
            if (element.Children.Count == 0 && element.Text is not null)
            {
                found.Add(new BpmnPropertyDescriptor(
                    $"{ExtensionPrefix}{here}#text",
                    $"{label} text",
                    BpmnPropertyKind.MultilineText,
                    element.Text));
            }

            Describe(element.Children, here, label, found);
        }
    }

    /// <summary>
    /// The one-based position of an element among its same-named siblings, or null when it is the
    /// only one of its name.
    /// </summary>
    /// <remarks>
    /// Only shown when it disambiguates. Labelling a lone <c>taskDefinition</c> as
    /// <c>taskDefinition[1]</c> is noise; labelling the second of three <c>input</c> elements is
    /// the only way to tell the reader which one they are editing.
    /// </remarks>
    private static int? Occurrence(IReadOnlyList<BpmnExtensionElement> elements, int index)
    {
        var name = elements[index].LocalName;
        var total = 0;
        var ordinal = 0;

        for (var scan = 0; scan < elements.Count; scan++)
        {
            if (!string.Equals(elements[scan].LocalName, name, StringComparison.Ordinal)) continue;

            total++;
            if (scan == index) ordinal = total;
        }

        return total > 1 ? ordinal : null;
    }

    /// <summary>The extension carrier of any element that has one.</summary>
    /// <remarks>
    /// A switch rather than an interface because the model deliberately declares <c>Extras</c> on
    /// each record that can carry it: a type that cannot carry extensions must not be able to look
    /// as though it can.
    /// </remarks>
    private static Model.Extensions.BpmnElementExtras? ExtrasOf(object element) => element switch
    {
        BpmnFlowNode node => node.Extras,
        BpmnSequenceFlow flow => flow.Extras,
        BpmnMessageFlow flow => flow.Extras,
        BpmnAssociation association => association.Extras,
        BpmnTextAnnotation annotation => annotation.Extras,
        BpmnGroup group => group.Extras,
        BpmnDataObjectReference reference => reference.Extras,
        BpmnDataStoreReference reference => reference.Extras,
        BpmnParticipant participant => participant.Extras,
        BpmnLane lane => lane.Extras,
        BpmnProcess process => process.Extras,
        BpmnCollaboration collaboration => collaboration.Extras,
        _ => null,
    };

    private static IReadOnlyList<BpmnPropertyDescriptor> StandardProperties(BpmnDocument document, object element) =>
        element switch
        {
            BpmnFlowNode node => FlowNodeProperties(document, node),
            BpmnSequenceFlow flow => SequenceFlowProperties(flow),
            BpmnMessageFlow flow =>
            [
                Text("name", "Name", flow.Name),
                Choice("messageRef", "Message", flow.MessageRef, Optional(document.Catalog.Messages
                    .Select(message => new BpmnPropertyChoice(message.Id, message.Name ?? message.Id))), BpmnPropertyGroupKind.MessageFlow),
            ],
            BpmnAssociation association =>
            [
                Choice(
                    "associationDirection",
                    "Direction",
                    Wire(association.Direction),
                    DirectionChoices, BpmnPropertyGroupKind.Association),
            ],
            BpmnDataAssociation association => DataAssociationProperties(association),
            BpmnTextAnnotation annotation =>
            [
                new("text", "Text", BpmnPropertyKind.MultilineText, annotation.Text) { Group = BpmnPropertyGroupKind.TextAnnotation },
                Text("textFormat", "Text format", annotation.TextFormat, BpmnPropertyGroupKind.TextAnnotation),
            ],
            BpmnGroup group =>
            [
                Text("name", "Name", group.Name),
                Choice("categoryValueRef", "Category", group.CategoryValueRef, Optional(document.Catalog.Categories
                    .SelectMany(category => category.Values)
                    .Select(value => new BpmnPropertyChoice(value.Id, value.Value ?? value.Id))), BpmnPropertyGroupKind.Group),
            ],
            BpmnDataObjectReference reference =>
            [
                Text("name", "Name", reference.Name),
                Text("state", "State", reference.State, BpmnPropertyGroupKind.DataState),
                Flag("isCollection", "Collection", reference.IsCollection, BpmnPropertyGroupKind.DataObjectReference),
                Choice("dataObjectRef", "Data Object", reference.DataObjectRef, Optional(document.DataObjects
                    .Select(dataObject => new BpmnPropertyChoice(dataObject.Id, dataObject.Name ?? dataObject.Id))), BpmnPropertyGroupKind.DataObjectReference),
            ],
            BpmnDataStoreReference reference =>
            [
                Text("name", "Name", reference.Name),
                Text("state", "State", reference.State, BpmnPropertyGroupKind.DataState),
                Choice("dataStoreRef", "Data Store", reference.DataStoreRef, Optional(document.Catalog.DataStores
                    .Select(store => new BpmnPropertyChoice(store.Id, store.Name ?? store.Id))), BpmnPropertyGroupKind.DataStoreReference),
            ],
            BpmnParticipant participant => [Text("name", "Name", participant.Name)],
            BpmnLane lane => [Text("name", "Name", lane.Name)],
            BpmnProcess process =>
            [
                Text("name", "Name", process.Name),
                Flag("isExecutable", "Executable", process.IsExecutable, BpmnPropertyGroupKind.Process),
                Choice("processType", "Process type", process.ProcessType, ProcessTypeChoices, BpmnPropertyGroupKind.Process),
                Flag("isClosed", "Closed", process.IsClosed, BpmnPropertyGroupKind.Process),
            ],
            BpmnCollaboration collaboration => [Text("name", "Name", collaboration.Name)],
            _ => [],
        };

    private static List<BpmnPropertyDescriptor> FlowNodeProperties(BpmnDocument document, BpmnFlowNode node)
    {
        var properties = new List<BpmnPropertyDescriptor> { Text("name", "Name", node.Name) };
        var category = CategoryOf(node.Type);

        if (category == BpmnElementCategory.Event)
        {
            properties.Add(Choice(
                "eventDefinition",
                "Trigger",
                Wire(node.EventDefinition),
                [.. SupportedTriggersFor(document, node).Select(trigger => new BpmnPropertyChoice(
                    Wire(trigger),
                    TriggerLabel(trigger)) { LabelResourceKey = $"EventTrigger.{trigger}" })], BpmnPropertyGroupKind.Event));

            // The dashed ring is only meaningful where an event can decline to interrupt: a
            // Boundary Event and an Event Sub-Process Start Event. Offering the flag elsewhere
            // would offer a value the notation never draws.
            if (node.Type == BpmnFlowNodeType.BoundaryEvent || IsEventSubProcessStart(document, node))
            {
                properties.Add(Flag("isInterrupting", "Interrupting", node.IsInterrupting, BpmnPropertyGroupKind.Event));
            }
        }

        if (category == BpmnElementCategory.Activity)
        {
            properties.Add(Choice("loopCharacteristics", "Loop", Wire(node.LoopCharacteristics), LoopChoices, BpmnPropertyGroupKind.LoopCharacteristics));
            properties.Add(Flag("isForCompensation", "Compensation handler", node.IsForCompensation, BpmnPropertyGroupKind.Activity));

            if (node.LoopCharacteristics == BpmnLoopCharacteristics.Standard || node.StandardLoop is not null)
            {
                var standard = node.StandardLoop ?? new BpmnStandardLoopDetails();
                properties.Add(Flag("standardLoop.testBefore", "Test before", standard.TestBefore, BpmnPropertyGroupKind.LoopCharacteristics));
                properties.Add(Text(
                    "standardLoop.loopMaximum",
                    "Loop maximum",
                    standard.LoopMaximum?.ToString(CultureInfo.InvariantCulture), BpmnPropertyGroupKind.LoopCharacteristics));
                properties.AddRange(ExpressionProperties(
                    "standardLoop.loopCondition",
                    "Loop condition",
                    standard.LoopCondition, BpmnPropertyGroupKind.LoopCharacteristics));
            }

            if (node.LoopCharacteristics is BpmnLoopCharacteristics.MultiInstanceParallel
                or BpmnLoopCharacteristics.MultiInstanceSequential || node.MultiInstanceLoop is not null)
            {
                var multi = node.MultiInstanceLoop ?? new BpmnMultiInstanceLoopDetails();
                properties.Add(Flag("multiInstance.isSequential", "Sequential", multi.IsSequential, BpmnPropertyGroupKind.LoopCharacteristics));
                properties.Add(Choice(
                    "multiInstance.behavior",
                    "Multi-instance behavior",
                    multi.Behavior,
                    MultiInstanceBehaviorChoices, BpmnPropertyGroupKind.LoopCharacteristics));
                properties.AddRange(ExpressionProperties(
                    "multiInstance.loopCardinality",
                    "Loop cardinality",
                    multi.LoopCardinality, BpmnPropertyGroupKind.LoopCharacteristics));
                properties.Add(Text(
                    "multiInstance.loopDataInputRef",
                    "Loop data input reference",
                    multi.LoopDataInputRef, BpmnPropertyGroupKind.LoopCharacteristics));
                properties.AddRange(LoopDataItemProperties(
                    "multiInstance.inputDataItem",
                    "Input data item",
                    multi.InputDataItem));
                properties.AddRange(ExpressionProperties(
                    "multiInstance.completionCondition",
                    "Completion condition",
                    multi.CompletionCondition, BpmnPropertyGroupKind.LoopCharacteristics));
                properties.Add(Text(
                    "multiInstance.loopDataOutputRef",
                    "Loop data output reference",
                    multi.LoopDataOutputRef, BpmnPropertyGroupKind.LoopCharacteristics));
                properties.AddRange(LoopDataItemProperties(
                    "multiInstance.outputDataItem",
                    "Output data item",
                    multi.OutputDataItem));

                var behaviors = multi.ComplexBehaviorDefinitions ?? [];
                for (var index = 0; index < behaviors.Count; index++)
                {
                    var prefix = $"multiInstance.complexBehavior.{index}";
                    properties.Add(Text(
                        $"{prefix}.event",
                        $"Complex behavior {index + 1} event",
                        behaviors[index].Event, BpmnPropertyGroupKind.ComplexBehaviorDefinition));
                    properties.AddRange(ExpressionProperties(
                        $"{prefix}.condition",
                        $"Complex behavior {index + 1} condition",
                        behaviors[index].Condition, BpmnPropertyGroupKind.ComplexBehaviorDefinition));
                }
            }

            if (node.Type == BpmnFlowNodeType.CallActivity)
            {
                // CT-010 makes this reference the element's whole identity, so it is offered as a
                // closed list: a typed id that resolves to nothing is the failure the picker exists
                // to prevent.
                properties.Add(Choice(
                    "calledElementRef",
                    "Calls",
                    node.CalledElementRef,
                    [.. BpmnDocumentEditor.CallableDefinitions(document)
                        .Select(definition => new BpmnPropertyChoice(definition.Id, definition.Name ?? definition.Id))], BpmnPropertyGroupKind.CallActivity));
            }

            if (node.Type == BpmnFlowNodeType.ScriptTask)
            {
                properties.Add(Text("scriptFormat", "Script format", node.ScriptFormat, BpmnPropertyGroupKind.ScriptTask));
                properties.Add(new("script", "Script", BpmnPropertyKind.MultilineText, node.Script) { Group = BpmnPropertyGroupKind.ScriptTask });
            }

            if (node.Type is BpmnFlowNodeType.SendTask or BpmnFlowNodeType.ReceiveTask)
            {
                properties.Add(Choice("messageRef", "Message", node.MessageRef, Optional(document.Catalog.Messages
                    .Select(message => new BpmnPropertyChoice(message.Id, message.Name ?? message.Id))),
                    node.Type == BpmnFlowNodeType.SendTask ? BpmnPropertyGroupKind.SendTask : BpmnPropertyGroupKind.ReceiveTask));
            }
        }

        if (node.Type == BpmnFlowNodeType.ComplexGateway)
        {
            properties.AddRange(ExpressionProperties(
                "activationCondition",
                "Activation condition",
                node.ActivationCondition, BpmnPropertyGroupKind.ComplexGateway));
        }

        // A default flow is the "otherwise" branch of a decision, so it belongs to the kinds that
        // decide: the conditional Gateways, and an Activity with conditional outgoing flows.
        if (node.Type is BpmnFlowNodeType.ExclusiveGateway
            or BpmnFlowNodeType.InclusiveGateway
            or BpmnFlowNodeType.ComplexGateway
            || category == BpmnElementCategory.Activity)
        {
            var outgoing = document.SequenceFlows.Where(flow => flow.SourceRef == node.Id).ToList();

            if (outgoing.Count > 1)
            {
                properties.Add(Choice("defaultFlowRef", "Default flow", node.DefaultFlowRef, Optional(
                    outgoing.Select(flow => new BpmnPropertyChoice(flow.Id, flow.Name ?? flow.Id))),
                    category == BpmnElementCategory.Activity ? BpmnPropertyGroupKind.Activity
                        : node.Type == BpmnFlowNodeType.ComplexGateway ? BpmnPropertyGroupKind.ComplexGateway
                        : BpmnPropertyGroupKind.Gateway));
            }
        }

        return properties;
    }

    private static List<BpmnPropertyDescriptor> DataAssociationProperties(BpmnDataAssociation association)
    {
        var properties = new List<BpmnPropertyDescriptor>();
        for (var index = 0; index < association.Assignments.Count; index++)
        {
            var assignment = association.Assignments[index];
            properties.AddRange(ExpressionProperties(
                $"assignment.{index}.from",
                $"Assignment {index + 1} from",
                assignment.From, BpmnPropertyGroupKind.Assignment));
            properties.AddRange(ExpressionProperties(
                $"assignment.{index}.to",
                $"Assignment {index + 1} to",
                assignment.To, BpmnPropertyGroupKind.Assignment));
        }

        return properties;
    }

    private static IReadOnlyList<BpmnPropertyDescriptor> LoopDataItemProperties(
        string prefix,
        string label,
        BpmnLoopDataItem? item)
    {
        item ??= new BpmnLoopDataItem();
        return
        [
            Text($"{prefix}.id", $"{label} id", item.Id, BpmnPropertyGroupKind.LoopCharacteristics),
            Text($"{prefix}.name", $"{label} name", item.Name, BpmnPropertyGroupKind.LoopCharacteristics),
            Text($"{prefix}.itemSubjectRef", $"{label} item subject reference", item.ItemSubjectRef, BpmnPropertyGroupKind.LoopCharacteristics),
            Flag($"{prefix}.isCollection", $"{label} is collection", item.IsCollection, BpmnPropertyGroupKind.LoopCharacteristics),
        ];
    }

    private static IReadOnlyList<BpmnPropertyDescriptor> ExpressionProperties(
        string name,
        string label,
        BpmnFormalExpression? expression,
        BpmnPropertyGroupKind group) =>
    [
        new(name, label, BpmnPropertyKind.MultilineText, expression?.Body) { Group = group },
        Text($"{name}.language", $"{label} language", expression?.Language, group),
        Text($"{name}.type", $"{label} type", expression?.TypeQName, group),
    ];

    private static IReadOnlyList<BpmnPropertyDescriptor> SequenceFlowProperties(BpmnSequenceFlow flow) =>
    [
        Text("name", "Name", flow.Name),
        Flag("isDefault", "Default flow", flow.IsDefault, BpmnPropertyGroupKind.SequenceFlow),

        // SF-006: a default flow carries no condition. The property stays visible on one so the
        // user can see the condition that has to go before the flow can become the default.
        new("conditionExpression", "Condition", BpmnPropertyKind.MultilineText, flow.ConditionExpression) { Group = BpmnPropertyGroupKind.SequenceFlow },
        Text("conditionLanguage", "Condition language", flow.ConditionLanguage, BpmnPropertyGroupKind.SequenceFlow),
    ];

    private static bool IsEventSubProcessStart(BpmnDocument document, BpmnFlowNode node) =>
        node.Type == BpmnFlowNodeType.StartEvent
        && document.FindFlowNode(node.ContainerId) is { Type: BpmnFlowNodeType.EventSubProcess };

    private static BpmnPropertyDescriptor Text(string name, string label, string? value,
        BpmnPropertyGroupKind group = BpmnPropertyGroupKind.General) =>
        new(name, label, BpmnPropertyKind.Text, value) { Group = group };

    private static BpmnPropertyDescriptor Flag(string name, string label, bool? value,
        BpmnPropertyGroupKind group = BpmnPropertyGroupKind.General) =>
        new(name, label, BpmnPropertyKind.Boolean, value is null ? null : value.Value ? "true" : "false") { Group = group };

    private static BpmnPropertyDescriptor Choice(
        string name,
        string label,
        string? value,
        IReadOnlyList<BpmnPropertyChoice> choices,
        BpmnPropertyGroupKind group = BpmnPropertyGroupKind.General) =>
        new(name, label, BpmnPropertyKind.Choice, value, Choices: choices) { Group = group };

    /// <summary>An optional reference needs a way to say "none", which an empty value is.</summary>
    private static IReadOnlyList<BpmnPropertyChoice> Optional(IEnumerable<BpmnPropertyChoice> choices) =>
        [new(string.Empty, "(none)") { LabelResourceKey = "PropertyChoice.Empty" }, .. choices];

    private static string Wire<TEnum>(TEnum value) where TEnum : struct, Enum =>
        JsonSerializer.Serialize(value).Trim('"');

    // --- references -------------------------------------------------------------

    private static List<BpmnElementReference> References(BpmnDocument document, object element)
    {
        var references = new List<BpmnElementReference>();

        void Add(string name, string? targetId)
        {
            if (string.IsNullOrEmpty(targetId)) return;

            var target = document.Find(targetId);
            references.Add(new BpmnElementReference(name, targetId, target is null ? null : LabelOf(document, target), target is not null));
        }

        switch (element)
        {
            case BpmnFlowNode node:
                Add("attachedToRef", node.AttachedToRef);
                Add("calledElementRef", node.CalledElementRef);
                Add("messageRef", node.MessageRef);
                Add("defaultFlowRef", node.DefaultFlowRef);
                foreach (var definition in node.EventDefinitions) Add(RefNameOf(definition.Type), definition.Ref);
                break;

            case BpmnSequenceFlow flow:
                Add("sourceRef", flow.SourceRef);
                Add("targetRef", flow.TargetRef);
                break;

            case BpmnMessageFlow flow:
                Add("sourceRef", flow.SourceRef);
                Add("targetRef", flow.TargetRef);
                Add("messageRef", flow.MessageRef);
                break;

            case BpmnAssociation association:
                Add("sourceRef", association.SourceRef);
                Add("targetRef", association.TargetRef);
                break;

            case BpmnDataAssociation association:
                foreach (var source in association.SourceRefs) Add("sourceRef", source);
                Add("targetRef", association.TargetRef);
                break;

            case BpmnDataObjectReference reference:
                Add("dataObjectRef", reference.DataObjectRef);
                break;

            case BpmnDataStoreReference reference:
                Add("dataStoreRef", reference.DataStoreRef);
                break;

            case BpmnGroup group:
                Add("categoryValueRef", group.CategoryValueRef);
                break;

            case BpmnParticipant participant:
                Add("processRef", participant.ProcessRef);
                break;

            default:
                break;
        }

        return references;
    }

    private static string RefNameOf(BpmnEventDefinitionType type) => type switch
    {
        BpmnEventDefinitionType.Message => "messageRef",
        BpmnEventDefinitionType.Error => "errorRef",
        BpmnEventDefinitionType.Signal => "signalRef",
        BpmnEventDefinitionType.Escalation => "escalationRef",
        _ => "ref",
    };

    // --- type switching ---------------------------------------------------------

    /// <summary>
    /// The set of types an element may be converted between.
    /// </summary>
    /// <param name="type">The type to group.</param>
    /// <returns>Its switch group, or <see cref="BpmnSwitchGroup.None"/>.</returns>
    public static BpmnSwitchGroup SwitchGroupOf(BpmnFlowNodeType type) => type switch
    {
        BpmnFlowNodeType.Task
            or BpmnFlowNodeType.UserTask
            or BpmnFlowNodeType.ServiceTask
            or BpmnFlowNodeType.ScriptTask
            or BpmnFlowNodeType.ManualTask
            or BpmnFlowNodeType.BusinessRuleTask
            or BpmnFlowNodeType.SendTask
            or BpmnFlowNodeType.ReceiveTask => BpmnSwitchGroup.Task,

        BpmnFlowNodeType.ExclusiveGateway
            or BpmnFlowNodeType.InclusiveGateway
            or BpmnFlowNodeType.ParallelGateway
            or BpmnFlowNodeType.ComplexGateway
            or BpmnFlowNodeType.EventBasedGateway => BpmnSwitchGroup.Gateway,

        BpmnFlowNodeType.SubProcess
            or BpmnFlowNodeType.Transaction
            or BpmnFlowNodeType.AdHocSubProcess => BpmnSwitchGroup.EmbeddedSubProcess,

        BpmnFlowNodeType.StartEvent
            or BpmnFlowNodeType.EndEvent
            or BpmnFlowNodeType.IntermediateCatchEvent
            or BpmnFlowNodeType.IntermediateThrowEvent
            or BpmnFlowNodeType.BoundaryEvent => BpmnSwitchGroup.EventTrigger,

        // An Event Sub-Process is not an ordinary Sub-Process — no Sequence Flow crosses its
        // boundary — and a Call Activity's identity is the reference it carries. Neither converts
        // into anything while keeping what section 7 promises to keep.
        _ => BpmnSwitchGroup.None,
    };

    /// <summary>
    /// What this element may be switched to, its own type included and marked.
    /// </summary>
    /// <remarks>
    /// A single-entry list means "only itself", which the caller reads as not switchable. The
    /// element's own type is present so a menu can show the group with the current member ticked
    /// rather than showing a list the current type is mysteriously missing from.
    /// </remarks>
    /// <param name="document">The document, for the context a trigger's validity depends on.</param>
    /// <param name="node">The node to offer alternatives for.</param>
    /// <returns>The options, in display order.</returns>
    public static IReadOnlyList<BpmnTypeOption> SwitchOptionsFor(BpmnDocument document, BpmnFlowNode node)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);

        return SwitchGroupOf(node.Type) switch
        {
            BpmnSwitchGroup.Task =>
            [
                .. TaskTypes.Select(type => new BpmnTypeOption(
                    type,
                    BpmnEventDefinitionType.None,
                    TypeLabel(type),
                    type == node.Type)),
            ],

            BpmnSwitchGroup.Gateway =>
            [
                .. GatewayTypes.Select(type => new BpmnTypeOption(
                    type,
                    BpmnEventDefinitionType.None,
                    TypeLabel(type),
                    type == node.Type)),
            ],

            BpmnSwitchGroup.EmbeddedSubProcess =>
            [
                .. SubProcessTypes.Select(type => new BpmnTypeOption(
                    type,
                    BpmnEventDefinitionType.None,
                    TypeLabel(type),
                    type == node.Type)),
            ],

            BpmnSwitchGroup.EventTrigger =>
            [
                .. SupportedTriggersFor(document, node).Select(trigger => new BpmnTypeOption(
                    node.Type,
                    trigger,
                    $"{TriggerLabel(trigger)} {TypeLabel(node.Type)}",
                    trigger == node.EventDefinition)),
            ],

            _ => [],
        };

    }

    private static readonly BpmnFlowNodeType[] TaskTypes =
    [
        BpmnFlowNodeType.Task,
        BpmnFlowNodeType.UserTask,
        BpmnFlowNodeType.ServiceTask,
        BpmnFlowNodeType.SendTask,
        BpmnFlowNodeType.ReceiveTask,
        BpmnFlowNodeType.ScriptTask,
        BpmnFlowNodeType.ManualTask,
        BpmnFlowNodeType.BusinessRuleTask,
    ];

    private static readonly BpmnFlowNodeType[] GatewayTypes =
    [
        BpmnFlowNodeType.ExclusiveGateway,
        BpmnFlowNodeType.ParallelGateway,
        BpmnFlowNodeType.InclusiveGateway,
        BpmnFlowNodeType.ComplexGateway,
        BpmnFlowNodeType.EventBasedGateway,
    ];

    private static readonly BpmnFlowNodeType[] SubProcessTypes =
    [
        BpmnFlowNodeType.SubProcess,
        BpmnFlowNodeType.Transaction,
        BpmnFlowNodeType.AdHocSubProcess,
    ];

    /// <summary>
    /// The triggers this Event may carry in its current container, host and interruption context.
    /// </summary>
    /// <remarks>
    /// BPMN 2.0.2 tables 10.84–10.92. A top-level Start Event, a Start Event in an ordinary
    /// Sub-Process and one in an Event Sub-Process have different trigger sets; Cancel also belongs
    /// to a Transaction only (EV-002).
    /// </remarks>
    /// <param name="document">The document, for the container the rules above depend on.</param>
    /// <param name="node">The Event.</param>
    /// <returns>Its legal triggers, in notation order, for the supplied interruption state.</returns>
    public static IReadOnlyList<BpmnEventDefinitionType> TriggersFor(BpmnDocument document, BpmnFlowNode node)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);

        return SupportedTriggersFor(document, node)
            .Where(trigger => IsInterruptionLegal(document, node with { EventDefinition = trigger }))
            .ToArray();
    }

    /// <summary>
    /// The triggers supported by an Event's type, container and Boundary host, without filtering
    /// for the current interruption state.
    /// </summary>
    /// <param name="document">The document, for the container and Boundary host rules.</param>
    /// <param name="node">The Event being checked.</param>
    /// <returns>Triggers supported in this structural context, in notation order.</returns>
    public static IReadOnlyList<BpmnEventDefinitionType> SupportedTriggersFor(
        BpmnDocument document,
        BpmnFlowNode node)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);

        return TriggerOptionsForContext(document, node);
    }

    /// <summary>
    /// Whether a trigger is supported by an Event's type, container and Boundary host, ignoring
    /// whether the current interruption state is compatible with that trigger.
    /// </summary>
    /// <param name="document">The document, for the container and Boundary host rules.</param>
    /// <param name="node">The Event being checked.</param>
    /// <param name="trigger">The trigger being checked.</param>
    /// <returns>Whether the trigger is supported in this structural context.</returns>
    public static bool IsTriggerLegalInContext(
        BpmnDocument document,
        BpmnFlowNode node,
        BpmnEventDefinitionType trigger)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);

        return SupportedTriggersFor(document, node).Contains(trigger);
    }

    private static BpmnEventDefinitionType[] TriggerOptionsForContext(
        BpmnDocument document,
        BpmnFlowNode node)
    {
        if (!IsFlowElementsContainer(document, node.ContainerId))
        {
            return [];
        }

        if (node.Type == BpmnFlowNodeType.BoundaryEvent)
        {
            var host = node.AttachedToRef is null ? null : document.FindFlowNode(node.AttachedToRef);
            if (host is null
                || CategoryOf(host.Type) != BpmnElementCategory.Activity
                || host.ContainerId != node.ContainerId)
            {
                return [];
            }
        }
        else if (node.AttachedToRef is not null)
        {
            return [];
        }

        BpmnEventDefinitionType[] triggers = node.Type switch
        {
            BpmnFlowNodeType.StartEvent when IsEventSubProcessStart(document, node) =>
            [
                BpmnEventDefinitionType.Message,
                BpmnEventDefinitionType.Timer,
                BpmnEventDefinitionType.Error,
                BpmnEventDefinitionType.Escalation,
                BpmnEventDefinitionType.Compensation,
                BpmnEventDefinitionType.Conditional,
                BpmnEventDefinitionType.Signal,
                BpmnEventDefinitionType.Multiple,
                BpmnEventDefinitionType.ParallelMultiple,
            ],

            BpmnFlowNodeType.StartEvent when IsEmbeddedSubProcessStart(document, node) =>
            [
                BpmnEventDefinitionType.None,
            ],

            BpmnFlowNodeType.StartEvent =>
            [
                BpmnEventDefinitionType.None,
                BpmnEventDefinitionType.Message,
                BpmnEventDefinitionType.Timer,
                BpmnEventDefinitionType.Conditional,
                BpmnEventDefinitionType.Signal,
                BpmnEventDefinitionType.Multiple,
                BpmnEventDefinitionType.ParallelMultiple,
            ],

            // A None Intermediate Event is a throw, not a catch, which is why there is no None here.
            BpmnFlowNodeType.IntermediateCatchEvent =>
            [
                BpmnEventDefinitionType.Message,
                BpmnEventDefinitionType.Timer,
                BpmnEventDefinitionType.Conditional,
                BpmnEventDefinitionType.Link,
                BpmnEventDefinitionType.Signal,
                BpmnEventDefinitionType.Multiple,
                BpmnEventDefinitionType.ParallelMultiple,
            ],

            BpmnFlowNodeType.IntermediateThrowEvent =>
            [
                BpmnEventDefinitionType.None,
                BpmnEventDefinitionType.Message,
                BpmnEventDefinitionType.Escalation,
                BpmnEventDefinitionType.Compensation,
                BpmnEventDefinitionType.Link,
                BpmnEventDefinitionType.Signal,
                BpmnEventDefinitionType.Multiple,
            ],

            BpmnFlowNodeType.EndEvent =>
            [
                BpmnEventDefinitionType.None,
                BpmnEventDefinitionType.Message,
                BpmnEventDefinitionType.Error,
                BpmnEventDefinitionType.Escalation,
                .. InTransaction(document, node.ContainerId) ? (BpmnEventDefinitionType[])[BpmnEventDefinitionType.Cancel] : [],
                BpmnEventDefinitionType.Compensation,
                BpmnEventDefinitionType.Signal,
                BpmnEventDefinitionType.Terminate,
                BpmnEventDefinitionType.Multiple,
            ],

            BpmnFlowNodeType.BoundaryEvent =>
            [
                BpmnEventDefinitionType.Message,
                BpmnEventDefinitionType.Timer,
                BpmnEventDefinitionType.Error,
                BpmnEventDefinitionType.Escalation,
                .. IsTransaction(document, node.AttachedToRef) ? (BpmnEventDefinitionType[])[BpmnEventDefinitionType.Cancel] : [],
                BpmnEventDefinitionType.Compensation,
                BpmnEventDefinitionType.Conditional,
                BpmnEventDefinitionType.Signal,
                BpmnEventDefinitionType.Multiple,
                BpmnEventDefinitionType.ParallelMultiple,
            ],

            _ => [],
        };

        return triggers;
    }

    /// <summary>
    /// Whether an Event's trigger, container and interruption state are legal together.
    /// </summary>
    /// <remarks>
    /// BPMN 2.0.2 table 10.92 permits only Message, Timer, Escalation, Conditional, Signal, Multiple,
    /// and Parallel Multiple on non-interrupting Boundary Events. Table 10.87 requires Error Event
    /// Sub-Process starts to interrupt and Compensation starts not to interrupt. Interruption is
    /// otherwise ignored where BPMN does not define it.
    /// </remarks>
    /// <param name="document">The document, for the Event's container and Boundary host.</param>
    /// <param name="node">The Event being checked.</param>
    /// <returns><see langword="true"/> when the complete Event context is allowed.</returns>
    public static bool IsLegalEvent(BpmnDocument document, BpmnFlowNode node)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);

        if (!IsFlowElementsContainer(document, node.ContainerId)) return false;

        if (node.Type == BpmnFlowNodeType.BoundaryEvent)
        {
            var host = node.AttachedToRef is null ? null : document.FindFlowNode(node.AttachedToRef);
            if (host is null
                || CategoryOf(host.Type) != BpmnElementCategory.Activity
                || host.ContainerId != node.ContainerId)
            {
                return false;
            }
        }
        else if (node.AttachedToRef is not null)
        {
            return false;
        }

        return IsTriggerLegalInContext(document, node, node.EventDefinition)
            && IsInterruptionLegal(document, node);
    }

    /// <summary>
    /// Whether the Event's interruption state is legal for its trigger.
    /// </summary>
    /// <param name="document">The document, for the Event's container and Boundary host.</param>
    /// <param name="node">The Event being checked.</param>
    /// <returns>Whether its interruption state is allowed; ignored BPMN attributes are accepted.</returns>
    public static bool IsInterruptionLegal(BpmnDocument document, BpmnFlowNode node)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);

        if (node.Type == BpmnFlowNodeType.BoundaryEvent)
        {
            var boundaryTriggers = node.EventDefinition is BpmnEventDefinitionType.Multiple
                or BpmnEventDefinitionType.ParallelMultiple
                ? node.EventDefinitions.Select(definition => definition.Type).ToList()
                : [node.EventDefinition];

            // Compensation has no cancellation state in BPMN 2.0.2 table 10.92, so the model's
            // only supported representation is its default true value, not non-interrupting.
            // Multiple Boundary Events use the most restrictive trigger's requirement.
            return !boundaryTriggers.Any(trigger =>
                trigger is BpmnEventDefinitionType.Error
                    or BpmnEventDefinitionType.Cancel
                    or BpmnEventDefinitionType.Compensation)
                || node.IsInterrupting;
        }

        if (IsEventSubProcessStart(document, node))
        {
            return node.EventDefinition switch
            {
                BpmnEventDefinitionType.Error => node.IsInterrupting,
                BpmnEventDefinitionType.Compensation => !node.IsInterrupting,
                _ => true,
            };
        }

        return true;
    }

    internal static bool TryGetRequiredInterruptingValue(
        BpmnDocument document,
        BpmnFlowNode node,
        out bool isInterrupting)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);

        var multipleBoundaryRequiresInterruption =
            (node.EventDefinition is BpmnEventDefinitionType.Multiple or BpmnEventDefinitionType.ParallelMultiple)
            && node.EventDefinitions.Any(definition =>
                definition.Type is BpmnEventDefinitionType.Error
                    or BpmnEventDefinitionType.Cancel
                    or BpmnEventDefinitionType.Compensation);
        if (node.Type == BpmnFlowNodeType.BoundaryEvent
            && (node.EventDefinition is BpmnEventDefinitionType.Error or BpmnEventDefinitionType.Cancel
                or BpmnEventDefinitionType.Compensation
                || multipleBoundaryRequiresInterruption))
        {
            isInterrupting = true;
            return true;
        }

        if (IsEventSubProcessStart(document, node)
            && node.EventDefinition is BpmnEventDefinitionType.Error or BpmnEventDefinitionType.Compensation)
        {
            isInterrupting = node.EventDefinition == BpmnEventDefinitionType.Error;
            return true;
        }

        isInterrupting = default;
        return false;
    }

    private static bool InTransaction(BpmnDocument document, string containerId) =>
        document.FindFlowNode(containerId) is { Type: BpmnFlowNodeType.Transaction };

    private static bool IsEmbeddedSubProcessStart(BpmnDocument document, BpmnFlowNode node) =>
        node.Type == BpmnFlowNodeType.StartEvent
        && document.FindFlowNode(node.ContainerId) is
        {
            Type: BpmnFlowNodeType.SubProcess
                or BpmnFlowNodeType.Transaction
                or BpmnFlowNodeType.AdHocSubProcess,
        };

    private static bool IsFlowElementsContainer(BpmnDocument document, string containerId) =>
        document.Processes.Any(process => process.Id == containerId)
        || document.FindFlowNode(containerId) is { FlowElementsOwned: true };

    private static bool IsTransaction(BpmnDocument document, string? hostId) =>
        hostId is not null && document.FindFlowNode(hostId) is { Type: BpmnFlowNodeType.Transaction };

    /// <summary>A display name for a flow node type.</summary>
    /// <param name="type">The type to name.</param>
    /// <returns>Title-case notation wording.</returns>
    public static string TypeLabel(BpmnFlowNodeType type) => type switch
    {
        BpmnFlowNodeType.StartEvent => "Start Event",
        BpmnFlowNodeType.EndEvent => "End Event",
        BpmnFlowNodeType.IntermediateCatchEvent => "Catch Event",
        BpmnFlowNodeType.IntermediateThrowEvent => "Throw Event",
        BpmnFlowNodeType.BoundaryEvent => "Boundary Event",
        BpmnFlowNodeType.Task => "Task",
        BpmnFlowNodeType.UserTask => "User Task",
        BpmnFlowNodeType.ServiceTask => "Service Task",
        BpmnFlowNodeType.ScriptTask => "Script Task",
        BpmnFlowNodeType.ManualTask => "Manual Task",
        BpmnFlowNodeType.BusinessRuleTask => "Business Rule Task",
        BpmnFlowNodeType.SendTask => "Send Task",
        BpmnFlowNodeType.ReceiveTask => "Receive Task",
        BpmnFlowNodeType.SubProcess => "Sub-Process",
        BpmnFlowNodeType.EventSubProcess => "Event Sub-Process",
        BpmnFlowNodeType.Transaction => "Transaction",
        BpmnFlowNodeType.AdHocSubProcess => "Ad-Hoc Sub-Process",
        BpmnFlowNodeType.CallActivity => "Call Activity",
        BpmnFlowNodeType.ExclusiveGateway => "Exclusive Gateway",
        BpmnFlowNodeType.InclusiveGateway => "Inclusive Gateway",
        BpmnFlowNodeType.ParallelGateway => "Parallel Gateway",
        BpmnFlowNodeType.ComplexGateway => "Complex Gateway",
        BpmnFlowNodeType.EventBasedGateway => "Event-Based Gateway",
        _ => type.ToString(),
    };

    /// <summary>A display name for an event trigger.</summary>
    /// <param name="type">The trigger to name.</param>
    /// <returns>Notation wording, with <c>None</c> spelled out rather than left blank.</returns>
    public static string TriggerLabel(BpmnEventDefinitionType type) => type switch
    {
        BpmnEventDefinitionType.None => "None",
        BpmnEventDefinitionType.ParallelMultiple => "Parallel Multiple",
        _ => type.ToString(),
    };
}
