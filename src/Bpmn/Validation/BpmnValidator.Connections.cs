using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Validation;

/// <summary>
/// The notation and connection layer (specification section 5.5, step 5).
/// </summary>
/// <remarks>
/// Endpoint compatibility, event context, gateway configuration and Pool/Lane rules. The canvas
/// checks most of these per gesture; these run over the finished document, which is where an
/// imported file — or a sequence of individually-legal edits — can still leave something the
/// matrices forbid.
/// </remarks>
public static partial class BpmnValidator
{
    private static void CheckConnections(DocumentIndex index, List<BpmnDiagnostic> found)
    {
        foreach (var node in index.Document.FlowNodes)
        {
            CheckEventFlows(index, node, found);
            CheckGateway(index, node, found);
        }

        CheckMessageFlows(index, found);
        CheckLinkEvents(index, found);
    }

    // --- events -------------------------------------------------------------------

    private static void CheckEventFlows(DocumentIndex index, BpmnFlowNode node, List<BpmnDiagnostic> found)
    {
        var incoming = index.Incoming[node.Id].ToList();
        var outgoing = index.Outgoing[node.Id].ToList();

        // D001: a Start Event starts something. An incoming flow would mean it does not.
        if (node.Type == BpmnFlowNodeType.StartEvent && incoming.Count > 0)
        {
            found.Add(BpmnDiagnostic.FromDetail(
                BpmnRules.D001,
                node.Id,
                incoming[0].Id,
                BpmnDiagnosticText.Create("D001.Detail", "{0} has {1}.", Name(node), incoming.Count)));
        }

        // D002: an End Event ends it.
        if (node.Type == BpmnFlowNodeType.EndEvent && outgoing.Count > 0)
        {
            found.Add(BpmnDiagnostic.FromDetail(
                BpmnRules.D002,
                node.Id,
                outgoing[0].Id,
                BpmnDiagnosticText.Create("D002.Detail", "{0} has {1}.", Name(node), outgoing.Count)));
        }

        // SF-017/SF-018: a Link pair is a jump, so the throw continues and the catch is reached
        // through its partner rather than by drawn flow.
        if (node.EventDefinition == BpmnEventDefinitionType.Link)
        {
            if (node.Type == BpmnFlowNodeType.IntermediateThrowEvent && outgoing.Count > 0)
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.SF017,
                    node.Id,
                    outgoing[0].Id,
                    BpmnDiagnosticText.Create("SF-017.Detail", "{0} has {1}.", Name(node), outgoing.Count)));
            }

            if (node.Type == BpmnFlowNodeType.IntermediateCatchEvent && incoming.Count > 0)
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.SF018,
                    node.Id,
                    incoming[0].Id,
                    BpmnDiagnosticText.Create("SF-018.Detail", "{0} has {1}.", Name(node), incoming.Count)));
            }
        }

        if (node.Type != BpmnFlowNodeType.BoundaryEvent) return;

        // D003: a Boundary Event is triggered by its host, never reached by a flow.
        if (incoming.Count > 0)
        {
            found.Add(BpmnDiagnostic.FromDetail(
                BpmnRules.D003,
                node.Id,
                incoming[0].Id,
                BpmnDiagnosticText.Create("D003.Detail", "{0} has {1}.", Name(node), incoming.Count)));
        }

        var host = index.Node(node.AttachedToRef);

        if (host is null || !IsActivity(host))
        {
            found.Add(BpmnDiagnostic.FromDetail(
                BpmnRules.D003,
                node.Id,
                node.AttachedToRef,
                host is null
                ? BpmnDiagnosticText.Create("D003.Detail.2", "{0} is attached to nothing.", Name(node))
                : BpmnDiagnosticText.Create("D003.Detail.3", "{0} is attached to {1}, which is not an Activity.", Name(node), Name(host))));
        }
    }

    /// <summary>
    /// D011: paired Link Events.
    /// </summary>
    /// <remarks>
    /// A Link pair is a jump within one Process: several throws may feed one catch, but a catch
    /// with no throw goes off nowhere and a name with two catches has no single destination.
    /// Matched by name within a container, which is what the specification means by "within the
    /// same Process".
    /// </remarks>
    private static void CheckLinkEvents(DocumentIndex index, List<BpmnDiagnostic> found)
    {
        var links = index.Document.FlowNodes
            .Where(node => node.EventDefinition == BpmnEventDefinitionType.Link)
            .ToList();

        foreach (var group in links.GroupBy(node => (node.ContainerId, LinkNameOf(node)), StringTupleComparer.Instance))
        {
            var throws = group.Where(node => node.Type == BpmnFlowNodeType.IntermediateThrowEvent).ToList();
            var catches = group.Where(node => node.Type == BpmnFlowNodeType.IntermediateCatchEvent).ToList();

            if (catches.Count > 1)
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D011,
                    catches[1].Id,
                    catches[0].Id,
                    BpmnDiagnosticText.Create("D011.Detail", "'{0}' is caught {1} times; a Link has one destination.", group.Key.Item2, catches.Count)));
            }

            if (catches.Count == 0 && throws.Count > 0)
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D011,
                    throws[0].Id,
                    detail: BpmnDiagnosticText.Create("D011.Detail.2", "'{0}' is thrown but never caught.", group.Key.Item2)));
            }

            if (throws.Count == 0 && catches.Count > 0)
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D011,
                    catches[0].Id,
                    detail: BpmnDiagnosticText.Create("D011.Detail.3", "'{0}' is caught but never thrown.", group.Key.Item2)));
            }
        }
    }

    /// <summary>A Link's pairing name, which is the definition's name rather than the Event's.</summary>
    private static string LinkNameOf(BpmnFlowNode node) =>
        node.EventDefinitions.FirstOrDefault(definition => definition.Type == BpmnEventDefinitionType.Link)?.Name
        ?? node.Name
        ?? string.Empty;

    // --- gateways ------------------------------------------------------------------

    private static void CheckGateway(DocumentIndex index, BpmnFlowNode node, List<BpmnDiagnostic> found)
    {
        if (!IsGateway(node)) return;

        var incoming = index.Incoming[node.Id].ToList();
        var outgoing = index.Outgoing[node.Id].ToList();

        CheckDecision(node, outgoing, found);
        CheckRedundantMerge(node, incoming, outgoing, found);
        CheckEventGateway(index, node, outgoing, found);
    }

    /// <summary>
    /// D004: product lint requiring decision branches to carry explicit machine-readable conditions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An Exclusive or Inclusive split with several branches needs a default flow, or every branch
    /// has to say when it is taken — otherwise the choice between them is undefined, and an engine
    /// picking arbitrarily is not what the diagram says.
    /// </para>
    /// <para>
    /// This is a product lint, stricter than BPMN schema/conformance for descriptive, non-executable
    /// models. A flow name is descriptive and never counts as a condition expression.
    /// </para>
    /// </remarks>
    private static void CheckDecision(
        BpmnFlowNode node,
        List<BpmnSequenceFlow> outgoing,
        List<BpmnDiagnostic> found)
    {
        var decides = node.Type is BpmnFlowNodeType.ExclusiveGateway or BpmnFlowNodeType.InclusiveGateway;
        if (!decides || outgoing.Count < 2) return;

        var defaults = outgoing.Count(flow => flow.IsDefault);

        var unconditional = outgoing
            .Where(flow => !flow.IsDefault
                && string.IsNullOrWhiteSpace(flow.ConditionExpression))
            .ToList();

        if (defaults == 1 || defaults == 0 && unconditional.Count == 0) return;

        found.Add(BpmnDiagnostic.FromDetail(
            BpmnRules.D004,
            node.Id,
            (unconditional.FirstOrDefault() ?? outgoing[0]).Id,
            defaults > 1
            ? BpmnDiagnosticText.Create("D004.Detail", "{0} has {1} default flows.", Name(node), defaults)
            : BpmnDiagnosticText.Create("D004.Detail.2", "{0} has {1} branches with neither a conditionExpression nor the default flag.", Name(node), unconditional.Count)));
    }

    /// <summary>D005: a merge gateway with one incoming flow synchronises nothing.</summary>
    private static void CheckRedundantMerge(
        BpmnFlowNode node,
        List<BpmnSequenceFlow> incoming,
        List<BpmnSequenceFlow> outgoing,
        List<BpmnDiagnostic> found)
    {
        var synchronises = node.Type is BpmnFlowNodeType.ParallelGateway
            or BpmnFlowNodeType.ComplexGateway
            or BpmnFlowNodeType.InclusiveGateway;

        // A merge is a gateway with one way out; with several it is a split, and one incoming flow
        // is exactly what a split is meant to have.
        if (synchronises && incoming.Count == 1 && outgoing.Count <= 1)
        {
            found.Add(BpmnDiagnostic.FromDetail(
                BpmnRules.D005,
                node.Id,
                detail: BpmnDiagnosticText.Create("D005.Detail", "{0} merges a single flow.", Name(node))));
        }
    }

    /// <summary>
    /// D006 and D014: an Event-Based Gateway decides by which event happens first.
    /// </summary>
    /// <remarks>
    /// Every branch must therefore lead to something that can catch one, none may carry a condition
    /// — the events are the condition — and the targets must be homogeneous, because a race between
    /// a Receive Task and a Message Event is two different mechanisms answering the same question.
    /// </remarks>
    private static void CheckEventGateway(
        DocumentIndex index,
        BpmnFlowNode node,
        List<BpmnSequenceFlow> outgoing,
        List<BpmnDiagnostic> found)
    {
        if (node.Type != BpmnFlowNodeType.EventBasedGateway) return;

        var kinds = new HashSet<BpmnFlowNodeType>();

        foreach (var flow in outgoing)
        {
            if (flow.ConditionExpression is { Length: > 0 })
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D006,
                    node.Id,
                    flow.Id,
                    BpmnDiagnosticText.Create("D006.Detail", "A branch carries a condition.")));
            }

            var target = index.Node(flow.TargetRef);
            if (target is null) continue;

            var catches = target.Type is BpmnFlowNodeType.IntermediateCatchEvent or BpmnFlowNodeType.ReceiveTask;

            if (!catches)
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D006,
                    node.Id,
                    target.Id,
                    BpmnDiagnosticText.Create("D006.Detail.2", "{0} is neither a catching Event nor a Receive Task.", Name(target))));

                continue;
            }

            kinds.Add(target.Type);

            // D014, second half: a target reachable another way could fire without winning the race.
            var others = index.Incoming[target.Id].Count(other => other.SourceRef != node.Id);

            if (others > 0)
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D014,
                    node.Id,
                    target.Id,
                    BpmnDiagnosticText.Create("D014.Detail", "{0} also has {1} incoming Sequence Flow from elsewhere.", Name(target), others)));
            }
        }

        if (kinds.Count > 1)
        {
            found.Add(BpmnDiagnostic.FromDetail(
                BpmnRules.D014,
                node.Id,
                detail: BpmnDiagnosticText.Create("D014.Detail.2", "Its branches mix catching Events and Receive Tasks.")));
        }
    }

    // --- message flows ---------------------------------------------------------------

    private static void CheckMessageFlows(DocumentIndex index, List<BpmnDiagnostic> found)
    {
        foreach (var flow in index.Document.MessageFlows)
        {
            // D008: a Message Flow is communication between Pools, so it crosses exactly one
            // boundary. Within one Pool it is not a Message Flow; between none it has no Pools.
            var source = PoolOf(index, flow.SourceRef);
            var target = PoolOf(index, flow.TargetRef);

            if (source is null || target is null)
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D008,
                    flow.Id,
                    source is null ? flow.SourceRef : flow.TargetRef,
                    BpmnDiagnosticText.Create("D008.Detail", "One of its ends is not in a Pool.")));
            }
            else if (source == target)
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D008,
                    flow.Id,
                    source,
                    BpmnDiagnosticText.Create("D008.Detail.2", "Both of its ends are in the same Pool.")));
            }

            CheckMessageEndpoints(index, flow, found);
        }

        CheckMessageResults(index, found);
    }

    private static void CheckMessageEndpoints(DocumentIndex index, BpmnMessageFlow flow, List<BpmnDiagnostic> found)
    {
        var source = index.Node(flow.SourceRef);
        var target = index.Node(flow.TargetRef);

        // MF-003: a Gateway routes tokens inside a Process; it neither sends nor receives.
        foreach (var end in new[] { source, target })
        {
            if (end is not null && IsGateway(end))
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.MF003,
                    flow.Id,
                    end.Id,
                    BpmnDiagnosticText.Create("MF-003.Detail", "{0} is a Gateway.", Name(end))));
            }
        }

        // MF-006: an End Event has finished; it cannot receive.
        if (target is { Type: BpmnFlowNodeType.EndEvent })
        {
            found.Add(BpmnDiagnostic.FromDetail(
                BpmnRules.MF006,
                flow.Id,
                target.Id,
                BpmnDiagnosticText.Create("MF-006.Detail", "{0} is an End Event.", Name(target))));
        }

        // MF-008: a catching end must be able to catch a message.
        if (target is not null && IsEvent(target) && !CatchesMessages(target))
        {
            found.Add(BpmnDiagnostic.FromDetail(
                BpmnRules.MF008,
                flow.Id,
                target.Id,
                BpmnDiagnosticText.Create("MF-008.Detail", "{0} does not catch messages.", Name(target))));
        }

        // MF-007: and a throwing end must be able to throw one.
        if (source is not null && IsEvent(source) && !ThrowsMessages(source))
        {
            found.Add(BpmnDiagnostic.FromDetail(
                BpmnRules.MF007,
                flow.Id,
                source.Id,
                BpmnDiagnosticText.Create("MF-007.Detail", "{0} does not throw messages.", Name(source))));
        }
    }

    private static bool CatchesMessages(BpmnFlowNode node) =>
        node.Type is BpmnFlowNodeType.StartEvent
            or BpmnFlowNodeType.IntermediateCatchEvent
            or BpmnFlowNodeType.BoundaryEvent
        && node.EventDefinition is BpmnEventDefinitionType.Message
            or BpmnEventDefinitionType.Multiple
            or BpmnEventDefinitionType.ParallelMultiple;

    private static bool ThrowsMessages(BpmnFlowNode node) =>
        node.Type is BpmnFlowNodeType.IntermediateThrowEvent or BpmnFlowNodeType.EndEvent
        && node.EventDefinition is BpmnEventDefinitionType.Message or BpmnEventDefinitionType.Multiple;

    /// <summary>
    /// D013: an End Event that sends says so.
    /// </summary>
    /// <remarks>
    /// One outgoing Message Flow needs a Message result; several need Multiple. Otherwise the shape
    /// is drawn as a plain End Event while the diagram has it sending messages, and the notation
    /// and the semantics disagree about what happens there.
    /// </remarks>
    private static void CheckMessageResults(DocumentIndex index, List<BpmnDiagnostic> found)
    {
        foreach (var node in index.Document.FlowNodes.Where(candidate => candidate.Type == BpmnFlowNodeType.EndEvent))
        {
            var sends = index.OutgoingMessages[node.Id].Count();
            if (sends == 0) continue;

            var expected = sends > 1 ? BpmnEventDefinitionType.Multiple : node.EventDefinition;
            var satisfied = sends > 1
                ? node.EventDefinition == BpmnEventDefinitionType.Multiple
                : node.EventDefinition is BpmnEventDefinitionType.Message or BpmnEventDefinitionType.Multiple;

            if (!satisfied)
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D013,
                    node.Id,
                    detail: BpmnDiagnosticText.Create("D013.Detail", "{0} sends {1} message(s) but its result is {2}.", Name(node), sends, node.EventDefinition)));
            }

            _ = expected;
        }
    }

    /// <summary>Compares the two-string keys a Link grouping uses, ordinally.</summary>
    private sealed class StringTupleComparer : IEqualityComparer<(string, string)>
    {
        public static readonly StringTupleComparer Instance = new();

        public bool Equals((string, string) x, (string, string) y) =>
            string.Equals(x.Item1, y.Item1, StringComparison.Ordinal)
            && string.Equals(x.Item2, y.Item2, StringComparison.Ordinal);

        public int GetHashCode((string, string) obj) =>
            HashCode.Combine(obj.Item1.GetHashCode(StringComparison.Ordinal), obj.Item2.GetHashCode(StringComparison.Ordinal));
    }
}
