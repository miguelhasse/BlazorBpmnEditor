namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>
/// The BPMN semantic concept a standard property belongs to. General collects common name and
/// documentation fields; the remaining groups follow BPMN 2.0.2 semantic ownership.
/// </summary>
public enum BpmnPropertyGroupKind
{
    /// <summary>Common identity, name, documentation, and unclassified host properties.</summary>
    General,
    /// <summary>Activity compensation and default-flow properties.</summary>
    Activity,
    /// <summary>Event trigger and interruption properties.</summary>
    Event,
    /// <summary>Script Task language and script body.</summary>
    ScriptTask,
    /// <summary>Call Activity called-element reference.</summary>
    CallActivity,
    /// <summary>Send Task message reference.</summary>
    SendTask,
    /// <summary>Receive Task message reference.</summary>
    ReceiveTask,
    /// <summary>Gateway default-flow properties.</summary>
    Gateway,
    /// <summary>Complex Gateway activation condition and default flow.</summary>
    ComplexGateway,
    /// <summary>Sequence Flow condition and default-flow projection.</summary>
    SequenceFlow,
    /// <summary>Message Flow message reference.</summary>
    MessageFlow,
    /// <summary>Association direction.</summary>
    Association,
    /// <summary>Data Association assignment expressions.</summary>
    Assignment,
    /// <summary>Text Annotation text and format.</summary>
    TextAnnotation,
    /// <summary>Artifact Group category reference.</summary>
    Group,
    /// <summary>Data Object Reference target and projected collection flag.</summary>
    DataObjectReference,
    /// <summary>Data Store Reference target.</summary>
    DataStoreReference,
    /// <summary>Data State name.</summary>
    DataState,
    /// <summary>Process execution, type, and closure properties.</summary>
    Process,
    /// <summary>Standard and multi-instance Loop Characteristics, including loop data items.</summary>
    LoopCharacteristics,
    /// <summary>Multi-instance Complex Behavior Definition condition and event.</summary>
    ComplexBehaviorDefinition,
}
