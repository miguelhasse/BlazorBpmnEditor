using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Validation;

/// <summary>
/// The reference, containment, semantic and renderability layers (specification section 5.5).
/// </summary>
/// <remarks>
/// What these have in common is that none of them is about one connector. They are questions about
/// the document — does every reference land somewhere, does every node have exactly one owner, can
/// every node be reached — and they are the reason a document-scoped validator exists at all.
/// </remarks>
public static partial class BpmnValidator
{
    // --- references (section 5.5, step 3) ------------------------------------------

    private static void CheckNamespaces(DocumentIndex index, List<BpmnDiagnostic> found)
    {
        var document = index.Document;
        if (string.IsNullOrWhiteSpace(document.Definitions.TargetNamespace))
        {
            found.Add(BpmnDiagnostic.FromDetail(
                BpmnRules.D021,
                detail: BpmnDiagnosticText.Create("D021.Detail", "Definitions/@targetNamespace is required and must not be empty.")));
        }

        foreach (var node in document.FlowNodes)
        {
            if (node.CalledElementQName is { } qname)
            {
                if (!IsNcName(qname.LocalName))
                {
                    found.Add(BpmnDiagnostic.FromDetail(
                        BpmnRules.D021,
                        node.Id,
                        detail: BpmnDiagnosticText.Create("D021.Detail.2", "calledElement value '{0}' has an invalid QName local name.", qname.LexicalValue)));
                }

                if (qname.NamespaceUri is null)
                {
                    found.Add(BpmnDiagnostic.FromDetail(
                        BpmnRules.D021,
                        node.Id,
                        detail: BpmnDiagnosticText.Create("D021.Detail.3", "calledElement QName '{0}' has an undeclared prefix or no resolved namespace.", qname.LexicalValue)));
                }

                if (node.CalledElementRef is not null
                    && !string.Equals(qname.NamespaceUri, document.Definitions.TargetNamespace, StringComparison.Ordinal))
                {
                    found.Add(BpmnDiagnostic.FromDetail(
                        BpmnRules.D021,
                        node.Id,
                        detail: BpmnDiagnosticText.Create("D021.Detail.4", "calledElement QName '{0}' was projected as a local reference although its namespace differs from targetNamespace.", qname.LexicalValue)));
                }
            }

            foreach (var expression in ExpressionsOf(node))
            {
                if (expression.TypeQName is null) continue;
                if (IsQName(expression.TypeQName) && expression.TypeNamespaceUri is not null) continue;

                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D021,
                    node.Id,
                    detail: IsQName(expression.TypeQName)
                    ? BpmnDiagnosticText.Create("D021.Detail.5", "xsi:type QName '{0}' has an undeclared prefix or no resolved namespace.", expression.TypeQName)
                    : BpmnDiagnosticText.Create("D021.Detail.6", "xsi:type value '{0}' is not a valid QName.", expression.TypeQName)));
            }
        }

        foreach (var association in document.DataAssociations)
        {
            foreach (var expression in association.Assignments.SelectMany(assignment => new[] { assignment.From, assignment.To }))
            {
                if (expression.TypeQName is null) continue;
                if (IsQName(expression.TypeQName) && expression.TypeNamespaceUri is not null) continue;

                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D021,
                    association.Id,
                    detail: IsQName(expression.TypeQName)
                    ? BpmnDiagnosticText.Create("D021.Detail.5", "xsi:type QName '{0}' has an undeclared prefix or no resolved namespace.", expression.TypeQName)
                    : BpmnDiagnosticText.Create("D021.Detail.6", "xsi:type value '{0}' is not a valid QName.", expression.TypeQName)));
            }
        }
    }

    private static IEnumerable<BpmnFormalExpression> ExpressionsOf(BpmnFlowNode node)
    {
        if (node.ActivationCondition is { } activation) yield return activation;
        if (node.StandardLoop?.LoopCondition is { } loopCondition) yield return loopCondition;

        if (node.MultiInstanceLoop is not { } multi) yield break;
        if (multi.LoopCardinality is { } cardinality) yield return cardinality;
        if (multi.CompletionCondition is { } completion) yield return completion;
        foreach (var behavior in multi.ComplexBehaviorDefinitions ?? [])
        {
            if (behavior.Condition is { } condition) yield return condition;
        }
    }

    private static bool IsNcName(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Contains(':')) return false;

        try
        {
            System.Xml.XmlConvert.VerifyNCName(value);
            return true;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    private static bool IsQName(string value)
    {
        var separator = value.IndexOf(':');
        if (separator == 0) return false;

        var localName = separator < 0 ? value : value[(separator + 1)..];
        return !localName.Contains(':')
            && IsNcName(localName)
            && (separator < 0 || IsNcName(value[..separator]));
    }

    private static void CheckReferences(DocumentIndex index, List<BpmnDiagnostic> found)
    {
        var document = index.Document;

        // D018: IDs must be XML NCNames and unique; duplicate IDs otherwise resolve to
        // whichever element first claimed the id in the document index.
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var reportedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in AllIds(document))
        {
            var valid = IsNcName(id);
            var unique = seenIds.Add(id);
            if ((!valid || !unique) && reportedIds.Add(id))
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D018,
                    id,
                    detail: !valid ? BpmnDiagnosticText.Create("D018.Detail", "'{0}' is not a valid XML NCName.", id) : BpmnDiagnosticText.Create("D018.Detail.2", "'{0}' is used more than once.", id)));
            }
        }

        foreach (var flow in document.SequenceFlows)
        {
            Reference(index, found, flow.Id, flow.SourceRef, "sourceRef", RequiresFlowNode);
            Reference(index, found, flow.Id, flow.TargetRef, "targetRef", RequiresFlowNode);
        }

        foreach (var flow in document.MessageFlows)
        {
            Reference(index, found, flow.Id, flow.SourceRef, "sourceRef", RequiresInteractionNode);
            Reference(index, found, flow.Id, flow.TargetRef, "targetRef", RequiresInteractionNode);
            Reference(index, found, flow.Id, flow.MessageRef, "messageRef", target => target is BpmnMessage);
        }

        foreach (var participant in document.Collaboration?.Participants ?? [])
        {
            // A black-box Participant has no processRef at all, which is legal.
            Reference(index, found, participant.Id, participant.ProcessRef, "processRef", target => target is BpmnProcess);
        }

        foreach (var node in document.FlowNodes)
        {
            Reference(index, found, node.Id, node.AttachedToRef, "attachedToRef", target => target is BpmnFlowNode);
            Reference(index, found, node.Id, node.DefaultFlowRef, "defaultFlowRef", target => target is BpmnSequenceFlow);
            Reference(index, found, node.Id, node.MessageRef, "messageRef", target => target is BpmnMessage);

            // calledElement is a QName. Never resolve its local part against this document if it
            // names another namespace: a same-spelled local id is not the referenced definition.
            var targetNamespace = document.Definitions.TargetNamespace;
            var externalCall = node.CalledElementQName is { } qname
                && !string.Equals(qname.NamespaceUri, targetNamespace, StringComparison.Ordinal);
            var called = node.CalledElementQName?.LocalName ?? node.CalledElementRef;
            var callable = externalCall ? null : called is null ? null : document.Find(called);

            // D023: a Call Activity must name a local Process or Global Task the editor can resolve.
            if (called is not null && callable is not (BpmnProcess or BpmnGlobalTask))
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D023,
                    node.Id,
                    detail: externalCall
                    ? BpmnDiagnosticText.Create("D023.Detail", "{0} calls QName '{1}' in namespace '{2}', which this document does not resolve.", Name(node), node.CalledElementQName!.LexicalValue, node.CalledElementQName.NamespaceUri)
                    : BpmnDiagnosticText.Create("D023.Detail.2", "{0} calls '{1}', which this document does not declare as a Process or Global Task.", Name(node), called)));
            }

            foreach (var definition in node.EventDefinitions)
            {
                Reference(index, found, node.Id, definition.Ref, $"{definition.Type} reference", _ => true);
            }
        }

        foreach (var reference in document.DataObjectReferences)
        {
            Reference(index, found, reference.Id, reference.DataObjectRef, "dataObjectRef", target => target is BpmnDataObject);
        }

        foreach (var reference in document.DataStoreReferences)
        {
            Reference(index, found, reference.Id, reference.DataStoreRef, "dataStoreRef", target => target is BpmnDataStore);
        }

        foreach (var group in document.Groups)
        {
            Reference(index, found, group.Id, group.CategoryValueRef, "categoryValueRef", target => target is BpmnCategoryValue);
        }
    }

    private static bool RequiresFlowNode(object target) => target is BpmnFlowNode;

    private static bool RequiresInteractionNode(object target) =>
        target is BpmnFlowNode or BpmnParticipant;

    /// <summary>
    /// D017: one reference, resolved and type-checked.
    /// </summary>
    /// <remarks>
    /// Null is not a finding — most of these attributes are optional, and the rules that require
    /// one are enforced where that requirement lives. What this checks is the reference that was
    /// written down: it must land on something, and on something of the right kind.
    /// </remarks>
    private static void Reference(
        DocumentIndex index,
        List<BpmnDiagnostic> found,
        string owner,
        string? reference,
        string attribute,
        Func<object, bool> permitted)
    {
        if (reference is not { Length: > 0 }) return;

        var target = index.Document.Find(reference);

        if (target is null)
        {
            found.Add(BpmnDiagnostic.FromDetail(
                BpmnRules.D017,
                owner,
                detail: BpmnDiagnosticText.Create("D017.Detail", "{0} points at '{1}', which is not in this document.", attribute, reference)));

            return;
        }

        if (!permitted(target))
        {
            found.Add(BpmnDiagnostic.FromDetail(
                BpmnRules.D017,
                owner,
                reference,
                BpmnDiagnosticText.Create("D017.Detail.2", "{0} points at a {1}, which it may not.", attribute, target.GetType().Name[4..])));
        }
    }

    private static IEnumerable<string> AllIds(BpmnDocument document)
    {
        foreach (var candidate in AllIdCandidates(document))
        {
            if (candidate is not null) yield return candidate;
        }
    }

    private static IEnumerable<string?> AllIdCandidates(BpmnDocument document)
    {
        yield return document.Definitions.Id;
        yield return document.Collaboration?.Id;
        foreach (var process in document.Processes) yield return process.Id;
        foreach (var participant in document.Collaboration?.Participants ?? []) yield return participant.Id;
        foreach (var laneSet in document.LaneSets) yield return laneSet.Id;
        foreach (var lane in document.Lanes) yield return lane.Id;
        foreach (var node in document.FlowNodes) yield return node.Id;
        foreach (var flow in document.SequenceFlows) yield return flow.Id;
        foreach (var flow in document.MessageFlows) yield return flow.Id;
        foreach (var association in document.Associations) yield return association.Id;
        foreach (var association in document.DataAssociations) yield return association.Id;
        foreach (var annotation in document.TextAnnotations) yield return annotation.Id;
        foreach (var group in document.Groups) yield return group.Id;
        foreach (var reference in document.DataObjectReferences) yield return reference.Id;
        foreach (var reference in document.DataStoreReferences) yield return reference.Id;
        foreach (var dataObject in document.DataObjects) yield return dataObject.Id;

        foreach (var node in document.FlowNodes)
        {
            foreach (var definition in node.EventDefinitions) yield return definition.Id;
            if (node.MultiInstanceLoop?.InputDataItem?.Id is { } inputId) yield return inputId;
            if (node.MultiInstanceLoop?.OutputDataItem?.Id is { } outputId) yield return outputId;
        }

        var catalog = document.Catalog;
        foreach (var item in catalog.Messages) yield return item.Id;
        foreach (var item in catalog.Signals) yield return item.Id;
        foreach (var item in catalog.Errors) yield return item.Id;
        foreach (var item in catalog.Escalations) yield return item.Id;
        foreach (var item in catalog.ItemDefinitions) yield return item.Id;
        foreach (var item in catalog.DataStores) yield return item.Id;
        foreach (var category in catalog.Categories)
        {
            yield return category.Id;
            foreach (var value in category.Values) yield return value.Id;
        }
        foreach (var item in catalog.Resources) yield return item.Id;
        foreach (var contract in catalog.Interfaces)
        {
            yield return contract.Id;
            foreach (var operation in contract.Operations) yield return operation.Id;
        }
        foreach (var item in catalog.GlobalTasks) yield return item.Id;
        foreach (var relationship in catalog.Relationships) yield return relationship.Id;
        foreach (var declaration in catalog.ExtensionDeclarations) yield return declaration.Id;

        foreach (var diagram in document.Diagrams)
        {
            yield return diagram.Id;
            yield return diagram.Plane.Id;
            foreach (var style in diagram.LabelStyles) yield return style.Id;
            foreach (var shape in diagram.Plane.Shapes)
            {
                yield return shape.Id;
                yield return shape.Label?.Id;
            }
            foreach (var edge in diagram.Plane.Edges)
            {
                yield return edge.Id;
                yield return edge.Label?.Id;
            }
        }
    }

    // --- containment (section 5.5, step 4) --------------------------------------------

    private static void CheckContainment(DocumentIndex index, List<BpmnDiagnostic> found)
    {
        var document = index.Document;

        var containers = document.Processes.Select(process => process.Id)
            .Concat(document.FlowNodes.Where(node => node.FlowElementsOwned).Select(node => node.Id))
            .ToHashSet(StringComparer.Ordinal);

        // D019, first half: a Flow Node with no resolvable container belongs to nothing, and
        // section 9.3 makes exactly one container the condition for taking part in Sequence Flow.
        foreach (var node in document.FlowNodes.Where(candidate => !containers.Contains(candidate.ContainerId)))
        {
            found.Add(BpmnDiagnostic.FromDetail(
                BpmnRules.D019,
                node.Id,
                detail: BpmnDiagnosticText.Create("D019.Detail", "{0} names container '{1}', which is not one.", Name(node), node.ContainerId)));
        }

        // D019, second half: Lane membership is the Lane's statement about the node, so a Lane in
        // one Process claiming a node from another is a claim the node cannot honour.
        foreach (var lane in document.Lanes)
        {
            var owner = document.LaneSets.FirstOrDefault(set => set.Id == lane.LaneSetId);

            if (owner is null)
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D019,
                    lane.Id,
                    detail: BpmnDiagnosticText.Create("D019.Detail.2", "Its lane set '{0}' is not declared.", lane.LaneSetId)));

                continue;
            }

            /*
             * CT-012: a Lane subdivides a Pool.
             *
             * Section 1.5 writes every rule about a Lane in terms of the Pool it divides — stacked
             * inside it, subdividing its bounds, taking its orientation. A Lane whose Process no
             * Participant references has none of those, so its geometry means nothing: there is no
             * rectangle it is a share of.
             *
             * The editor refuses to create one and the reader gives an orphan lane set a Pool on
             * import, so this fires only for a document assembled some other way — which is exactly
             * when a document-wide check is the thing that catches it.
             */
            if (BpmnStencil.PoolOwning(document, owner.ContainerId) is null)
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.CT012,
                    lane.Id,
                    detail: BpmnDiagnosticText.Create("CT-012.Detail", "No Participant references '{0}'.", owner.ContainerId)));
            }

            foreach (var member in lane.FlowNodeRefs)
            {
                var node = index.Node(member);

                if (node is null)
                {
                    found.Add(BpmnDiagnostic.FromDetail(
                        BpmnRules.D019,
                        lane.Id,
                        member,
                        BpmnDiagnosticText.Create("D019.Detail.3", "It claims '{0}', which does not exist.", member)));
                }
                else if (node.ContainerId != owner.ContainerId)
                {
                    found.Add(BpmnDiagnostic.FromDetail(
                        BpmnRules.D019,
                        lane.Id,
                        member,
                        BpmnDiagnosticText.Create("D019.Detail.4", "It claims {0}, which belongs to '{1}'.", Name(node), node.ContainerId)));
                }
            }
        }

        // D019 again: a node in two leaf Lanes is in neither, as far as a reader is concerned.
        foreach (var group in document.Lanes
            .SelectMany(lane => lane.FlowNodeRefs.Select(member => (Lane: lane, Member: member)))
            .GroupBy(entry => entry.Member, StringComparer.Ordinal)
            .Where(entry => entry.Count() > 1))
        {
            var lanes = group.Select(entry => entry.Lane.Id).ToList();

            found.Add(BpmnDiagnostic.FromDetail(
                BpmnRules.D019,
                group.Key,
                lanes[1],
                BpmnDiagnosticText.Create("D019.Detail.5", "It is claimed by {0} Lanes.", lanes.Count)));
        }

        CheckScopes(index, found);
    }

    /// <summary>
    /// D007 and SF-007: a Sequence Flow stays inside one scope.
    /// </summary>
    /// <remarks>
    /// Both halves of the same idea. A flow that crosses a Pool boundary is communication and
    /// should be a Message Flow; one that crosses a Sub-Process boundary is a token leaving a scope
    /// that owns it. Either way the document says something no engine can run and no reader can
    /// follow.
    /// </remarks>
    private static void CheckScopes(DocumentIndex index, List<BpmnDiagnostic> found)
    {
        foreach (var flow in index.Document.SequenceFlows)
        {
            var source = index.Node(flow.SourceRef);
            var target = index.Node(flow.TargetRef);
            if (source is null || target is null) continue;

            if (PoolOf(index, source.Id) != PoolOf(index, target.Id))
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D007,
                    flow.Id,
                    source.Id,
                    BpmnDiagnosticText.Create("D007.Detail", "{0} and {1} are in different Pools.", Name(source), Name(target))));
            }
            else if (source.ContainerId != target.ContainerId)
            {
                // A Boundary Event is the documented exception: it is owned by the container its
                // host is in, and its outgoing flow legitimately continues in that container.
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.SF007,
                    flow.Id,
                    source.Id,
                    BpmnDiagnosticText.Create("SF-007.Detail", "{0} is in '{1}' and {2} in '{3}'.", Name(source), source.ContainerId, Name(target), target.ContainerId)));
            }
        }
    }

    // --- document semantics (section 5.5, step 6) ---------------------------------------

    private static void CheckSemantics(DocumentIndex index, List<BpmnDiagnostic> found)
    {
        CheckEventSubProcesses(index, found);
        CheckEventContexts(index, found);
        CheckCompensation(index, found);
        CheckReachability(index, found);

        // The model's own invariants are part of the same picture, and duplicating them here would
        // be a second definition of each.
        foreach (var violation in BpmnDocumentInvariants.Check(index.Document))
        {
            found.Add(new BpmnDiagnostic(violation.RuleId, BpmnSeverity.Error, violation.Message, violation.ElementId)
            {
                MessageResourceKey = violation.LocalizableMessage?.ResourceKey,
                MessageArguments = violation.LocalizableMessage?.Arguments,
            });
        }
    }

    /// <summary>D010: an Event Sub-Process is triggered, never entered.</summary>
    private static void CheckEventSubProcesses(DocumentIndex index, List<BpmnDiagnostic> found)
    {
        foreach (var sub in index.Document.FlowNodes.Where(node => node.Type == BpmnFlowNodeType.EventSubProcess))
        {
            var crossing = index.Incoming[sub.Id].Count() + index.Outgoing[sub.Id].Count();

            if (crossing > 0)
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D010,
                    sub.Id,
                    detail: BpmnDiagnosticText.Create("D010.Detail", "{0} has {1} Sequence Flow crossing its boundary.", Name(sub), crossing)));
            }

            var starts = index.Document.FlowNodes
                .Where(node => node.ContainerId == sub.Id && node.Type == BpmnFlowNodeType.StartEvent)
                .ToList();

            if (starts.Count != 1)
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D010,
                    sub.Id,
                    detail: BpmnDiagnosticText.Create("D010.Detail.2", "{0} has {1} Start Events; it needs exactly one.", Name(sub), starts.Count)));

                continue;
            }

            if (starts[0].EventDefinition == BpmnEventDefinitionType.None)
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D010,
                    starts[0].Id,
                    sub.Id,
                    BpmnDiagnosticText.Create("D010.Detail.3", "An untyped Start Event can never trigger it.")));
            }
        }
    }

    /// <summary>D020: an Event's trigger has to be one its context allows.</summary>
    private static void CheckEventContexts(DocumentIndex index, List<BpmnDiagnostic> found)
    {
        foreach (var node in index.Document.FlowNodes.Where(IsEvent))
        {
            if (Editing.BpmnElementInspector.IsLegalEvent(index.Document, node)) continue;

            var triggerAllowed = Editing.BpmnElementInspector.IsTriggerLegalInContext(
                index.Document,
                node,
                node.EventDefinition);
            var detail = !triggerAllowed
                            ? BpmnDiagnosticText.Create("D020.Detail", "{0} is a {1} carrying a {2} trigger.", Name(node), node.Type, node.EventDefinition)
                            : !Editing.BpmnElementInspector.IsInterruptionLegal(index.Document, node)
                                ? BpmnDiagnosticText.Create("D020.Detail.2", "{0} is a {1} with an invalid interruption state for {2}.", Name(node), node.Type, node.EventDefinition)
                                : BpmnDiagnosticText.Create("D020.Detail.3", "{0} has an invalid container or Boundary host context.", Name(node));

            found.Add(BpmnDiagnostic.FromDetail(
                BpmnRules.D020,
                node.Id,
                detail: detail));
        }
    }

    /// <summary>
    /// D012: a compensation handler is reached by compensation, not by the main flow.
    /// </summary>
    /// <remarks>
    /// The Activity a Compensation Boundary Event compensates is run out of band, when the
    /// transaction unwinds. An ordinary Sequence Flow into it would also run it during the normal
    /// path, which is not what the diagram is saying.
    /// </remarks>
    private static void CheckCompensation(DocumentIndex index, List<BpmnDiagnostic> found)
    {
        var handlers = index.Document.Associations
            .Where(association => index.Node(association.SourceRef) is
            {
                Type: BpmnFlowNodeType.BoundaryEvent,
                EventDefinition: BpmnEventDefinitionType.Compensation,
            })
            .Select(association => association.TargetRef)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var handler in handlers)
        {
            var reached = index.Incoming[handler].Count();

            if (reached > 0)
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D012,
                    handler,
                    detail: BpmnDiagnosticText.Create("D012.Detail", "It has {0} incoming Sequence Flow.", reached)));
            }
        }
    }

    /// <summary>
    /// D009: orphans and dead ends.
    /// </summary>
    /// <remarks>
    /// Warning-level, deliberately. A diagram under construction is full of both, and an editor
    /// that called them errors would refuse to let anyone build one — but a finished diagram with
    /// an unreachable node has a node nobody will ever run.
    /// </remarks>
    private static void CheckReachability(DocumentIndex index, List<BpmnDiagnostic> found)
    {
        foreach (var container in index.Document.FlowNodes
            .Select(node => node.ContainerId)
            .Distinct(StringComparer.Ordinal))
        {
            var nodes = index.Document.FlowNodes
                .Where(node => node.ContainerId == container && node.Type != BpmnFlowNodeType.BoundaryEvent)
                .ToList();

            var starts = nodes.Where(node => node.Type == BpmnFlowNodeType.StartEvent).ToList();

            // Nothing to measure from. A Process with no Start Event is a separate finding for a
            // profile that requires one, and plenty of legitimate fragments have none.
            if (starts.Count == 0 || nodes.Count < 2) continue;

            var reachable = Reachable(index, starts.Select(start => start.Id), forward: true);

            foreach (var node in nodes.Where(candidate => !reachable.Contains(candidate.Id)))
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D009,
                    node.Id,
                    detail: BpmnDiagnosticText.Create("D009.Detail", "{0} cannot be reached from any Start Event.", Name(node))));
            }

            var ends = nodes.Where(node => node.Type == BpmnFlowNodeType.EndEvent).Select(end => end.Id).ToList();
            if (ends.Count == 0) continue;

            var finishing = Reachable(index, ends, forward: false);

            foreach (var start in starts.Where(candidate => !finishing.Contains(candidate.Id)))
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D009,
                    start.Id,
                    detail: BpmnDiagnosticText.Create("D009.Detail.2", "{0} cannot reach any End Event.", Name(start))));
            }
        }
    }

    private static HashSet<string> Reachable(DocumentIndex index, IEnumerable<string> from, bool forward)
    {
        var seen = new HashSet<string>(from, StringComparer.Ordinal);
        var queue = new Queue<string>(seen);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            var next = forward
                ? index.Outgoing[current].Select(flow => flow.TargetRef)
                : index.Incoming[current].Select(flow => flow.SourceRef);

            // A Boundary Event's own outgoing flow continues the main path, so a node reached only
            // through one is reachable — following the host's attachments keeps that true.
            if (forward)
            {
                next = next.Concat(index.Attached[current].Select(boundary => boundary.Id));
            }

            foreach (var id in next)
            {
                if (seen.Add(id)) queue.Enqueue(id);
            }
        }

        return seen;
    }

    // --- renderability (section 5.5, step 8) ---------------------------------------------

    private static void CheckRenderability(DocumentIndex index, List<BpmnDiagnostic> found)
    {
        var document = index.Document;

        foreach (var diagram in document.Diagrams)
        {
            var drawn = new HashSet<string>(StringComparer.Ordinal);

            foreach (var shape in diagram.Plane.Shapes)
            {
                // D015: a shape with no area is a shape nothing can be drawn in.
                if (!shape.Bounds.IsPositive)
                {
                    found.Add(BpmnDiagnostic.FromDetail(
                        BpmnRules.D015,
                        shape.BpmnElement,
                        detail: BpmnDiagnosticText.Create("D015.Detail", "Its bounds are {0} by {1}.", shape.Bounds.Width, shape.Bounds.Height)));
                }

                if (document.Find(shape.BpmnElement) is null)
                {
                    found.Add(BpmnDiagnostic.FromDetail(
                        BpmnRules.D015,
                        shape.BpmnElement,
                        detail: BpmnDiagnosticText.Create("D015.Detail.2", "Shape '{0}' draws an element this document does not have.", shape.Id)));
                }

                // D022: two shapes for one element on one plane make containment and selection
                // ambiguous — a click would have two right answers.
                else if (!drawn.Add(shape.BpmnElement))
                {
                    found.Add(BpmnDiagnostic.FromDetail(
                        BpmnRules.D022,
                        shape.BpmnElement,
                        detail: BpmnDiagnosticText.Create("D022.Detail", "It is drawn more than once on '{0}'.", diagram.Id)));
                }

                if (shape.Label?.StyleId is { } style && document.Find(style) is null)
                {
                    found.Add(BpmnDiagnostic.FromDetail(
                        BpmnRules.D022,
                        shape.BpmnElement,
                        detail: BpmnDiagnosticText.Create("D022.Detail.2", "Its label references style '{0}', which is not declared.", style)));
                }
            }

            foreach (var edge in diagram.Plane.Edges)
            {
                // D015 and DI-001: BPMNDI's schema minimum. A route with one point is not a route.
                if (edge.Waypoints.Count < 2)
                {
                    found.Add(BpmnDiagnostic.FromDetail(
                        BpmnRules.D015,
                        edge.BpmnElement,
                        detail: BpmnDiagnosticText.Create("D015.Detail.3", "Its route has {0} waypoint(s).", edge.Waypoints.Count)));
                }

                if (document.Find(edge.BpmnElement) is null)
                {
                    found.Add(BpmnDiagnostic.FromDetail(
                        BpmnRules.D015,
                        edge.BpmnElement,
                        detail: BpmnDiagnosticText.Create("D015.Detail.4", "Edge '{0}' draws a connector this document does not have.", edge.Id)));
                }
            }

            // D022: a plane draws something. One that names nothing has no owner to draw.
            if (diagram.Plane.BpmnElement is null or { Length: 0 })
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D022,
                    diagram.Id,
                    detail: BpmnDiagnosticText.Create("D022.Detail.3", "Plane '{0}' names no BPMN element.", diagram.Plane.Id)));
            }
            else if (document.Find(diagram.Plane.BpmnElement) is null)
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.D022,
                    diagram.Id,
                    detail: BpmnDiagnosticText.Create("D022.Detail.4", "Plane '{0}' is owned by '{1}', which does not exist.", diagram.Plane.Id, diagram.Plane.BpmnElement)));
            }
        }
    }
    /// <summary>
    /// Whether what a container draws agrees with whether it says it is expanded (CT-007, CT-009).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Section 2.8.1: "Collapsed Sub-Processes and Call Activities are opaque leaves in the active
    /// plane and must not have visible embedded process-content cells." The renderer draws one cell
    /// per shape on the plane and nothing else, so a document that puts a collapsed container's
    /// children on the same plane draws them floating over the closed box, parented to the Pool
    /// rather than to the container they belong to.
    /// </para>
    /// <para>
    /// This is not reachable through the editor — collapsing moves the content to a plane of its
    /// own — which is exactly why it is worth checking: it is reachable by a hand edit and by every
    /// other tool that writes these files, and nothing else in the pipeline would notice.
    /// </para>
    /// </remarks>
    private static void CheckExpansion(DocumentIndex index, List<BpmnDiagnostic> found)
    {
        var document = index.Document;

        var opaqueKinds = document.FlowNodes
            .Where(node => node.FlowElementsOwned || node.Type == BpmnFlowNodeType.CallActivity)
            .ToDictionary(node => node.Id, StringComparer.Ordinal);

        if (opaqueKinds.Count == 0) return;

        var held = Held(document);

        foreach (var diagram in document.Diagrams)
        {
            var here = diagram.Plane.Shapes.Select(shape => shape.BpmnElement)
                .Concat(diagram.Plane.Edges.Select(edge => edge.BpmnElement))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var shape in diagram.Plane.Shapes)
            {
                if (!opaqueKinds.TryGetValue(shape.BpmnElement, out var node)) continue;

                // A Call Activity has no local content to expand and never carries the attribute,
                // so it is opaque whatever the shape says (section 1.2.1).
                var opaque = node.Type == BpmnFlowNodeType.CallActivity || shape.IsExpanded != true;

                if (opaque)
                {
                    foreach (var drawn in Contents(held, node.Id).Where(here.Contains))
                    {
                        found.Add(BpmnDiagnostic.FromDetail(
                            BpmnRules.CT007,
                            node.Id,
                            drawn,
                            BpmnDiagnosticText.Create("CT-007.Detail", "It is drawn collapsed on '{0}', which also draws '{1}' from inside it.", diagram.Id, drawn)));
                    }

                    continue;
                }

                /*
                 * CT-009, the other way round: content drawn in two places at once. A plane of its
                 * own is where a collapsed container keeps its children, so one that still exists
                 * while the container is expanded leaves no answer to "where does this element
                 * live" — and drilling in would show a second copy of what is already on screen.
                 */
                var ownPlane = document.Diagrams.FirstOrDefault(other =>
                    other.Plane.BpmnElement == node.Id && other.Plane.Shapes.Count > 0);

                if (ownPlane is not null)
                {
                    found.Add(BpmnDiagnostic.FromDetail(
                        BpmnRules.CT009,
                        node.Id,
                        detail: BpmnDiagnosticText.Create("CT-009.Detail", "It is drawn expanded on '{0}' and its content is also drawn on '{1}'.", diagram.Id, ownPlane.Id)));
                }
            }
        }
    }

    /// <summary>Everything each container holds directly, for every kind of thing a container holds.</summary>
    private static ILookup<string, string> Held(BpmnDocument document) => Owned(document)
        .ToLookup(entry => entry.Container, entry => entry.Element, StringComparer.Ordinal);

    private static IEnumerable<(string Container, string Element)> Owned(BpmnDocument document)
    {
        foreach (var node in document.FlowNodes) yield return (node.ContainerId, node.Id);
        foreach (var flow in document.SequenceFlows) yield return (flow.ContainerId, flow.Id);
        foreach (var association in document.Associations) yield return (association.ContainerId, association.Id);
        foreach (var annotation in document.TextAnnotations) yield return (annotation.ContainerId, annotation.Id);
        foreach (var group in document.Groups) yield return (group.ContainerId, group.Id);
        foreach (var reference in document.DataObjectReferences) yield return (reference.ContainerId, reference.Id);
        foreach (var reference in document.DataStoreReferences) yield return (reference.ContainerId, reference.Id);
    }

    /// <summary>
    /// Everything a container holds, however deeply nested.
    /// </summary>
    /// <remarks>
    /// Transitive, because a collapsed Sub-Process hides its grandchildren too — a Task inside a
    /// Sub-Process inside the collapsed one is just as undrawable as a direct child.
    /// </remarks>
    private static IEnumerable<string> Contents(ILookup<string, string> held, string containerId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>([containerId]);

        while (pending.Count > 0)
        {
            foreach (var child in held[pending.Dequeue()])
            {
                if (!seen.Add(child)) continue;

                yield return child;
                pending.Enqueue(child);
            }
        }
    }
}
