using System.Xml.Linq;

namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

/// <summary>
/// The child order BPMN's complex types require, and the placement logic that honours it.
/// </summary>
/// <remarks>
/// <para>
/// Every BPMN complex type is an <c>xsd:sequence</c>, so child order is not cosmetic: a
/// <c>&lt;laneSet&gt;</c> written after the flow elements, or an <c>&lt;extensionElements&gt;</c>
/// written after <c>&lt;incoming&gt;</c>, produces a document other modelling tools reject even
/// though every element is present and correct.
/// </para>
/// <para>
/// Ranks are per schema <em>slot</em>, not per element name. <c>flowElement*</c> is one slot
/// holding a substitution group, so a Start Event and a Task rank equally and keep the order they
/// were written in. Ranking them individually would be just as valid against the schema, but it
/// would reorder every document to this writer's taste and turn a one-line edit into a whole-file
/// diff.
/// </para>
/// <para>
/// The canonical writer emits in this order by construction and sorts to be sure. The fidelity
/// writer uses it to place new children in a tree it did not create, without disturbing anything
/// already there.
/// </para>
/// </remarks>
internal static class BpmnSchemaOrder
{
    /// <summary>The element names in one <c>xsd:sequence</c> slot, all ranked equally.</summary>
    private static readonly string[] RootElements =
    [
        "itemDefinition", "message", "signal", "error", "escalation", "dataStore", "category",
        "resource", "interface", "globalTask", "globalUserTask", "globalManualTask",
        "globalScriptTask", "globalBusinessRuleTask", "process", "collaboration",
    ];

    /// <summary>The <c>flowElement</c> substitution group.</summary>
    private static readonly string[] FlowElements =
    [
        "dataObject", "dataObjectReference", "dataStoreReference",
        "startEvent", "intermediateCatchEvent", "intermediateThrowEvent", "boundaryEvent",
        "endEvent", "task", "userTask", "manualTask", "serviceTask", "scriptTask",
        "businessRuleTask", "sendTask", "receiveTask", "subProcess", "transaction",
        "adHocSubProcess", "callActivity", "exclusiveGateway", "inclusiveGateway",
        "parallelGateway", "complexGateway", "eventBasedGateway", "sequenceFlow",
    ];

    /// <summary>The <c>artifact</c> substitution group.</summary>
    private static readonly string[] Artifacts = ["association", "group", "textAnnotation"];

    /// <summary>The <c>eventDefinition</c> substitution group.</summary>
    private static readonly string[] EventDefinitions =
    [
        "messageEventDefinition", "timerEventDefinition", "errorEventDefinition",
        "escalationEventDefinition", "cancelEventDefinition", "compensateEventDefinition",
        "conditionalEventDefinition", "linkEventDefinition", "signalEventDefinition",
        "terminateEventDefinition", "eventDefinitionRef",
    ];

    /// <summary>Children of <c>definitions</c> (<c>tDefinitions</c>).</summary>
    /// <remarks>
    /// Unlisted content lands in the <c>rootElement*</c> slot, not at the end. The Choreography
    /// subtrees this editor preserves without rendering, and any vendor root element, are root
    /// elements — writing them after <c>BPMNDiagram</c> would break the sequence.
    /// </remarks>
    private static readonly ChildOrder Definitions = new(
        [["import"], ["extension"], RootElements, ["BPMNDiagram"], ["relationship"]],
        UnlistedRank: 2);

    /// <summary>Children of <c>process</c> (<c>tProcess</c>, which extends <c>tCallableElement</c>).</summary>
    private static readonly ChildOrder Process = new(
    [
        ["documentation"],
        ["extensionElements"],
        ["supportedInterfaceRef"],
        ["ioSpecification"],
        ["ioBinding"],
        ["auditing"],
        ["monitoring"],
        ["property"],
        ["laneSet"],
        FlowElements,
        Artifacts,
        ["resourceRole"],
        ["correlationSubscription"],
        ["supports"],
    ]);

    /// <summary>
    /// Children of the Sub-Process kinds (<c>tSubProcess</c>, which extends <c>tActivity</c>).
    /// </summary>
    /// <remarks>
    /// Not the same as <see cref="Process"/>, which is the mistake that is easy to make: a
    /// Sub-Process is an Activity first, so its <c>incoming</c>, <c>outgoing</c> and
    /// <c>loopCharacteristics</c> all precede the content it contains.
    /// </remarks>
    private static readonly ChildOrder SubProcess = new(
    [
        ["documentation"],
        ["extensionElements"],
        ["incoming"],
        ["outgoing"],
        ["ioSpecification"],
        ["property"],
        ["dataInput", "dataOutput"],
        ["dataInputAssociation", "dataOutputAssociation"],
        ["inputSet", "outputSet"],
        ["resourceRole"],
        ["loopCharacteristics", "standardLoopCharacteristics", "multiInstanceLoopCharacteristics"],
        ["laneSet"],
        FlowElements,
        Artifacts,
    ]);

    /// <summary>Children of a leaf flow node: Events, Tasks and Gateways.</summary>
    private static readonly ChildOrder FlowNode = new(
    [
        ["documentation"],
        ["extensionElements"],
        ["incoming"],
        ["outgoing"],
        ["activationCondition"],
        ["ioSpecification"],
        ["property"],
        ["dataInput", "dataOutput"],
        ["dataInputAssociation", "dataOutputAssociation"],
        ["inputSet", "outputSet"],
        ["resourceRole"],
        ["loopCharacteristics", "standardLoopCharacteristics", "multiInstanceLoopCharacteristics"],
        EventDefinitions,
        ["script"],
    ]);

    /// <summary>Children of <c>collaboration</c>.</summary>
    private static readonly ChildOrder Collaboration = new(
    [
        ["documentation"],
        ["extensionElements"],
        ["participant"],
        ["messageFlow"],
        Artifacts,
        ["conversationNode", "conversationAssociation", "participantAssociation"],
        ["messageFlowAssociation"],
        ["correlationKey"],
        ["choreographyRef"],
        ["conversationLink"],
    ]);

    /// <summary>Children of <c>laneSet</c> and <c>lane</c>.</summary>
    private static readonly ChildOrder Lanes = new(
    [
        ["documentation"],
        ["extensionElements"],
        ["partitionElement"],
        ["lane"],
        ["flowNodeRef"],
        ["childLaneSet"],
    ]);

    /// <summary>Children of <c>sequenceFlow</c>, <c>messageFlow</c> and the association kinds.</summary>
    private static readonly ChildOrder Connector = new(
    [
        ["documentation"],
        ["extensionElements"],
        ["conditionExpression"],
        ["sourceRef"],
        ["targetRef"],
        ["transformation"],
        ["assignment"],
    ]);

    /// <summary>Children of a BPMNDI element. <c>di:extension</c> always leads.</summary>
    private static readonly ChildOrder DiagramElement = new(
    [
        ["extension"],
        ["Bounds"],
        ["waypoint"],
        ["BPMNLabel"],
        ["BPMNPlane"],
        ["BPMNLabelStyle"],
        ["Font"],
    ]);

    private static readonly ChildOrder StandardLoop = new(
    [
        ["loopCondition"],
    ]);

    private static readonly ChildOrder MultiInstanceLoop = new(
    [
        ["loopCardinality"],
        ["loopDataInputRef"],
        ["inputDataItem"],
        ["loopDataOutputRef"],
        ["outputDataItem"],
        ["complexBehaviorDefinition"],
        ["completionCondition"],
    ]);

    private static readonly ChildOrder ComplexBehavior = new(
    [
        ["condition"],
    ]);

    private static readonly ChildOrder Assignment = new(
    [
        ["from"],
        ["to"],
    ]);

    /// <summary>
    /// Inserts <paramref name="child"/> into <paramref name="parent"/> at its schema position.
    /// </summary>
    /// <remarks>
    /// Equal-ranked siblings keep document order and the new child joins the end of its own run,
    /// so re-inserting an element the document already had several of does not shuffle them.
    /// </remarks>
    /// <param name="parent">The element to insert into.</param>
    /// <param name="child">The element to insert.</param>
    internal static void Insert(XElement parent, XElement child)
    {
        var order = OrderFor(parent);
        var rank = order.RankOf(child.Name.LocalName);

        var after = parent.Elements().LastOrDefault(existing =>
            order.RankOf(existing.Name.LocalName) <= rank);

        if (after is null)
        {
            parent.AddFirst(child);
        }
        else
        {
            after.AddAfterSelf(child);
        }
    }

    /// <summary>Sorts an element's existing children into schema order, in place.</summary>
    /// <remarks>
    /// Stable: children in the same slot keep the order they were added in. Used only by the
    /// canonical writer. The fidelity writer never calls it, because re-ordering children the
    /// author wrote is exactly the rewriting that mode exists to avoid.
    /// </remarks>
    /// <param name="parent">The element whose children to reorder.</param>
    internal static void Sort(XElement parent)
    {
        var order = OrderFor(parent);

        // Most elements are written in schema order already; one allocation-free pass settles that.
        var previous = int.MinValue;
        var inOrder = true;
        foreach (var element in parent.Elements())
        {
            var rank = order.RankOf(element.Name.LocalName);
            if (rank < previous)
            {
                inOrder = false;
                break;
            }

            previous = rank;
        }

        if (inOrder) return;

        var children = parent.Elements().ToList();

        var sorted = children
            .Select((element, index) => (element, index, rank: order.RankOf(element.Name.LocalName)))
            .OrderBy(entry => entry.rank)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.element)
            .ToList();

        if (sorted.SequenceEqual(children)) return;

        foreach (var child in children) child.Remove();
        foreach (var child in sorted) parent.Add(child);
    }

    private static ChildOrder OrderFor(XElement parent)
    {
        if (parent.Name.Namespace == BpmnXmlNames.Diagram
            || parent.Name.Namespace == BpmnXmlNames.DiagramInterchange
            || parent.Name.Namespace == BpmnXmlNames.DiagramCommon)
        {
            return DiagramElement;
        }

        return parent.Name.LocalName switch
        {
            "definitions" => Definitions,
            "process" => Process,
            "subProcess" or "transaction" or "adHocSubProcess" => SubProcess,
            "standardLoopCharacteristics" => StandardLoop,
            "multiInstanceLoopCharacteristics" => MultiInstanceLoop,
            "complexBehaviorDefinition" => ComplexBehavior,
            "assignment" => Assignment,
            "collaboration" => Collaboration,
            "laneSet" or "lane" or "childLaneSet" => Lanes,
            "sequenceFlow" or "messageFlow" or "association" or "dataInputAssociation"
                or "dataOutputAssociation" => Connector,
            _ => FlowNode,
        };
    }

    /// <summary>
    /// One complex type's child order, as the sequence of slots the schema declares.
    /// </summary>
    /// <param name="Slots">Element names per slot, in schema order; names within a slot rank equally.</param>
    /// <param name="UnlistedRank">
    /// Where a name this table does not list belongs. It defaults to after everything, which is the
    /// safe answer for content that is out of schema anyway — but at <c>definitions</c> level
    /// unlisted content is a root element and has a real slot to go in.
    /// </param>
    private sealed record ChildOrder(string[][] Slots, int? UnlistedRank = null)
    {
        internal int RankOf(string localName)
        {
            for (var slot = 0; slot < Slots.Length; slot++)
            {
                if (Array.IndexOf(Slots[slot], localName) >= 0) return slot;
            }

            return UnlistedRank ?? Slots.Length;
        }
    }
}
