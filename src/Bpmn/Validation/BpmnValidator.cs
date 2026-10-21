using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Validation;

/// <summary>
/// Validates a whole document (specification sections 5.2 and 5.5).
/// </summary>
/// <remarks>
/// <para>
/// The document-scoped half of the rule table. The canvas already judges a gesture as it happens,
/// against the same <c>rules/bpmn-rules.json</c> — but a rule like "every node is reachable from a
/// Start Event" is not about a gesture and cannot be answered while one is in flight. Those rules
/// are this engine's, and they run over the model rather than over the SVG, because the model is
/// what gets exported.
/// </para>
/// <para>
/// Section 5.5 sets both the order and the discipline: run the layers in sequence and "retain all
/// issues rather than stopping after the first failure". So nothing here returns early, and a
/// document that fails the reference layer is still walked for containment and DI — a user fixing a
/// document wants the whole list, not the first item of it, and a half-reported document reads as
/// though the editor gave up.
/// </para>
/// <para>
/// Severity and wording come from the generated <see cref="BpmnRules"/> catalog rather than from
/// string literals here. That catalog is generated from the same JSON the TypeScript side reads, so
/// a rule means one thing in both runtimes and is reworded in one place.
/// </para>
/// </remarks>
public static partial class BpmnValidator
{
    /// <summary>
    /// Validates a document.
    /// </summary>
    /// <param name="document">The document to check.</param>
    /// <returns>Every finding, in layer order.</returns>
    public static BpmnValidationReport Validate(BpmnDocument document) => Validate(document, []);

    /// <summary>
    /// Validates a document, then runs the host's engine profiles over it.
    /// </summary>
    /// <remarks>
    /// Profiles run last and can only add. Section 5.5 puts them in their own layer and requires
    /// it to run "without weakening core errors": a profile that could remove or downgrade a core
    /// finding would be a setting that turns BPMN validation off, which is not what an engine
    /// profile is for.
    /// </remarks>
    /// <param name="document">The document to check.</param>
    /// <param name="profiles">Engine profiles to run after the core layers.</param>
    /// <returns>Every finding, in layer order, the profiles' last.</returns>
    public static BpmnValidationReport Validate(
        BpmnDocument document,
        IReadOnlyList<IBpmnProfileValidator> profiles)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(profiles);

        var found = new List<BpmnDiagnostic>();
        var index = new DocumentIndex(document);

        // Section 5.5's pipeline, in its order. The XML and schema layers are the reader's — by the
        // time a document reaches here it has parsed, and what the reader could not keep it already
        // reported as an import diagnostic.
        CheckNamespaces(index, found);
        CheckReferences(index, found);
        CheckExtensions(index, found);
        CheckContainment(index, found);
        CheckConnections(index, found);
        CheckSemantics(index, found);
        CheckRenderability(index, found);
        CheckExpansion(index, found);
        foreach (var annotation in document.TextAnnotations)
        {
            if (BpmnAnnotationAttachments.Connections(document, annotation.Id).Skip(1).Any())
            {
                found.Add(new BpmnDiagnostic("AN-001", BpmnSeverity.Warning,
                    "Editor policy allows one target per Text Annotation. Multiple bindings have no lifecycle owner.", annotation.Id));
            }
        }
        foreach (var edge in document.Diagrams.SelectMany(diagram => diagram.Plane.Edges))
        {
            if (edge.Waypoints.Count > 2 && BpmnAnnotationAttachments.IsAnnotationAssociation(document, edge.BpmnElement))
            {
                found.Add(new BpmnDiagnostic("AN-002", BpmnSeverity.Warning,
                    "Editor policy requires a straight annotation Association with two endpoints.", edge.BpmnElement));
            }
        }

        // Layer 7, and last for a reason: the core list is complete before a profile sees the
        // document, and nothing a profile returns can reach back into it.
        if (profiles.Count > 0) found.AddRange(BpmnProfileRunner.Run(document, profiles));

        return new BpmnValidationReport(found);
    }

    /// <summary>
    /// The lookups every layer needs, built once.
    /// </summary>
    /// <remarks>
    /// Each rule on its own would walk the flow collections to find a node's incoming or outgoing
    /// edges, which turns a linear document into a quadratic validation. The stress fixture has 602
    /// shapes and 601 connectors, so that is the difference between a validation the editor can run
    /// on every edit and one it cannot.
    /// </remarks>
    private sealed class DocumentIndex(BpmnDocument document)
    {
        public BpmnDocument Document { get; } = document;

        /// <summary>Sequence Flows arriving at each node.</summary>
        public ILookup<string, BpmnSequenceFlow> Incoming { get; } =
            document.SequenceFlows.ToLookup(flow => flow.TargetRef, StringComparer.Ordinal);

        /// <summary>Sequence Flows leaving each node.</summary>
        public ILookup<string, BpmnSequenceFlow> Outgoing { get; } =
            document.SequenceFlows.ToLookup(flow => flow.SourceRef, StringComparer.Ordinal);

        /// <summary>Message Flows arriving at each element.</summary>
        public ILookup<string, BpmnMessageFlow> IncomingMessages { get; } =
            document.MessageFlows.ToLookup(flow => flow.TargetRef, StringComparer.Ordinal);

        /// <summary>Message Flows leaving each element.</summary>
        public ILookup<string, BpmnMessageFlow> OutgoingMessages { get; } =
            document.MessageFlows.ToLookup(flow => flow.SourceRef, StringComparer.Ordinal);

        /// <summary>Boundary Events by the Activity they are attached to.</summary>
        public ILookup<string, BpmnFlowNode> Attached { get; } = document.FlowNodes
            .Where(node => node.AttachedToRef is not null)
            .ToLookup(node => node.AttachedToRef!, StringComparer.Ordinal);

        /// <summary>Every drawn shape, by the element it draws.</summary>
        public ILookup<string, Model.Di.BpmnShape> Shapes { get; } = document.Diagrams
            .SelectMany(diagram => diagram.Plane.Shapes)
            .ToLookup(shape => shape.BpmnElement, StringComparer.Ordinal);

        /// <summary>Finds a flow node, or null.</summary>
        public BpmnFlowNode? Node(string? id) => id is null ? null : Document.FindFlowNode(id);
    }

    /// <summary>The Pool a node belongs to, which is what SF-006 and MF-002 compare.</summary>
    private static string? PoolOf(DocumentIndex index, string? elementId)
    {
        if (elementId is null) return null;

        return index.Document.Find(elementId) switch
        {
            BpmnFlowNode node => node.PoolId ?? ParticipantFor(index, node.ContainerId),
            BpmnParticipant participant => participant.Id,
            _ => null,
        };
    }

    private static string? ParticipantFor(DocumentIndex index, string containerId) =>
        index.Document.Collaboration?.Participants
            .FirstOrDefault(participant => participant.ProcessRef == containerId)?.Id;

    private static bool IsEvent(BpmnFlowNode node) =>
        Editing.BpmnElementInspector.CategoryOf(node.Type) == Editing.BpmnElementCategory.Event;

    private static bool IsActivity(BpmnFlowNode node) =>
        Editing.BpmnElementInspector.CategoryOf(node.Type) == Editing.BpmnElementCategory.Activity;

    private static bool IsGateway(BpmnFlowNode node) =>
        Editing.BpmnElementInspector.CategoryOf(node.Type) == Editing.BpmnElementCategory.Gateway;

    /// <summary>A name to put in a message, falling back to the id when there is none.</summary>
    private static string Name(BpmnFlowNode node) => node.Name is { Length: > 0 } name ? $"'{name}'" : $"'{node.Id}'";
}
