using Hasseware.AspNetCore.Components.Bpmn.Editing;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools;

/// <summary>A tool host with a user in front of it: an undo history to step through and a selection to show.</summary>
/// <remarks>Optional: a host that loads a stored document for an agent has neither.</remarks>
public interface IBpmnInteractiveHost
{
    /// <summary>Who asked for the edit an undo would reverse, or null when there is none.</summary>
    public BpmnEditOrigin? UndoOrigin { get; }

    /// <summary>Who asked for the edit a redo would reapply, or null when there is none.</summary>
    public BpmnEditOrigin? RedoOrigin { get; }

    /// <summary>Steps back one edit.</summary>
    /// <returns>A task that completes when the document has changed.</returns>
    public Task UndoAsync();

    /// <summary>Reapplies one undone edit.</summary>
    /// <returns>A task that completes when the document has changed.</returns>
    public Task RedoAsync();

    /// <summary>Selects elements, so the user sees what an agent means.</summary>
    /// <param name="elementIds">The elements; empty clears the selection.</param>
    /// <returns>A task that completes when the selection is shown.</returns>
    public Task SelectAsync(IReadOnlyList<string> elementIds);
}
