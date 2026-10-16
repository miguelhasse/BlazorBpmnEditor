using System.Text.Json.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Model;

/// <summary>
/// Every drawable flow element type in scope (specification section 6 <c>flowNode.type</c>).
/// </summary>
/// <remarks>
/// Complete by design. Choreography and Conversation types are excluded (section 8), but
/// Complex Gateway, Ad-Hoc Sub-Process, Transaction and Event Sub-Process are all first-class
/// types here rather than flags, because the notation, the stencil and the connection matrices
/// all treat them as distinct kinds.
///
/// Serialised as the exact camelCase strings the canvas contract declares; the wire names are
/// pinned per member so renaming a C# constant cannot silently change the protocol.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<BpmnFlowNodeType>))]
public enum BpmnFlowNodeType
{
    /// <summary>Start Event.</summary>
    [JsonStringEnumMemberName("startEvent")] StartEvent,

    /// <summary>End Event.</summary>
    [JsonStringEnumMemberName("endEvent")] EndEvent,

    /// <summary>Intermediate Catch Event.</summary>
    [JsonStringEnumMemberName("intermediateCatchEvent")] IntermediateCatchEvent,

    /// <summary>Intermediate Throw Event.</summary>
    [JsonStringEnumMemberName("intermediateThrowEvent")] IntermediateThrowEvent,

    /// <summary>Boundary Event, always attached to an Activity.</summary>
    [JsonStringEnumMemberName("boundaryEvent")] BoundaryEvent,

    /// <summary>Abstract Task with no type glyph.</summary>
    [JsonStringEnumMemberName("task")] Task,

    /// <summary>User Task.</summary>
    [JsonStringEnumMemberName("userTask")] UserTask,

    /// <summary>Service Task.</summary>
    [JsonStringEnumMemberName("serviceTask")] ServiceTask,

    /// <summary>Script Task.</summary>
    [JsonStringEnumMemberName("scriptTask")] ScriptTask,

    /// <summary>Manual Task.</summary>
    [JsonStringEnumMemberName("manualTask")] ManualTask,

    /// <summary>Business Rule Task.</summary>
    [JsonStringEnumMemberName("businessRuleTask")] BusinessRuleTask,

    /// <summary>Send Task.</summary>
    [JsonStringEnumMemberName("sendTask")] SendTask,

    /// <summary>Receive Task.</summary>
    [JsonStringEnumMemberName("receiveTask")] ReceiveTask,

    /// <summary>Embedded Sub-Process.</summary>
    [JsonStringEnumMemberName("subProcess")] SubProcess,

    /// <summary>Event Sub-Process: no external Sequence Flow crosses its boundary.</summary>
    [JsonStringEnumMemberName("eventSubProcess")] EventSubProcess,

    /// <summary>Transaction Sub-Process, drawn with a doubled border.</summary>
    [JsonStringEnumMemberName("transaction")] Transaction,

    /// <summary>Ad-Hoc Sub-Process.</summary>
    [JsonStringEnumMemberName("adHocSubProcess")] AdHocSubProcess,

    /// <summary>Call Activity: references a global Process or Task, never owns local content.</summary>
    [JsonStringEnumMemberName("callActivity")] CallActivity,

    /// <summary>Exclusive Gateway.</summary>
    [JsonStringEnumMemberName("exclusiveGateway")] ExclusiveGateway,

    /// <summary>Inclusive Gateway.</summary>
    [JsonStringEnumMemberName("inclusiveGateway")] InclusiveGateway,

    /// <summary>Parallel Gateway.</summary>
    [JsonStringEnumMemberName("parallelGateway")] ParallelGateway,

    /// <summary>Complex Gateway.</summary>
    [JsonStringEnumMemberName("complexGateway")] ComplexGateway,

    /// <summary>Event-Based Gateway.</summary>
    [JsonStringEnumMemberName("eventBasedGateway")] EventBasedGateway,
}

/// <summary>Event trigger types (specification section 1.1).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BpmnEventDefinitionType>))]
public enum BpmnEventDefinitionType
{
    /// <summary>No trigger — a plain (None) event.</summary>
    [JsonStringEnumMemberName("none")] None,

    /// <summary>Message.</summary>
    [JsonStringEnumMemberName("message")] Message,

    /// <summary>Timer. Catch-only, and always drawn stroke-only.</summary>
    [JsonStringEnumMemberName("timer")] Timer,

    /// <summary>Error.</summary>
    [JsonStringEnumMemberName("error")] Error,

    /// <summary>Escalation.</summary>
    [JsonStringEnumMemberName("escalation")] Escalation,

    /// <summary>Cancel. Valid only on a Transaction boundary or a Transaction End Event.</summary>
    [JsonStringEnumMemberName("cancel")] Cancel,

    /// <summary>Compensation.</summary>
    [JsonStringEnumMemberName("compensation")] Compensation,

    /// <summary>Conditional. Catch-only, and always drawn stroke-only.</summary>
    [JsonStringEnumMemberName("conditional")] Conditional,

    /// <summary>Link, paired throw/catch within one Process.</summary>
    [JsonStringEnumMemberName("link")] Link,

    /// <summary>Signal.</summary>
    [JsonStringEnumMemberName("signal")] Signal,

    /// <summary>Multiple: any one of several declared triggers.</summary>
    [JsonStringEnumMemberName("multiple")] Multiple,

    /// <summary>Parallel Multiple: all declared triggers required.</summary>
    [JsonStringEnumMemberName("parallelMultiple")] ParallelMultiple,

    /// <summary>Terminate. End Events only.</summary>
    [JsonStringEnumMemberName("terminate")] Terminate,
}

/// <summary>Activity loop and multi-instance characteristics (specification section 6).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BpmnLoopCharacteristics>))]
public enum BpmnLoopCharacteristics
{
    /// <summary>No loop marker.</summary>
    [JsonStringEnumMemberName("none")] None,

    /// <summary>Standard loop.</summary>
    [JsonStringEnumMemberName("standard")] Standard,

    /// <summary>Sequential multi-instance.</summary>
    [JsonStringEnumMemberName("multiInstanceSequential")] MultiInstanceSequential,

    /// <summary>Parallel multi-instance.</summary>
    [JsonStringEnumMemberName("multiInstanceParallel")] MultiInstanceParallel,
}

/// <summary>Association direction (specification section 6).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BpmnAssociationDirection>))]
public enum BpmnAssociationDirection
{
    /// <summary>Undirected.</summary>
    [JsonStringEnumMemberName("none")] None,

    /// <summary>Directed towards the target.</summary>
    [JsonStringEnumMemberName("one")] One,

    /// <summary>Directed at both ends.</summary>
    [JsonStringEnumMemberName("both")] Both,
}

/// <summary>Data Association direction (specification section 2.9).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BpmnDataAssociationKind>))]
public enum BpmnDataAssociationKind
{
    /// <summary>Data flows into the Activity or throwing Event.</summary>
    [JsonStringEnumMemberName("input")] Input,

    /// <summary>Data flows out of the Activity or catching Event.</summary>
    [JsonStringEnumMemberName("output")] Output,
}
