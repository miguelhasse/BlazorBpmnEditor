using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;

using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>One step on the way to the diagram now shown.</summary>
/// <param name="DiagramId">The diagram.</param>
/// <param name="Label">What to call it in a breadcrumb.</param>
/// <param name="ElementId">The element that was opened to get here, for a drill-down.</param>
public sealed record BpmnDiagramStep(string DiagramId, string Label, string? ElementId = null);

/// <summary>
/// Where the canvas has just navigated, and how it got there.
/// </summary>
/// <param name="DiagramId">The diagram now shown.</param>
/// <param name="Path">
/// The whole route from the root diagram to this one, so a host can render a breadcrumb without
/// tracking navigation itself.
/// </param>
/// <param name="Origin">What caused the move.</param>
public sealed record BpmnDiagramNavigated(
    string DiagramId,
    IReadOnlyList<BpmnDiagramStep> Path,
    BpmnNavigationOrigin Origin);

/// <summary>What caused a diagram change.</summary>
public enum BpmnNavigationOrigin
{
    /// <summary>The document was loaded, or its first diagram was chosen for it.</summary>
    Load,

    /// <summary>The user picked a diagram from a list.</summary>
    Picker,

    /// <summary>The user opened a Call Activity or a collapsed Sub-Process.</summary>
    DrillDown,

    /// <summary>The user went back up the path they had drilled down.</summary>
    Ascend,

    /// <summary>Something was revealed that lives on another plane.</summary>
    Reveal,
}

/// <summary>
/// Moving between the diagrams one document declares (implementation plan M6).
/// </summary>
/// <remarks>
/// <para>
/// A BPMN file may draw several things, and a Call Activity's content is a different Process
/// altogether. Which plane is on screen is a view decision: the semantic model, the edit history
/// and any unsaved work belong to the document, so moving between diagrams can neither lose an edit
/// nor commit one. That is what makes it safe to offer in Readonly too.
/// </para>
/// <para>
/// The path is kept here rather than on the canvas, because it is a fact about the document — which
/// Call Activity leads to which Process — and the canvas only ever knows which plane it is
/// currently painting.
/// </para>
/// </remarks>
public sealed partial class BpmnEditorSession
{
    private readonly List<BpmnDiagramStep> _path = [];

    /// <summary>
    /// The route from the root diagram to the one now shown.
    /// </summary>
    /// <remarks>
    /// For a host's breadcrumb. Always at least one step once a document is loaded.
    /// </remarks>
    public IReadOnlyList<BpmnDiagramStep> NavigationPath => State.NavigationPath;

    /// <summary>Every diagram the document declares, for a picker.</summary>
    public IReadOnlyList<BpmnDiagramTarget> Diagrams =>
        CachedDiagrams();

    /// <summary>Test seam: replaces the diagrams the document declares.</summary>
    internal IReadOnlyList<BpmnDiagramTarget>? DiagramsOverride { get; set; }

    /// <summary>Test seam: replaces drill resolution.</summary>
    internal Func<string, BpmnDrillTarget>? DrillTargetOverride { get; set; }

    /// <summary>Test seam: receives every navigation instead of the canvas.</summary>
    internal Func<string, Task>? NavigateOverride { get; set; }

    /// <summary>The diagram the canvas is showing.</summary>
    public string? ActiveDiagramId => _activeDiagramId ?? DiagramId;

    /// <summary>Raised whenever the shown diagram changes, with the whole path to it.</summary>
    internal EventCallback<BpmnDiagramNavigated> OnDiagramNavigated { get; set; }

    private string? _activeDiagramId;

    /// <summary>
    /// Whether an element opens a diagram, and what to say when it does not.
    /// </summary>
    /// <param name="elementId">The element in question.</param>
    /// <returns>The target, or a refusal carrying the reason to show on a disabled control.</returns>
    public BpmnDrillTarget DrillTargetFor(string elementId) => DrillTargetOverride is { } drill ? drill(elementId) : EditedDocument is { } document
        ? BpmnDiagramNavigation.DrillTargetFor(document, elementId)
        : new BpmnDrillTarget(null, BpmnDrillRefusal.NotDrillable, "No document is loaded.");

    /// <summary>
    /// Shows another of the document's diagrams.
    /// </summary>
    /// <remarks>
    /// Chosen from a list rather than drilled into, so the path restarts at that diagram: the user
    /// did not arrive through anything, and a breadcrumb claiming they did would offer a way back
    /// to a plane they were never on.
    /// </remarks>
    /// <param name="diagramId">The diagram to show.</param>
    /// <returns>A task that completes when the canvas has switched.</returns>
    public Task GoToDiagramAsync(string diagramId) =>
        NavigateAsync(diagramId, BpmnNavigationOrigin.Picker, resetPath: true);

    /// <summary>
    /// Opens the diagram behind a Call Activity or a collapsed Sub-Process.
    /// </summary>
    /// <param name="elementId">The element to open.</param>
    /// <returns>The refusal when it could not be opened, so a caller can report it.</returns>
    public async Task<BpmnDrillTarget> DrillIntoAsync(string elementId)
    {
        var target = DrillTargetFor(elementId);
        if (target.DiagramId is not { } diagramId) return target;

        await NavigateAsync(diagramId, BpmnNavigationOrigin.DrillDown, resetPath: false, elementId)
            .ConfigureAwait(true);

        return target;
    }

    /// <summary>
    /// Goes back to a diagram already on the path, dropping everything after it.
    /// </summary>
    /// <param name="diagramId">A diagram the path already contains.</param>
    /// <returns>A task that completes when the canvas has switched.</returns>
    public async Task AscendToAsync(string diagramId)
    {
        var index = _path.FindIndex(step => step.DiagramId == diagramId);
        if (index < 0) return;

        _path.RemoveRange(index + 1, _path.Count - index - 1);

        await ShowAsync(diagramId, BpmnNavigationOrigin.Ascend).ConfigureAwait(true);
    }

    /// <summary>
    /// Shows the diagram an element is drawn on, and selects it.
    /// </summary>
    /// <remarks>
    /// What a reference browser needs. A usage often names an element on another plane, and
    /// selecting it without going there first selects something nobody can see.
    /// </remarks>
    /// <param name="elementId">The element to reveal.</param>
    /// <returns>A task that completes once the element is shown and selected.</returns>
    public async Task RevealAsync(string elementId)
    {
        if (EditedDocument is not { } document) return;

        var diagramId = BpmnDiagramNavigation.DiagramShowing(document, elementId);

        if (diagramId is not null && diagramId != ActiveDiagramId)
        {
            await NavigateAsync(diagramId, BpmnNavigationOrigin.Reveal, resetPath: true).ConfigureAwait(true);
        }

        await SetSelectionAsync([elementId]).ConfigureAwait(true);
    }

    /// <summary>
    /// Starts the path afresh for a newly loaded document, then passes the load on to the host.
    /// </summary>
    /// <remarks>
    /// A load is the one navigation the canvas decides for itself — it picks the first diagram when
    /// nobody named one — so the path is seeded from what came back rather than from what was
    /// asked for.
    /// </remarks>
    internal async Task HandleDocumentLoadedAsync(DocumentLoaded payload)
    {
        var moved = _activeDiagramId != payload.DiagramId;

        _activeDiagramId = payload.DiagramId;

        /*
         * Only when this diagram is not already somewhere on the path. A load that arrives because
         * the user drilled in is the end of a route they walked, and re-seeding on it would throw
         * away the breadcrumb at the moment it became useful. A load of a different file has a
         * diagram the path has never heard of, and starts a new one.
         */
        if (!_path.Exists(step => step.DiagramId == payload.DiagramId))
        {
            _path.Clear();
            _path.Add(new BpmnDiagramStep(payload.DiagramId, LabelOfDiagram(payload.DiagramId)));
        }

        NotifyStateChanged();

        await OnDocumentLoaded.InvokeAsync(payload).ConfigureAwait(true);

        if (moved)
        {
            await OnDiagramNavigated
                .InvokeAsync(new BpmnDiagramNavigated(payload.DiagramId, [.. _path], BpmnNavigationOrigin.Load))
                .ConfigureAwait(true);
        }
    }

    private async Task NavigateAsync(
        string diagramId,
        BpmnNavigationOrigin origin,
        bool resetPath,
        string? throughElementId = null)
    {
        if (NavigateOverride is { } intercept)
        {
            await intercept(diagramId).ConfigureAwait(true);
            return;
        }

        if (resetPath)
        {
            _path.Clear();
            _path.Add(new BpmnDiagramStep(diagramId, LabelOfDiagram(diagramId)));
        }
        else if (!_path.Exists(step => step.DiagramId == diagramId))
        {
            _path.Add(new BpmnDiagramStep(diagramId, LabelOfDiagram(diagramId), throughElementId));
        }

        await ShowAsync(diagramId, origin).ConfigureAwait(true);
    }

    /// <summary>
    /// Puts a diagram on screen.
    /// </summary>
    /// <remarks>
    /// By moving the parameter the canvas renders from, not by calling into it. The canvas reloads
    /// whenever its <c>DiagramId</c> changes and compares what it drew against that parameter on
    /// every render — so an imperative switch is undone by the very next render, which is exactly
    /// what happened: the drill-down reached the right plane and was back on the old one before
    /// anything was painted.
    /// </remarks>
    private async Task ShowAsync(string diagramId, BpmnNavigationOrigin origin)
    {
        _activeDiagramId = diagramId;

        // The selection belonged to the plane being left, and its ids are not on this one.
        _selection = [];
        _selectionSnapshot = null;

        NotifyStateChanged();

        await OnDiagramNavigated
            .InvokeAsync(new BpmnDiagramNavigated(diagramId, [.. _path], origin))
            .ConfigureAwait(true);
    }

    private string LabelOfDiagram(string diagramId) => Diagrams
        .FirstOrDefault(target => target.DiagramId == diagramId)?.Label ?? diagramId;
}
