using Hasseware.AspNetCore.Components.Bpmn.Accessibility;
using Hasseware.AspNetCore.Components.Bpmn.Interop;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// Keyboard and screen-reader navigation of the diagram (implementation plan M7).
/// </summary>
/// <remarks>
/// The exit criterion is that the whole diagram is navigable without a mouse. That means two
/// things the drawn shapes cannot provide however carefully they are labelled: an order to walk
/// them in, and the connections between them. Both come from the model, so both are computed here
/// rather than read back off the paper.
/// </remarks>
public sealed partial class BpmnEditorSession
{

    /// <summary>
    /// Whether the parallel accessible tree is rendered.
    /// </summary>
    /// <remarks>
    /// On by default and offered as a parameter only for a host that has built its own equivalent.
    /// Turning it off leaves the diagram unreachable by keyboard, so it is not a decision to make
    /// casually — which is why the default is the other way.
    /// </remarks>
    internal bool ShowAccessibleTree { get; set; } = true;

    /// <summary>Accessible name of the navigable tree.</summary>
    internal string? AccessibleTreeLabel { get; set; }

    /// <summary>The diagram as a navigable graph, in reading order.</summary>
    public IReadOnlyList<BpmnGraphNode> AccessibleGraph => CachedAccessibleGraph();

    /// <summary>Test seam: replaces the graph derived from the drawn document.</summary>
    internal IReadOnlyList<BpmnGraphNode>? AccessibleGraphOverride { get; set; }

    /// <summary>Test seam: receives every expansion toggle instead of the document.</summary>
    internal Func<string, Task>? ToggleOverride { get; set; }

    /// <summary>Test seam: receives every activation instead of the canvas.</summary>
    internal Func<string, Task>? ActivateOverride { get; set; }

    /// <summary>The element the keyboard reader is on.</summary>
    /// <remarks>
    /// Follows the selection when exactly one element is selected, so a reader who clicks and a
    /// reader who arrows are looking at the same place. A multi-selection says nothing about where
    /// a cursor should be, so it leaves the cursor alone.
    /// </remarks>
    public string? AccessibleCurrent { get => _selection.Count == 1 ? _selection[0] : field; private set; }

    /// <summary>
    /// Moves the reader to an element: selects it and brings it into view.
    /// </summary>
    /// <remarks>
    /// Both, not either. Selecting alone leaves a sighted keyboard user watching an unchanged
    /// screen while the announcements move somewhere off-canvas, and scrolling alone loses the
    /// outline that says which element is being talked about.
    /// </remarks>
    /// <param name="elementId">The element to move to.</param>
    public async Task GoToAccessibleAsync(string elementId)
    {
        if (string.IsNullOrEmpty(elementId)) return;

        AccessibleCurrent = elementId;

        await SetSelectionAsync([elementId]).ConfigureAwait(true);
        await SetViewportAsync(ViewportCommand.RevealElement(elementId)).ConfigureAwait(true);
    }

    /// <summary>
    /// Activates the element the reader is on, which opens its label for editing.
    /// </summary>
    /// <remarks>
    /// Read-only keeps the reader's place and refuses the edit, in the same way every other
    /// mutation surface is closed rather than made to fail after the fact (section 7).
    /// </remarks>
    /// <param name="elementId">The element to activate.</param>
    public async Task ActivateAccessibleAsync(string elementId)
    {
        if (IsReadOnly || string.IsNullOrEmpty(elementId)) return;
        if (ActivateOverride is { } intercept)
        {
            await intercept(elementId).ConfigureAwait(true);
            return;
        }

        if (_canvas is null) return;

        await _canvas.EditLabelAsync(elementId).ConfigureAwait(true);
    }
}
