using System.Xml.Linq;

using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

/// <summary>
/// The BPMN 2.0 namespaces and the element names this reader recognises.
/// </summary>
/// <remarks>
/// Namespace URIs are fixed by OMG BPMN 2.0.2 and never vary between documents, so they are
/// constants rather than something discovered per file. A prefix, by contrast, is arbitrary and
/// is never matched on.
/// </remarks>
internal static class BpmnXmlNames
{
    internal static readonly XNamespace Model = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    internal static readonly XNamespace Diagram = "http://www.omg.org/spec/BPMN/20100524/DI";
    internal static readonly XNamespace DiagramCommon = "http://www.omg.org/spec/DD/20100524/DC";
    internal static readonly XNamespace DiagramInterchange = "http://www.omg.org/spec/DD/20100524/DI";

    /// <summary>
    /// Element local names that are out of scope but must survive a round trip untouched
    /// (specification section 8): Choreography and Conversation constructs.
    /// </summary>
    internal static readonly HashSet<string> ExcludedElements = new(StringComparer.Ordinal)
    {
        "choreography",
        "choreographyTask",
        "subChoreography",
        "callChoreography",
        "conversation",
        "subConversation",
        "callConversation",
        "conversationLink",
        "conversationAssociation",
    };

    /// <summary>
    /// Maps a BPMN element local name to its editor type.
    /// </summary>
    /// <remarks>
    /// <c>subProcess</c> is resolved by the caller rather than here, because an Event
    /// Sub-Process is a <c>subProcess</c> carrying <c>triggeredByEvent="true"</c> and only the
    /// element's attributes can tell the two apart.
    /// </remarks>
    internal static BpmnFlowNodeType? ResolveFlowNodeType(string localName) => localName switch
    {
        "startEvent" => BpmnFlowNodeType.StartEvent,
        "endEvent" => BpmnFlowNodeType.EndEvent,
        "intermediateCatchEvent" => BpmnFlowNodeType.IntermediateCatchEvent,
        "intermediateThrowEvent" => BpmnFlowNodeType.IntermediateThrowEvent,
        "boundaryEvent" => BpmnFlowNodeType.BoundaryEvent,
        "task" => BpmnFlowNodeType.Task,
        "userTask" => BpmnFlowNodeType.UserTask,
        "serviceTask" => BpmnFlowNodeType.ServiceTask,
        "scriptTask" => BpmnFlowNodeType.ScriptTask,
        "manualTask" => BpmnFlowNodeType.ManualTask,
        "businessRuleTask" => BpmnFlowNodeType.BusinessRuleTask,
        "sendTask" => BpmnFlowNodeType.SendTask,
        "receiveTask" => BpmnFlowNodeType.ReceiveTask,
        "subProcess" => BpmnFlowNodeType.SubProcess,
        "transaction" => BpmnFlowNodeType.Transaction,
        "adHocSubProcess" => BpmnFlowNodeType.AdHocSubProcess,
        "callActivity" => BpmnFlowNodeType.CallActivity,
        "exclusiveGateway" => BpmnFlowNodeType.ExclusiveGateway,
        "inclusiveGateway" => BpmnFlowNodeType.InclusiveGateway,
        "parallelGateway" => BpmnFlowNodeType.ParallelGateway,
        "complexGateway" => BpmnFlowNodeType.ComplexGateway,
        "eventBasedGateway" => BpmnFlowNodeType.EventBasedGateway,
        _ => null,
    };

    /// <summary>Maps an <c>*EventDefinition</c> element name to its trigger type.</summary>
    internal static BpmnEventDefinitionType? ResolveEventDefinition(string localName) => localName switch
    {
        "messageEventDefinition" => BpmnEventDefinitionType.Message,
        "timerEventDefinition" => BpmnEventDefinitionType.Timer,
        "errorEventDefinition" => BpmnEventDefinitionType.Error,
        "escalationEventDefinition" => BpmnEventDefinitionType.Escalation,
        "cancelEventDefinition" => BpmnEventDefinitionType.Cancel,
        "compensateEventDefinition" => BpmnEventDefinitionType.Compensation,
        "conditionalEventDefinition" => BpmnEventDefinitionType.Conditional,
        "linkEventDefinition" => BpmnEventDefinitionType.Link,
        "signalEventDefinition" => BpmnEventDefinitionType.Signal,
        "terminateEventDefinition" => BpmnEventDefinitionType.Terminate,
        _ => null,
    };

    /// <summary>
    /// Maps an editor type back to the element name a document declares it with.
    /// </summary>
    /// <remarks>
    /// Not the inverse of <see cref="ResolveFlowNodeType"/> in one case: an Event Sub-Process is
    /// written as a <c>subProcess</c> carrying <c>triggeredByEvent="true"</c>, because BPMN has no
    /// element of its own for it. The writer adds that attribute.
    /// </remarks>
    internal static string ElementNameFor(BpmnFlowNodeType type) => type switch
    {
        BpmnFlowNodeType.StartEvent => "startEvent",
        BpmnFlowNodeType.EndEvent => "endEvent",
        BpmnFlowNodeType.IntermediateCatchEvent => "intermediateCatchEvent",
        BpmnFlowNodeType.IntermediateThrowEvent => "intermediateThrowEvent",
        BpmnFlowNodeType.BoundaryEvent => "boundaryEvent",
        BpmnFlowNodeType.Task => "task",
        BpmnFlowNodeType.UserTask => "userTask",
        BpmnFlowNodeType.ServiceTask => "serviceTask",
        BpmnFlowNodeType.ScriptTask => "scriptTask",
        BpmnFlowNodeType.ManualTask => "manualTask",
        BpmnFlowNodeType.BusinessRuleTask => "businessRuleTask",
        BpmnFlowNodeType.SendTask => "sendTask",
        BpmnFlowNodeType.ReceiveTask => "receiveTask",
        BpmnFlowNodeType.SubProcess => "subProcess",
        BpmnFlowNodeType.EventSubProcess => "subProcess",
        BpmnFlowNodeType.Transaction => "transaction",
        BpmnFlowNodeType.AdHocSubProcess => "adHocSubProcess",
        BpmnFlowNodeType.CallActivity => "callActivity",
        BpmnFlowNodeType.ExclusiveGateway => "exclusiveGateway",
        BpmnFlowNodeType.InclusiveGateway => "inclusiveGateway",
        BpmnFlowNodeType.ParallelGateway => "parallelGateway",
        BpmnFlowNodeType.ComplexGateway => "complexGateway",
        BpmnFlowNodeType.EventBasedGateway => "eventBasedGateway",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown flow node type."),
    };

    /// <summary>Maps a trigger back to its <c>*EventDefinition</c> element name.</summary>
    /// <remarks>
    /// <see cref="BpmnEventDefinitionType.Multiple"/> and
    /// <see cref="BpmnEventDefinitionType.ParallelMultiple"/> have no element of their own — they
    /// are what several definitions on one Event mean — so they map to nothing and the writer
    /// emits the individual definitions the model retained instead.
    /// </remarks>
    internal static string? ElementNameFor(BpmnEventDefinitionType type) => type switch
    {
        BpmnEventDefinitionType.Message => "messageEventDefinition",
        BpmnEventDefinitionType.Timer => "timerEventDefinition",
        BpmnEventDefinitionType.Error => "errorEventDefinition",
        BpmnEventDefinitionType.Escalation => "escalationEventDefinition",
        BpmnEventDefinitionType.Cancel => "cancelEventDefinition",
        BpmnEventDefinitionType.Compensation => "compensateEventDefinition",
        BpmnEventDefinitionType.Conditional => "conditionalEventDefinition",
        BpmnEventDefinitionType.Link => "linkEventDefinition",
        BpmnEventDefinitionType.Signal => "signalEventDefinition",
        BpmnEventDefinitionType.Terminate => "terminateEventDefinition",
        _ => null,
    };

    /// <summary>
    /// The attribute each trigger uses to reference its global definition.
    /// </summary>
    /// <remarks>
    /// One attribute per trigger, all meaning "the definition this event refers to", which is why
    /// the model stores them in a single <c>Ref</c> field rather than four nullable ones.
    /// </remarks>
    internal static string? ReferenceAttributeFor(BpmnEventDefinitionType type) => type switch
    {
        BpmnEventDefinitionType.Message => "messageRef",
        BpmnEventDefinitionType.Error => "errorRef",
        BpmnEventDefinitionType.Escalation => "escalationRef",
        BpmnEventDefinitionType.Signal => "signalRef",
        _ => null,
    };

    /// <summary>The <c>definitions</c> children this reader models as typed global definitions.</summary>
    internal static readonly HashSet<string> CatalogElements = new(StringComparer.Ordinal)
    {
        "import",
        "extension",
        "itemDefinition",
        "message",
        "signal",
        "error",
        "escalation",
        "dataStore",
        "category",
        "resource",
        "interface",
        "globalTask",
        "globalUserTask",
        "globalManualTask",
        "globalScriptTask",
        "globalBusinessRuleTask",
        "relationship",
    };

    /// <summary>The three timer expression elements, which mean different things and never merge.</summary>
    internal static readonly string[] TimerExpressions = ["timeDate", "timeDuration", "timeCycle"];

    /// <summary>The owned BPMN elements whose body is modelled as a formal-expression string.</summary>
    internal static readonly HashSet<string> FormalExpressionElements = new(StringComparer.Ordinal)
    {
        "conditionExpression",
        "activationCondition",
        "loopCondition",
        "loopCardinality",
        "completionCondition",
        "condition",
        "from",
        "to",
    };

    /// <summary>Owned text-valued elements whose values are represented directly by the model.</summary>
    internal static readonly HashSet<string> TextElements = new(StringComparer.Ordinal)
    {
        "flowNodeRef",
        "loopDataInputRef",
        "loopDataOutputRef",
        "sourceRef",
        "targetRef",
        "source",
        "target",
        "inMessageRef",
        "outMessageRef",
        "transformation",
        "text",
        "documentation",
        "script",
        "timeDate",
        "timeDuration",
        "timeCycle",
    };

    /// <summary>
    /// Every element name the writer produces, and therefore every one the model owns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fidelity writer uses this to decide what it may update or delete. Anything absent is
    /// content the reader never interpreted, so the model has no opinion about it and it is left
    /// untouched — <c>ioSpecification</c>, <c>property</c>, <c>resourceRole</c>, vendor elements,
    /// Choreography subtrees. Adding a name here without teaching the writer to produce it would
    /// make a save delete that element.
    /// </para>
    /// <para>
    /// <c>incoming</c> and <c>outgoing</c> are absent on purpose: they restate
    /// <c>sourceRef</c>/<c>targetRef</c>, the model does not store them, and the fidelity writer
    /// refreshes them in place rather than owning them.
    /// </para>
    /// </remarks>
    internal static readonly HashSet<string> OwnedElements = new(StringComparer.Ordinal)
    {
        // definitions level
        "import", "extension", "itemDefinition", "message", "signal", "error", "escalation",
        "dataStore", "category", "categoryValue", "resource", "interface", "operation",
        "inMessageRef", "outMessageRef", "relationship", "source", "target",
        "globalTask", "globalUserTask", "globalManualTask", "globalScriptTask",
        "globalBusinessRuleTask",

        // containers
        "process", "collaboration", "participant", "messageFlow",
        "laneSet", "lane", "childLaneSet", "flowNodeRef",

        // shared base-element children
        "documentation", "extensionElements",

        // flow elements
        "startEvent", "endEvent", "intermediateCatchEvent", "intermediateThrowEvent",
        "boundaryEvent", "task", "userTask", "serviceTask", "scriptTask", "manualTask",
        "businessRuleTask", "sendTask", "receiveTask", "subProcess", "transaction",
        "adHocSubProcess", "callActivity", "exclusiveGateway", "inclusiveGateway",
        "parallelGateway", "complexGateway", "eventBasedGateway",
        "sequenceFlow", "conditionExpression",
        "activationCondition",

        // data and artifacts
        "dataObject", "dataObjectReference", "dataStoreReference", "dataState",
        "dataInputAssociation", "dataOutputAssociation", "sourceRef", "targetRef",
        "transformation", "assignment", "from", "to", "association", "group", "textAnnotation", "text",

        // activity and event detail
        "standardLoopCharacteristics", "loopCondition", "multiInstanceLoopCharacteristics",
        "loopCardinality", "loopDataInputRef", "inputDataItem", "loopDataOutputRef",
        "outputDataItem", "complexBehaviorDefinition",
        "messageEventDefinition", "timerEventDefinition", "errorEventDefinition",
        "escalationEventDefinition", "cancelEventDefinition", "compensateEventDefinition",
        "conditionalEventDefinition", "linkEventDefinition", "signalEventDefinition",
        "terminateEventDefinition", "timeDate", "timeDuration", "timeCycle", "condition",

        // BPMNDI
        "BPMNDiagram", "BPMNPlane", "BPMNShape", "BPMNEdge", "BPMNLabel", "BPMNLabelStyle",
        "Bounds", "waypoint", "Font",
    };

    internal static bool IsOwnedElement(XElement parent, XElement element)
    {
        if (element.Name.Namespace != Model
            && element.Name.Namespace != Diagram
            && element.Name.Namespace != DiagramCommon
            && element.Name.Namespace != DiagramInterchange)
        {
            return false;
        }

        return OwnedElements.Contains(element.Name.LocalName)
            || (parent.Name == Model + "multiInstanceLoopCharacteristics"
                && element.Name == Model + "completionCondition")
            // Only a Script Task's script is modelled; a script under anything else is not the
            // model's to delete, so it is left exactly where it is.
            || (parent.Name == Model + "scriptTask"
                && element.Name == Model + "script");
    }
}
