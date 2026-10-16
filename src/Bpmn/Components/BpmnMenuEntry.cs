namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>A command a context menu can invoke on the editor.</summary>
public enum BpmnMenuCommand
{
    /// <summary>Start editing the target's label in place (section 4.5).</summary>
    EditLabel,

    /// <summary>Cut the selection.</summary>
    Cut,

    /// <summary>Copy the selection.</summary>
    Copy,

    /// <summary>Paste the clipboard onto the active plane.</summary>
    Paste,

    /// <summary>Duplicate the selection.</summary>
    Duplicate,

    /// <summary>Delete the selection.</summary>
    Delete,

    /// <summary>Select everything on the active plane.</summary>
    SelectAll,

    /// <summary>Zoom the viewport to the content.</summary>
    FitToContent,

    /// <summary>Undo the last edit.</summary>
    Undo,

    /// <summary>Redo the last undone edit.</summary>
    Redo,
}

/// <summary>One entry in the menu.</summary>
/// <param name="Command">What it does.</param>
/// <param name="Label">What it says.</param>
/// <param name="IsEnabled">Whether it can be chosen now.</param>
public sealed record BpmnMenuEntry(BpmnMenuCommand Command, string Label, bool IsEnabled = true);
