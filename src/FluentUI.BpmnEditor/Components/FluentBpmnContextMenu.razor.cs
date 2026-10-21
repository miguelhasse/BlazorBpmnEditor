using Hasseware.AspNetCore.Components.Bpmn;

using Microsoft.FluentUI.AspNetCore.Components;

using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn;

public sealed partial class FluentBpmnContextMenu
{
    private static Icon CommandIcon(BpmnMenuCommand command) => command switch
    {
        BpmnMenuCommand.EditLabel => new Icons.Regular.Size20.Edit(),
        BpmnMenuCommand.Cut => new Icons.Regular.Size20.Cut(),
        BpmnMenuCommand.Copy => new Icons.Regular.Size20.Copy(),
        BpmnMenuCommand.Paste => new Icons.Regular.Size20.ClipboardPaste(),
        BpmnMenuCommand.Duplicate => new Icons.Regular.Size20.CopyAdd(),
        BpmnMenuCommand.Delete => new Icons.Regular.Size20.Delete(),
        BpmnMenuCommand.SelectAll => new Icons.Regular.Size20.SelectAllOn(),
        BpmnMenuCommand.FitToContent => new Icons.Regular.Size20.ArrowFit(),
        BpmnMenuCommand.Undo => new Icons.Regular.Size20.ArrowUndo(),
        BpmnMenuCommand.Redo => new Icons.Regular.Size20.ArrowRedo(),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown menu command."),
    };
}
