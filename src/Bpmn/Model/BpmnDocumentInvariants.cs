using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Model;

/// <summary>A structural defect found in a document.</summary>
/// <param name="RuleId">The specification rule this violates.</param>
/// <param name="ElementId">The element the finding is about.</param>
/// <param name="Message">What is wrong, in terms a user can act on.</param>
public sealed record BpmnInvariantViolation(string RuleId, string ElementId, string Message)
{
    internal BpmnDiagnosticText? LocalizableMessage { get; init; }

    internal static BpmnInvariantViolation FromText(string ruleId, string elementId, BpmnDiagnosticText text) =>
        new(ruleId, elementId, text.Message) { LocalizableMessage = text };

    /// <summary>Compares invariant findings independently of presentation metadata.</summary>
    public bool Equals(BpmnInvariantViolation? other) => other is not null
        && RuleId == other.RuleId && ElementId == other.ElementId && Message == other.Message;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(RuleId, ElementId, Message);
}

/// <summary>
/// The invariants the model itself owns.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately narrow. This is not the validation engine of specification section 5 — it checks
/// only the properties that make a document structurally incoherent rather than merely invalid
/// BPMN: contradictions no renderer can draw and no editor can safely edit.
/// </para>
/// <para>
/// Connection rules, event-context legality and the whole-diagram checks live in the rule table
/// (<c>rules/bpmn-rules.json</c>) and are evaluated by the validators on both sides. Duplicating
/// any of them here would be exactly the drift that table exists to prevent.
/// </para>
/// </remarks>
public static class BpmnDocumentInvariants
{
    /// <summary>Checks a document and returns every violation found.</summary>
    public static IReadOnlyList<BpmnInvariantViolation> Check(BpmnDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var violations = new List<BpmnInvariantViolation>();

        CheckFlowNodes(document, violations);
        CheckDiagramGeometry(document, violations);

        return violations;
    }

    private static void CheckFlowNodes(BpmnDocument document, List<BpmnInvariantViolation> violations)
    {
        foreach (var node in document.FlowNodes)
        {
            // A Call Activity references a global definition; an embedded Sub-Process owns local
            // content. Both at once, or neither, describes an element that cannot be drawn or
            // navigated (section 6 note on calledElementRef / flowElementsOwned).
            if (node.FlowElementsOwned && node.CalledElementRef is not null)
            {
                violations.Add(BpmnInvariantViolation.FromText(
                    "CT-001",
                    node.Id,
                    BpmnDiagnosticText.Create("CT-001.Native", "An element cannot both own local flow elements and reference a called element.")));
            }

            if (node.Type is BpmnFlowNodeType.CallActivity && node.FlowElementsOwned)
            {
                violations.Add(BpmnInvariantViolation.FromText(
                    "CT-001",
                    node.Id,
                    BpmnDiagnosticText.Create("CT-001.Native.2", "A Call Activity never owns local flow elements; its content lives in the called Process.")));
            }

            if (node.Type is not BpmnFlowNodeType.BoundaryEvent && node.AttachedToRef is not null)
            {
                violations.Add(BpmnInvariantViolation.FromText(
                    "CT-005",
                    node.Id,
                    BpmnDiagnosticText.Create("CT-005.Native", "Only a Boundary Event may declare an attachment to a host Activity.")));
            }

            // A free-floating Boundary Event has no host outline to sit on, so it has no
            // position at all (section 2.5).
            if (node.Type is BpmnFlowNodeType.BoundaryEvent && node.AttachedToRef is null)
            {
                violations.Add(BpmnInvariantViolation.FromText(
                    "CT-005",
                    node.Id,
                    BpmnDiagnosticText.Create("CT-005.Native.2", "A Boundary Event must be attached to exactly one Activity.")));
            }
            else if (node.Type is BpmnFlowNodeType.BoundaryEvent
                     && document.FindFlowNode(node.AttachedToRef!) is null)
            {
                violations.Add(BpmnInvariantViolation.FromText(
                    "CT-005",
                    node.Id,
                    BpmnDiagnosticText.Create("CT-005.Native.3", "Boundary Event host '{0}' does not resolve to a flow node.", node.AttachedToRef)));
            }
        }
    }

    private static void CheckDiagramGeometry(BpmnDocument document, List<BpmnInvariantViolation> violations)
    {
        foreach (var diagram in document.Diagrams)
        {
            foreach (var shape in diagram.Plane.Shapes)
            {
                if (!shape.Bounds.IsPositive)
                {
                    violations.Add(BpmnInvariantViolation.FromText(
                        "DI-001",
                        shape.BpmnElement,
                    BpmnDiagnosticText.Create("DI-001.Native", "Every BPMNShape must have positive width and height.")));
                }

                // isHorizontal is defined only on Pool and Lane shapes; anywhere else it is a
                // meaningless attribute that downstream tools will read as orientation.
                if (shape.IsHorizontal is not null && !IsSwimlane(document, shape))
                {
                    violations.Add(BpmnInvariantViolation.FromText(
                        "DI-007",
                        shape.BpmnElement,
                    BpmnDiagnosticText.Create("DI-007.Native", "isHorizontal is defined only on Pool and Lane shapes.")));
                }
            }

            foreach (var edge in diagram.Plane.Edges)
            {
                if (edge.Waypoints.Count < 2)
                {
                    violations.Add(BpmnInvariantViolation.FromText(
                        "DI-002",
                        edge.BpmnElement,
                    BpmnDiagnosticText.Create("DI-002.Native", "Every BPMNEdge must have at least two waypoints.")));
                }
            }
        }
    }

    private static bool IsSwimlane(BpmnDocument document, BpmnShape shape) =>
        document.Find(shape.BpmnElement) is BpmnParticipant or BpmnLane;
}
