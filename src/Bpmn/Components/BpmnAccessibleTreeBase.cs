using Hasseware.AspNetCore.Components.Bpmn.Accessibility;
using Hasseware.AspNetCore.Components.Bpmn.Interop;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// The logic of the diagram as a keyboard-navigable tree (implementation plan M7); a UI kit
/// supplies the markup.
/// </summary>
/// <remarks>
/// It holds no navigation rules of its own. Which element an arrow key lands on is
/// <see cref="BpmnGraphNavigation"/>'s decision, so it can be argued about in tests with named
/// cases rather than inferred from watching a browser.
/// </remarks>
public abstract class BpmnAccessibleTreeBase : BpmnCanvasWidgetBase<BpmnAccessibleTreeState>
{
    /// <inheritdoc />
    protected override bool ShouldRenderOnStateChange(BpmnEditorState previous, BpmnEditorState current) =>
        !ReferenceEquals(previous.Overlays.AccessibleTree, current.Overlays.AccessibleTree);

    private readonly Dictionary<string, ElementReference> _items = new(StringComparer.Ordinal);

    /// <summary>The element whose item should take focus on the next render, if any.</summary>
    private string? _pendingFocus;

    /// <summary>What the tree says when the diagram is empty; a localized default when not set.</summary>
    [Parameter]
    public string? EmptyMessage { get; set; }

    /// <inheritdoc />
    protected override BpmnAccessibleTreeState? WidgetState => State.Overlays.AccessibleTree;

    /// <inheritdoc />
    protected override bool IsOpen => WidgetState?.IsOpen == true;

    /// <inheritdoc />
    protected override Anchor? Anchor => null;

    /// <inheritdoc />
    protected override bool FocusOnOpen => false;

    /// <inheritdoc />
    protected override string DefaultAriaLabel => WidgetState?.AriaLabel is { Length: > 0 } label
        ? label
        : Text("AccessibleTree.AriaLabel");

    /// <summary>The diagram, in reading order.</summary>
    protected IReadOnlyList<BpmnGraphNode> Items => WidgetState?.Nodes ?? [];

    /// <summary>The element the reader is on.</summary>
    protected string? Here => WidgetState?.Current;

    /// <summary>The empty-state text in force.</summary>
    protected string EffectiveEmptyMessage => string.IsNullOrWhiteSpace(EmptyMessage)
        ? Text("AccessibleTree.Empty")
        : EmptyMessage;

    /// <summary>The rendered items by element id; capture each with <c>@ref="ItemRefs[id]"</c> so focus can be moved to it.</summary>
    protected IDictionary<string, ElementReference> ItemRefs => _items;

    /// <summary>
    /// The <c>aria-expanded</c> value for an item, or <see langword="null"/> to leave it off.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> rather than "false" for anything that cannot be expanded, because
    /// Blazor omits an attribute whose value is null and a Task announced as collapsed would tell
    /// a reader it is hiding something.
    /// </remarks>
    /// <param name="node">The element the item stands for.</param>
    /// <returns>"true", "false", or <see langword="null"/> for anything that cannot be expanded.</returns>
    protected static string? Expanded(BpmnGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.IsExpanded switch
        {
            true => "true",
            false => "false",
            null => null,
        };
    }

    /// <summary>Whether an item is the one reachable by Tab (roving tabindex).</summary>
    /// <param name="node">The element the item stands for.</param>
    /// <returns><see langword="true"/> for the current item, or the first when there is none.</returns>
    protected bool IsTabStop(BpmnGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.ElementId == Here || (Here is null && Items.Count > 0 && ReferenceEquals(node, Items[0]));
    }

    /// <summary>The DOM id of an element's tree item, so a caller can point at it.</summary>
    /// <param name="elementId">The element.</param>
    /// <returns>The id.</returns>
    public static string ItemId(string elementId) => $"bpmn-a11y-{elementId}";

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingFocus is not { } id) return;

        _pendingFocus = null;

        /*
         * Focus is moved here rather than in the key handler because the item that should take it
         * may not have been rendered yet. A missing reference is a race, not a fault: the reader
         * pressed a key while the diagram was being replaced, and doing nothing is right.
         */
        if (_items.TryGetValue(id, out var item))
        {
            try
            {
                await item.FocusAsync().ConfigureAwait(true);
            }
            catch (InvalidOperationException)
            {
                // The element went away between the render and this call.
            }
        }
    }

    /// <summary>Tells the editor an item took focus.</summary>
    /// <param name="elementId">The element the item stands for.</param>
    /// <returns>A task that completes once the editor has moved.</returns>
    protected async Task FocusAsync(string elementId)
    {
        if (elementId == Here) return;

        await Session.GoToAccessibleAsync(elementId).ConfigureAwait(true);
    }

    /// <summary>
    /// Binds keys to directions, and nothing else.
    /// </summary>
    /// <remarks>
    /// Left and right walk the flow; up and down choose between the branches out of a fork. Home
    /// and End are the ends of the diagram. Enter activates, which is how a reader edits a label
    /// without a pointer. Every other key is left alone, Tab above all: a tree that swallowed Tab
    /// would be a trap.
    /// </remarks>
    /// <param name="args">The key event.</param>
    /// <returns>A task that completes once the key has been handled.</returns>
    protected override async Task HandleKeyDownAsync(KeyboardEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Key == "Enter")
        {
            if (Here is { } activating) await Session.ActivateAccessibleAsync(activating).ConfigureAwait(true);
            return;
        }

        /*
         * Plus and minus expand and collapse a Sub-Process. Not Left and Right, which already walk
         * the Sequence Flow, and not Enter, which edits the label and must stay consistent with
         * what double-clicking a collapsed Sub-Process does.
         */
        if (args.Key is "+" or "-" or "Add" or "Subtract")
        {
            var wanted = args.Key is "+" or "Add";

            if (Here is { } toggling
                && Items.FirstOrDefault(node => node.ElementId == toggling) is { IsExpanded: { } already }
                && already != wanted)
            {
                await Session.ToggleExpansionAsync(toggling).ConfigureAwait(true);
            }

            return;
        }

        var direction = args.Key switch
        {
            "ArrowRight" => BpmnGraphDirection.Forward,
            "ArrowLeft" => BpmnGraphDirection.Back,
            "ArrowDown" => BpmnGraphDirection.Next,
            "ArrowUp" => BpmnGraphDirection.Previous,
            "Home" => BpmnGraphDirection.First,
            "End" => BpmnGraphDirection.Last,
            _ => (BpmnGraphDirection?)null,
        };

        if (direction is not { } move) return;

        if (BpmnGraphNavigation.Move(Items, Here, move) is not { } target) return;

        _pendingFocus = target;
        await Session.GoToAccessibleAsync(target).ConfigureAwait(true);
    }
}
