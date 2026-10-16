using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>
/// The document being edited, and the history of what it was before.
/// </summary>
/// <remarks>
/// <para>
/// History is a stack of document snapshots, not of inverse commands. The specification asks undo
/// to restore geometry, exact attachment coordinates, routing constraints, selection, references
/// and containment <em>exactly</em>; a snapshot does that by definition, where an inverse command
/// is a second implementation of every edit and the one that quietly drifts.
/// </para>
/// <para>
/// This is affordable only because a <see cref="BpmnDocument"/> is immutable. An entry is a
/// reference to a record whose unchanged collections are shared with its neighbours, so the cost
/// of a step is the part that actually changed, not a copy of the diagram.
/// </para>
/// <para>
/// Not thread-safe, and not meant to be: it belongs to one editor component on one Blazor
/// synchronisation context.
/// </para>
/// </remarks>
public sealed class BpmnEditHistory
{
    private readonly List<Entry> _undo = [];
    private readonly List<Entry> _redo = [];
    private EditingOptions _options;

    /// <summary>Starts a history at a document.</summary>
    /// <param name="document">The document as it currently stands.</param>
    /// <param name="options">Editing options, including the history depth and collision clearance.</param>
    public BpmnEditHistory(BpmnDocument document, EditingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        Document = document;
        _options = options ?? EditingOptions.Default;
    }

    /// <summary>The current document.</summary>
    public BpmnDocument Document { get; private set; }

    /// <summary>Whether there is a step to undo.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Whether there is a step to redo.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>What undoing would reverse, for a menu label.</summary>
    public string? UndoLabel => CanUndo ? _undo[^1].Label : null;

    /// <summary>What redoing would reapply, for a menu label.</summary>
    public string? RedoLabel => CanRedo ? _redo[^1].Label : null;

    /// <summary>Who asked for the edit undoing would reverse, or null when there is none.</summary>
    /// <remarks>
    /// Lets an agent undo only its own work: it checks this is its own origin before undoing.
    /// </remarks>
    public BpmnEditOrigin? UndoOrigin => CanUndo ? _undo[^1].Origin : null;

    /// <summary>Who asked for the edit redoing would reapply, or null when there is none.</summary>
    public BpmnEditOrigin? RedoOrigin => CanRedo ? _redo[^1].Origin : null;

    /// <summary>Applies the current host configuration to subsequent commands.</summary>
    /// <remarks>
    /// Configuration can change while a document remains open. In particular, the event edit
    /// policy must not be captured by an old history instance; lowering history depth also trims
    /// both available stacks while retaining their newest entries.
    /// </remarks>
    /// <param name="options">The current editing configuration.</param>
    public void UpdateOptions(EditingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;

        TrimOldest(_undo, options.HistoryDepth);
        TrimOldest(_redo, options.HistoryDepth);
    }

    /// <summary>
    /// Applies a command, recording it if it succeeded.
    /// </summary>
    /// <remarks>
    /// A refused command leaves the history untouched as well as the document: the user's undo
    /// stack should not gain an entry for something that did not happen.
    /// </remarks>
    /// <param name="command">The edit to apply.</param>
    /// <returns>The outcome, whose document is always the one to use next.</returns>
    public BpmnEditResult Apply(BpmnEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = BpmnDocumentEditor.Apply(Document, command, _options);
        if (!result.IsApplied) return result;

        _undo.Add(new Entry(Document, command.Label, command.Origin));

        // A new edit invalidates the redo branch: there is no longer a single future to go back to.
        _redo.Clear();

        if (_undo.Count > _options.HistoryDepth) _undo.RemoveAt(0);

        Document = result.Document;
        return result;
    }

    /// <summary>
    /// Replaces the document without recording a step.
    /// </summary>
    /// <remarks>
    /// For loading a different document, not for editing one. The history is cleared because its
    /// entries describe a document that is no longer open, and offering to undo into it would
    /// silently discard whatever is.
    /// </remarks>
    /// <param name="document">The document to open.</param>
    public void Reset(BpmnDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        Document = document;
        _undo.Clear();
        _redo.Clear();
    }

    /// <summary>Steps back one edit.</summary>
    /// <returns>The document to use, unchanged when there was nothing to undo.</returns>
    public BpmnDocument Undo()
    {
        if (!CanUndo) return Document;

        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(new Entry(Document, entry.Label, entry.Origin));
        Document = entry.Document;

        return Document;
    }

    /// <summary>Steps forward one edit.</summary>
    /// <returns>The document to use, unchanged when there was nothing to redo.</returns>
    public BpmnDocument Redo()
    {
        if (!CanRedo) return Document;

        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(new Entry(Document, entry.Label, entry.Origin));
        Document = entry.Document;

        return Document;
    }

    private static void TrimOldest(List<Entry> entries, int depth)
    {
        if (entries.Count > depth) entries.RemoveRange(0, entries.Count - depth);
    }

    /// <summary>One step: the document as it was, and the name and origin of the edit that left it.</summary>
    private sealed record Entry(BpmnDocument Document, string Label, BpmnEditOrigin Origin);
}
