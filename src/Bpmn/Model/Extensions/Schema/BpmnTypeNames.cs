namespace Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema;

/// <summary>Wire-name conversion and BPMN 2.0 type inheritance.</summary>
public static class BpmnTypeNames
{
    // BPMN 2.0 hierarchy from bpmn-io/bpmn-moddle resources/bpmn/json/bpmn.json (MIT).
    private static readonly Dictionary<string, string[]> Parents = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["bpmn:Interface"] = ["bpmn:RootElement"],
        ["bpmn:Operation"] = ["bpmn:BaseElement"],
        ["bpmn:EndPoint"] = ["bpmn:RootElement"],
        ["bpmn:Auditing"] = ["bpmn:BaseElement"],
        ["bpmn:GlobalTask"] = ["bpmn:CallableElement"],
        ["bpmn:Monitoring"] = ["bpmn:BaseElement"],
        ["bpmn:Performer"] = ["bpmn:ResourceRole"],
        ["bpmn:Process"] = ["bpmn:FlowElementsContainer", "bpmn:CallableElement"],
        ["bpmn:LaneSet"] = ["bpmn:BaseElement"],
        ["bpmn:Lane"] = ["bpmn:BaseElement"],
        ["bpmn:GlobalManualTask"] = ["bpmn:GlobalTask"],
        ["bpmn:ManualTask"] = ["bpmn:Task"],
        ["bpmn:UserTask"] = ["bpmn:Task"],
        ["bpmn:Rendering"] = ["bpmn:BaseElement"],
        ["bpmn:HumanPerformer"] = ["bpmn:Performer"],
        ["bpmn:PotentialOwner"] = ["bpmn:HumanPerformer"],
        ["bpmn:GlobalUserTask"] = ["bpmn:GlobalTask"],
        ["bpmn:Gateway"] = ["bpmn:FlowNode"],
        ["bpmn:EventBasedGateway"] = ["bpmn:Gateway"],
        ["bpmn:ComplexGateway"] = ["bpmn:Gateway"],
        ["bpmn:ExclusiveGateway"] = ["bpmn:Gateway"],
        ["bpmn:InclusiveGateway"] = ["bpmn:Gateway"],
        ["bpmn:ParallelGateway"] = ["bpmn:Gateway"],
        ["bpmn:RootElement"] = ["bpmn:BaseElement"],
        ["bpmn:Relationship"] = ["bpmn:BaseElement"],
        ["bpmn:BaseElement"] = ["bpmn:"],
        ["bpmn:Extension"] = ["bpmn:"],
        ["bpmn:ExtensionDefinition"] = ["bpmn:"],
        ["bpmn:ExtensionAttributeDefinition"] = ["bpmn:"],
        ["bpmn:ExtensionElements"] = ["bpmn:"],
        ["bpmn:Documentation"] = ["bpmn:BaseElement"],
        ["bpmn:Event"] = ["bpmn:FlowNode", "bpmn:InteractionNode"],
        ["bpmn:IntermediateCatchEvent"] = ["bpmn:CatchEvent"],
        ["bpmn:IntermediateThrowEvent"] = ["bpmn:ThrowEvent"],
        ["bpmn:EndEvent"] = ["bpmn:ThrowEvent"],
        ["bpmn:StartEvent"] = ["bpmn:CatchEvent"],
        ["bpmn:ThrowEvent"] = ["bpmn:Event"],
        ["bpmn:CatchEvent"] = ["bpmn:Event"],
        ["bpmn:BoundaryEvent"] = ["bpmn:CatchEvent"],
        ["bpmn:EventDefinition"] = ["bpmn:RootElement"],
        ["bpmn:CancelEventDefinition"] = ["bpmn:EventDefinition"],
        ["bpmn:ErrorEventDefinition"] = ["bpmn:EventDefinition"],
        ["bpmn:TerminateEventDefinition"] = ["bpmn:EventDefinition"],
        ["bpmn:EscalationEventDefinition"] = ["bpmn:EventDefinition"],
        ["bpmn:Escalation"] = ["bpmn:RootElement"],
        ["bpmn:CompensateEventDefinition"] = ["bpmn:EventDefinition"],
        ["bpmn:TimerEventDefinition"] = ["bpmn:EventDefinition"],
        ["bpmn:LinkEventDefinition"] = ["bpmn:EventDefinition"],
        ["bpmn:MessageEventDefinition"] = ["bpmn:EventDefinition"],
        ["bpmn:ConditionalEventDefinition"] = ["bpmn:EventDefinition"],
        ["bpmn:SignalEventDefinition"] = ["bpmn:EventDefinition"],
        ["bpmn:Signal"] = ["bpmn:RootElement"],
        ["bpmn:ImplicitThrowEvent"] = ["bpmn:ThrowEvent"],
        ["bpmn:DataState"] = ["bpmn:BaseElement"],
        ["bpmn:ItemAwareElement"] = ["bpmn:BaseElement"],
        ["bpmn:DataAssociation"] = ["bpmn:BaseElement"],
        ["bpmn:DataInput"] = ["bpmn:ItemAwareElement"],
        ["bpmn:DataOutput"] = ["bpmn:ItemAwareElement"],
        ["bpmn:InputSet"] = ["bpmn:BaseElement"],
        ["bpmn:OutputSet"] = ["bpmn:BaseElement"],
        ["bpmn:Property"] = ["bpmn:ItemAwareElement"],
        ["bpmn:DataInputAssociation"] = ["bpmn:DataAssociation"],
        ["bpmn:DataOutputAssociation"] = ["bpmn:DataAssociation"],
        ["bpmn:InputOutputSpecification"] = ["bpmn:BaseElement"],
        ["bpmn:DataObject"] = ["bpmn:FlowElement", "bpmn:ItemAwareElement"],
        ["bpmn:InputOutputBinding"] = ["bpmn:"],
        ["bpmn:Assignment"] = ["bpmn:BaseElement"],
        ["bpmn:DataStore"] = ["bpmn:RootElement", "bpmn:ItemAwareElement"],
        ["bpmn:DataStoreReference"] = ["bpmn:ItemAwareElement", "bpmn:FlowElement"],
        ["bpmn:DataObjectReference"] = ["bpmn:ItemAwareElement", "bpmn:FlowElement"],
        ["bpmn:ConversationLink"] = ["bpmn:BaseElement"],
        ["bpmn:ConversationAssociation"] = ["bpmn:BaseElement"],
        ["bpmn:CallConversation"] = ["bpmn:ConversationNode"],
        ["bpmn:Conversation"] = ["bpmn:ConversationNode"],
        ["bpmn:SubConversation"] = ["bpmn:ConversationNode"],
        ["bpmn:ConversationNode"] = ["bpmn:InteractionNode", "bpmn:BaseElement"],
        ["bpmn:GlobalConversation"] = ["bpmn:Collaboration"],
        ["bpmn:PartnerEntity"] = ["bpmn:RootElement"],
        ["bpmn:PartnerRole"] = ["bpmn:RootElement"],
        ["bpmn:CorrelationProperty"] = ["bpmn:RootElement"],
        ["bpmn:Error"] = ["bpmn:RootElement"],
        ["bpmn:CorrelationKey"] = ["bpmn:BaseElement"],
        ["bpmn:Expression"] = ["bpmn:BaseElement"],
        ["bpmn:FormalExpression"] = ["bpmn:Expression"],
        ["bpmn:Message"] = ["bpmn:RootElement"],
        ["bpmn:ItemDefinition"] = ["bpmn:RootElement"],
        ["bpmn:FlowElement"] = ["bpmn:BaseElement"],
        ["bpmn:SequenceFlow"] = ["bpmn:FlowElement"],
        ["bpmn:FlowElementsContainer"] = ["bpmn:BaseElement"],
        ["bpmn:CallableElement"] = ["bpmn:RootElement"],
        ["bpmn:FlowNode"] = ["bpmn:FlowElement"],
        ["bpmn:CorrelationPropertyRetrievalExpression"] = ["bpmn:BaseElement"],
        ["bpmn:CorrelationPropertyBinding"] = ["bpmn:BaseElement"],
        ["bpmn:Resource"] = ["bpmn:RootElement"],
        ["bpmn:ResourceParameter"] = ["bpmn:BaseElement"],
        ["bpmn:CorrelationSubscription"] = ["bpmn:BaseElement"],
        ["bpmn:MessageFlow"] = ["bpmn:BaseElement"],
        ["bpmn:MessageFlowAssociation"] = ["bpmn:BaseElement"],
        ["bpmn:InteractionNode"] = ["bpmn:"],
        ["bpmn:Participant"] = ["bpmn:InteractionNode", "bpmn:BaseElement"],
        ["bpmn:ParticipantAssociation"] = ["bpmn:BaseElement"],
        ["bpmn:ParticipantMultiplicity"] = ["bpmn:BaseElement"],
        ["bpmn:Collaboration"] = ["bpmn:RootElement"],
        ["bpmn:ChoreographyActivity"] = ["bpmn:FlowNode"],
        ["bpmn:CallChoreography"] = ["bpmn:ChoreographyActivity"],
        ["bpmn:SubChoreography"] = ["bpmn:ChoreographyActivity", "bpmn:FlowElementsContainer"],
        ["bpmn:ChoreographyTask"] = ["bpmn:ChoreographyActivity"],
        ["bpmn:Choreography"] = ["bpmn:Collaboration", "bpmn:FlowElementsContainer"],
        ["bpmn:GlobalChoreographyTask"] = ["bpmn:Choreography"],
        ["bpmn:TextAnnotation"] = ["bpmn:Artifact"],
        ["bpmn:Group"] = ["bpmn:Artifact"],
        ["bpmn:Association"] = ["bpmn:Artifact"],
        ["bpmn:Category"] = ["bpmn:RootElement"],
        ["bpmn:Artifact"] = ["bpmn:BaseElement"],
        ["bpmn:CategoryValue"] = ["bpmn:BaseElement"],
        ["bpmn:Activity"] = ["bpmn:FlowNode"],
        ["bpmn:ServiceTask"] = ["bpmn:Task"],
        ["bpmn:SubProcess"] = ["bpmn:Activity", "bpmn:FlowElementsContainer", "bpmn:InteractionNode"],
        ["bpmn:LoopCharacteristics"] = ["bpmn:BaseElement"],
        ["bpmn:MultiInstanceLoopCharacteristics"] = ["bpmn:LoopCharacteristics"],
        ["bpmn:StandardLoopCharacteristics"] = ["bpmn:LoopCharacteristics"],
        ["bpmn:CallActivity"] = ["bpmn:Activity", "bpmn:InteractionNode"],
        ["bpmn:Task"] = ["bpmn:Activity", "bpmn:InteractionNode"],
        ["bpmn:SendTask"] = ["bpmn:Task"],
        ["bpmn:ReceiveTask"] = ["bpmn:Task"],
        ["bpmn:ScriptTask"] = ["bpmn:Task"],
        ["bpmn:BusinessRuleTask"] = ["bpmn:Task"],
        ["bpmn:AdHocSubProcess"] = ["bpmn:SubProcess"],
        ["bpmn:Transaction"] = ["bpmn:SubProcess"],
        ["bpmn:GlobalScriptTask"] = ["bpmn:GlobalTask"],
        ["bpmn:GlobalBusinessRuleTask"] = ["bpmn:GlobalTask"],
        ["bpmn:ComplexBehaviorDefinition"] = ["bpmn:BaseElement"],
        ["bpmn:ResourceRole"] = ["bpmn:BaseElement"],
        ["bpmn:ResourceParameterBinding"] = ["bpmn:BaseElement"],
        ["bpmn:ResourceAssignmentExpression"] = ["bpmn:BaseElement"],
        ["bpmn:Import"] = ["bpmn:"],
        ["bpmn:Definitions"] = ["bpmn:BaseElement"],
    };

    /// <summary>Converts an editor wire name or BPMN local name to a qualified moddle type name.</summary>
    public static string QualifiedName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is "eventSubProcess" or "EventSubProcess" or "bpmn:EventSubProcess") return "bpmn:SubProcess";
        if (name.Contains(':') || name == "Element") return name;
        return "bpmn:" + char.ToUpperInvariant(name[0]) + name[1..];
    }

    /// <summary>Whether a BPMN type equals or inherits a base type, including multiple inheritance.</summary>
    public static bool IsA(string bpmnType, string baseType)
    {
        var expected = QualifiedName(baseType);
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(QualifiedName(bpmnType));
        while (pending.TryPop(out var current))
        {
            if (current == expected) return true;
            if (!visited.Add(current)) continue;
            if (Parents.TryGetValue(current, out var parents))
                foreach (var parent in parents) pending.Push(parent);
        }
        return false;
    }
}
