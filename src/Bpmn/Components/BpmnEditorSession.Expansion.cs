using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// What the editor remembers about a Sub-Process it has expanded (specification section 1.2.1).
/// </summary>
/// <remarks>
/// <para>
/// "It restores the last expanded bounds in the current session when available." Session, not
/// document: a size the user dragged a container to is a fact about this sitting, not something
/// the file should carry, and writing it into the DI would change what a file says for a reason
/// nobody asked for (sections 10.6 and 11).
/// </para>
/// <para>
/// So it lives here, outside the document and outside the history. Undo and redo deliberately do
/// not touch it — it is a hint about what to do next, not state anyone can be looking at, and an
/// undo that also rewound the hint would make the next expansion depend on how many times the user
/// had pressed Ctrl+Z.
/// </para>
/// </remarks>
public sealed partial class BpmnEditorSession
{
    /// <summary>
    /// The size each Sub-Process was last expanded to, and the collapsed box it went back into.
    /// </summary>
    private readonly Dictionary<string, (Bounds Expanded, Bounds Collapsed)> _expansions = new(StringComparer.Ordinal);

    /// <summary>
    /// The document as a read-only reader has opened it up, or <see langword="null"/> for none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Section 1.2.1: in read-only the same gestures "perform a transient local inspection toggle
    /// and must not dirty, serialize, or enter document history". So the command is applied — the
    /// arithmetic for moving child DI between planes, sizing the container and making room lives
    /// in one place and this is not going to be a second copy of it — and the result is kept here
    /// as something to draw rather than becoming the document of record.
    /// </para>
    /// <para>
    /// Nothing reaches the host: no OnDocumentChanged, no history entry, no dirty flag. Export
    /// writes <see cref="Document"/>, which never saw any of it.
    /// </para>
    /// </remarks>
    private BpmnDocument? _inspection;

    /// <summary>
    /// Records what a Sub-Process looked like expanded, just before it is collapsed.
    /// </summary>
    /// <param name="elementId">The Sub-Process about to close.</param>
    private void RememberSizeOf(string elementId)
    {
        if (DrawnDocument is not { } document) return;
        if (ShapeOnActivePlane(document, elementId) is not { } shape) return;

        var collapsed = BpmnSubProcessLayout.CollapsedBounds(shape.Bounds, _snapshot.Editing);

        if (_expansions.TryGetValue(elementId, out var previous) && previous == (shape.Bounds, collapsed)) return;
        _expansions[elementId] = (shape.Bounds, collapsed);
        _expansionsDirty = true;
        NotifyStateChanged();
    }

    /// <summary>
    /// The size to restore, when the remembered one is still the right answer.
    /// </summary>
    /// <remarks>
    /// Offered only while the shape is exactly where the collapse left it. Once the user has moved
    /// or resized the collapsed box, the remembered rectangle describes somewhere it no longer is,
    /// and restoring it would move the Sub-Process out from under the pointer that just expanded
    /// it. Forgetting is the honest answer; the content still decides the size.
    /// </remarks>
    /// <param name="elementId">The Sub-Process about to open.</param>
    /// <returns>The remembered bounds, or <see langword="null"/> to compute fresh ones.</returns>
    private Bounds? RememberedSizeOf(string elementId)
    {
        if (!_expansions.TryGetValue(elementId, out var remembered)) return null;
        if (DrawnDocument is not { } document) return null;
        if (ShapeOnActivePlane(document, elementId) is not { } shape) return null;

        return shape.Bounds == remembered.Collapsed ? remembered.Expanded : null;
    }

    /// <summary>
    /// Opens a Sub-Process, or closes it, whichever it is not.
    /// </summary>
    /// <remarks>
    /// The one place a toggle is the right shape. The commands are a pair on purpose — applying
    /// the same one twice has to give the same document — but a reader pressing a key is asking
    /// for the other state, not for a particular one, and only the editor knows which that is.
    /// </remarks>
    /// <param name="elementId">The Sub-Process to open or close.</param>
    /// <returns>A task that completes once the document has been redrawn.</returns>
    public async Task ToggleExpansionAsync(string elementId)
    {
        if (string.IsNullOrEmpty(elementId)) return;
        if (ToggleOverride is { } intercept)
        {
            await intercept(elementId).ConfigureAwait(true);
            return;
        }

        if (EditedDocument is not { } document) return;
        if (ShapeOnActivePlane(document, elementId) is not { } shape) return;

        if (IsReadOnly)
        {
            await InspectExpansionAsync(elementId, shape.IsExpanded != true).ConfigureAwait(true);
            return;
        }

        if (EnsureHistory() is not { } history) return;

        if (shape.IsExpanded == true)
        {
            RememberSizeOf(elementId);

            await AfterEditAsync(history.Apply(
                new CollapseSubProcessCommand(elementId, HostDiagramId: ActiveDiagramId))).ConfigureAwait(true);

            return;
        }

        await AfterEditAsync(history.Apply(
            new ExpandSubProcessCommand(elementId, RememberedSizeOf(elementId), ActiveDiagramId))).ConfigureAwait(true);
    }

    /// <summary>
    /// Opens or closes a Sub-Process for looking at, without changing the document.
    /// </summary>
    /// <param name="elementId">The Sub-Process to open or close.</param>
    /// <param name="expand">Whether to open it.</param>
    /// <returns>A task that completes once the canvas has been redrawn.</returns>
    private async Task InspectExpansionAsync(string elementId, bool expand)
    {
        if (DrawnDocument is not { } current) return;

        BpmnEditCommand command = expand
            ? new ExpandSubProcessCommand(elementId, RememberedSizeOf(elementId), ActiveDiagramId)
            : new CollapseSubProcessCommand(elementId, HostDiagramId: ActiveDiagramId);

        if (!expand) RememberSizeOf(elementId);

        var result = BpmnDocumentEditor.Apply(current, command, _snapshot.Editing);
        if (!result.IsApplied) return;

        _inspection = result.Document;

        /*
         * The canvas draws EditedDocument, so changing what that resolves to and re-rendering is
         * the whole of it. Deliberately not AfterEditAsync: that is the path that records history,
         * marks the document dirty and tells the host something changed, and none of those is true
         * here.
         */
        NotifyStateChanged();

        await Task.CompletedTask.ConfigureAwait(true);
    }

    /// <summary>Forgets everything, because the document being described is gone.</summary>
    private void ForgetExpansions()
    {
        if (_expansions.Count > 0) _expansionsDirty = true;
        _expansions.Clear();
        _inspection = null;
    }

    private BpmnShape? ShapeOnActivePlane(BpmnDocument document, string elementId) => document.Diagrams
        .Where(diagram => ActiveDiagramId is null || diagram.Id == ActiveDiagramId)
        .SelectMany(diagram => diagram.Plane.Shapes)
        .FirstOrDefault(shape => shape.BpmnElement == elementId);
}
