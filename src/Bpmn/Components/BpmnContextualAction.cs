using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>What a contextual action does when it is chosen.</summary>
public enum BpmnActionKind
{
    /// <summary>Create a connected element after the selected one.</summary>
    Append,

    /// <summary>Edit the selected element's label in place.</summary>
    EditLabel,

    /// <summary>Delete the selected element.</summary>
    Delete,

    /// <summary>Open the full context menu for the selected element.</summary>
    OpenMenu,

    /// <summary>Create a Text Annotation associated with the selected shape.</summary>
    AddTextAnnotation,
}

/// <summary>One button on the contextual strip.</summary>
/// <param name="Kind">What it does.</param>
/// <param name="Label">Its accessible name and tooltip.</param>
/// <param name="Glyph">A one-character mark, which is decorative — the label carries the meaning.</param>
/// <param name="Type">The type to append, for <see cref="BpmnActionKind.Append"/>.</param>
/// <param name="EventDefinition">The trigger to append, for an Event.</param>
public sealed record BpmnContextualAction(
    BpmnActionKind Kind,
    string Label,
    string Glyph,
    BpmnFlowNodeType Type = BpmnFlowNodeType.Task,
    BpmnEventDefinitionType EventDefinition = BpmnEventDefinitionType.None)
{
    /// <summary>A stable key, since several appends differ only by the type they create.</summary>
    public string Key => $"{Kind}:{Type}:{EventDefinition}";
}
