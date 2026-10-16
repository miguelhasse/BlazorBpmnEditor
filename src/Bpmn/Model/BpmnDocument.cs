using System.Collections.Frozen;
using System.Text.Json.Serialization;

using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Model;

/// <summary>
/// A complete BPMN document: semantic model plus DI layout.
/// </summary>
/// <remarks>
/// <para>
/// This is the editor's semantic model of record. The SVG canvas is a view over it and over the
/// DI layer, never the source of truth (specification section 10).
/// </para>
/// <para>
/// Flow elements are held in flat collections and located by <c>ContainerId</c>, exactly as
/// section 6 models containment, rather than nested. Nested Sub-Process content is therefore
/// preserved in place with no recursive structure to walk, and a container can be collapsed or
/// expanded without moving anything.
/// </para>
/// <para>
/// Immutable: every edit produces a new document. That is what makes undo, preview-before-commit
/// import, and the "reject atomically, restore exactly" behaviour the specification demands
/// cheap instead of requiring careful rollback code.
/// </para>
/// </remarks>
public sealed record BpmnDocument
{
    /// <summary>
    /// Element indexes, held outside the record.
    /// </summary>
    /// <remarks>
    /// Not a field. A record's <c>with</c> expression copies every field, including a cached
    /// index, so an edited document would inherit an index built from the document it was derived
    /// from — and every lookup for a newly created element would miss. A side table keyed on
    /// instance identity gives each document its own entry for free, and a weak key means a
    /// discarded document takes its index with it.
    /// </remarks>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        BpmnDocument,
        FrozenDictionary<string, object>> Indexes = new();

    /// <summary>Document-level metadata.</summary>
    public required BpmnDefinitions Definitions { get; init; }

    /// <summary>Process root elements.</summary>
    public IReadOnlyList<BpmnProcess> Processes { get; init; } = [];

    /// <summary>The Collaboration root element, when the document has one.</summary>
    public BpmnCollaboration? Collaboration { get; init; }

    /// <summary>Lane sets, which may be empty for a valid lane-less Process.</summary>
    public IReadOnlyList<BpmnLaneSet> LaneSets { get; init; } = [];

    /// <summary>Lanes belonging to the declared lane sets.</summary>
    public IReadOnlyList<BpmnLane> Lanes { get; init; } = [];

    /// <summary>Events, Activities and Gateways across every container.</summary>
    public IReadOnlyList<BpmnFlowNode> FlowNodes { get; init; } = [];

    /// <summary>Sequence Flows across every container.</summary>
    public IReadOnlyList<BpmnSequenceFlow> SequenceFlows { get; init; } = [];

    /// <summary>Message Flows, which belong to the Collaboration.</summary>
    public IReadOnlyList<BpmnMessageFlow> MessageFlows { get; init; } = [];

    /// <summary>Associations between Artifacts and flow elements.</summary>
    public IReadOnlyList<BpmnAssociation> Associations { get; init; } = [];

    /// <summary>Data Input and Data Output Associations.</summary>
    public IReadOnlyList<BpmnDataAssociation> DataAssociations { get; init; } = [];

    /// <summary>Text Annotations.</summary>
    public IReadOnlyList<BpmnTextAnnotation> TextAnnotations { get; init; } = [];

    /// <summary>Groups: visual categorisation only.</summary>
    public IReadOnlyList<BpmnGroup> Groups { get; init; } = [];

    /// <summary>Data Object References drawn on a plane.</summary>
    public IReadOnlyList<BpmnDataObjectReference> DataObjectReferences { get; init; } = [];

    /// <summary>Data Store References drawn on a plane.</summary>
    public IReadOnlyList<BpmnDataStoreReference> DataStoreReferences { get; init; } = [];

    /// <summary>Data Object definitions, which the References above point at.</summary>
    public IReadOnlyList<BpmnDataObject> DataObjects { get; init; } = [];

    /// <summary>
    /// The document-scoped definitions that are referenced but never drawn (section 1.0).
    /// </summary>
    public BpmnRootCatalog Catalog { get; init; } = BpmnRootCatalog.Empty;

    /// <summary>Diagrams, each owning one plane.</summary>
    public IReadOnlyList<BpmnDiagram> Diagrams { get; init; } = [];

    /// <summary>
    /// The XML this document was read from, when it was read from any.
    /// </summary>
    /// <remarks>
    /// Opaque, and the only thing here that is not semantics: it exists so a fidelity export can
    /// preserve the comments, document order and unknown attributes no model can hold
    /// (section 10.6). Never serialized to the canvas, and <see langword="null"/> for a document
    /// the editor built rather than imported.
    /// </remarks>
    [JsonIgnore]
    public BpmnSourceDocument? Source { get; init; }

    /// <summary>
    /// Resolves any element by id.
    /// </summary>
    /// <remarks>
    /// Specification section 1.0 requires the editor to keep an id index for reference
    /// resolution. It is built on first use and cached, because most documents are loaded and
    /// rendered without anything ever asking for a lookup.
    /// </remarks>
    [JsonIgnore]
    public FrozenDictionary<string, object> ElementIndex => Indexes.GetValue(this, static document => document.BuildIndex());

    /// <summary>Finds an element by id, or returns <see langword="null"/>.</summary>
    public object? Find(string id) => ElementIndex.GetValueOrDefault(id);

    /// <summary>Finds a flow node by id, or returns <see langword="null"/>.</summary>
    public BpmnFlowNode? FindFlowNode(string id) => Find(id) as BpmnFlowNode;

    private FrozenDictionary<string, object> BuildIndex()
    {
        var index = new Dictionary<string, object>(StringComparer.Ordinal);

        // Later duplicates are ignored here rather than throwing: a duplicate id is a document
        // defect that validation reports as D018, and a half-loaded document helps nobody
        // diagnose it.
        void Add(string id, object value) => index.TryAdd(id, value);

        foreach (var process in Processes) Add(process.Id, process);

        // The Collaboration is a root element with an id like any other, and things reference it:
        // an artifact created on a Collaboration plane is owned by it (section 9.1), so a lookup
        // that cannot find it makes that ownership unresolvable.
        if (Collaboration is { } collaboration) Add(collaboration.Id, collaboration);

        foreach (var participant in Collaboration?.Participants ?? []) Add(participant.Id, participant);
        foreach (var laneSet in LaneSets) Add(laneSet.Id, laneSet);
        foreach (var lane in Lanes) Add(lane.Id, lane);
        foreach (var node in FlowNodes) Add(node.Id, node);
        foreach (var flow in SequenceFlows) Add(flow.Id, flow);
        foreach (var flow in MessageFlows) Add(flow.Id, flow);
        foreach (var association in Associations) Add(association.Id, association);
        foreach (var association in DataAssociations) Add(association.Id, association);
        foreach (var annotation in TextAnnotations) Add(annotation.Id, annotation);
        foreach (var group in Groups) Add(group.Id, group);
        foreach (var reference in DataObjectReferences) Add(reference.Id, reference);
        foreach (var reference in DataStoreReferences) Add(reference.Id, reference);
        foreach (var dataObject in DataObjects) Add(dataObject.Id, dataObject);

        // Global definitions are indexed too: section 1.0 makes resolving errorRef, messageRef and
        // categoryValueRef the editor's responsibility, and they resolve through the same index.
        foreach (var message in Catalog.Messages) Add(message.Id, message);
        foreach (var signal in Catalog.Signals) Add(signal.Id, signal);
        foreach (var error in Catalog.Errors) Add(error.Id, error);
        foreach (var escalation in Catalog.Escalations) Add(escalation.Id, escalation);
        foreach (var item in Catalog.ItemDefinitions) Add(item.Id, item);
        foreach (var store in Catalog.DataStores) Add(store.Id, store);
        foreach (var resource in Catalog.Resources) Add(resource.Id, resource);
        foreach (var globalTask in Catalog.GlobalTasks) Add(globalTask.Id, globalTask);

        foreach (var category in Catalog.Categories)
        {
            Add(category.Id, category);
            foreach (var value in category.Values) Add(value.Id, value);
        }

        foreach (var contract in Catalog.Interfaces)
        {
            Add(contract.Id, contract);
            foreach (var operation in contract.Operations) Add(operation.Id, operation);
        }

        foreach (var diagram in Diagrams)
        {
            foreach (var style in diagram.LabelStyles) Add(style.Id, style);
        }

        return index.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
