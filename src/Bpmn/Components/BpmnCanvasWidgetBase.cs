using System.Globalization;

using Hasseware.AspNetCore.Components.Bpmn.Interop;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// The logic-only base of a widget drawn over the canvas — context menu, contextual actions,
/// label editor, accessible tree — positioned against the canvas host.
/// </summary>
/// <typeparam name="TState">The overlay slice of <see cref="BpmnEditorState"/> the widget shows.</typeparam>
/// <remarks>
/// Besides the editor it requires the <see cref="BpmnCanvasSurface"/> the canvas cascades, because
/// the coordinates a widget is given mean nothing anywhere else.
/// </remarks>
public abstract class BpmnCanvasWidgetBase<TState> : BpmnEditorComponentBase
    where TState : class
{
    private Anchor? _styleAnchor;
    private double _styleLeft;
    private double _styleTop;
    private string? _anchorStyle;
    private bool _wasOpen;
    private bool _focusPending;

    [CascadingParameter]
    private BpmnCanvasSurface? CascadedSurface { get; set; }

    /// <summary>The widget's accessible name; a localized default when not set.</summary>
    [Parameter]
    public string? AriaLabel { get; set; }

    /// <summary>The element focus moves to when the widget opens.</summary>
    protected ElementReference FocusTarget { get; set; }

    /// <summary>The canvas the widget is drawn inside.</summary>
    protected BpmnCanvasSurface Surface => CascadedSurface ?? throw MissingCanvas();

    /// <summary>The slice of <see cref="BpmnEditorComponentBase.State"/> this widget shows.</summary>
    protected abstract TState? WidgetState { get; }

    /// <summary>Whether the widget is open.</summary>
    protected abstract bool IsOpen { get; }

    /// <summary>Where to place the widget, or <see langword="null"/> when it has no anchor.</summary>
    protected abstract Anchor? Anchor { get; }

    /// <summary>The localized default for <see cref="AriaLabel"/>.</summary>
    protected abstract string DefaultAriaLabel { get; }

    /// <summary>The accessible name in force: the parameter, or the localized default.</summary>
    protected string EffectiveAriaLabel => string.IsNullOrWhiteSpace(AriaLabel) ? DefaultAriaLabel : AriaLabel;

    /// <summary>Whether focus moves to <see cref="FocusTarget"/> each time the widget opens.</summary>
    protected virtual bool FocusOnOpen => true;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        if (CascadedSurface is null) throw MissingCanvas();

        var open = IsOpen;

        // Once per opening: refocusing on every render would take focus from whatever the user
        // moved to inside the widget.
        if (open && !_wasOpen && FocusOnOpen) _focusPending = true;

        _wasOpen = open;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_focusPending || !IsOpen) return;

        _focusPending = false;
        // Scrolling the host would invalidate the client-space anchors of its overlays.
        await FocusTarget.FocusAsync(preventScroll: true).ConfigureAwait(true);
    }

    /// <summary>Closes the widget; what "close" means is the widget's own.</summary>
    /// <returns>A task that completes once the widget has asked to close.</returns>
    protected virtual Task DismissAsync() => Task.CompletedTask;

    /// <summary>Escape dismisses; derived widgets add their own keys.</summary>
    /// <param name="args">The key event.</param>
    /// <returns>A task that completes once the key has been handled.</returns>
    protected virtual Task HandleKeyDownAsync(KeyboardEventArgs args) =>
        args.Key == "Escape" ? DismissAsync() : Task.CompletedTask;

    /// <summary>
    /// A pixel value in the invariant culture, so a comma-decimal locale cannot corrupt a style.
    /// </summary>
    /// <param name="value">The length.</param>
    /// <returns>The value formatted to two decimals at most.</returns>
    protected static string Px(double value) => string.Create(CultureInfo.InvariantCulture, $"{value:0.##}px");

    /// <summary>
    /// Absolute placement from the anchor's rectangle.
    /// </summary>
    /// <param name="left">Selects the horizontal offset from the rectangle.</param>
    /// <param name="top">Selects the vertical offset from the rectangle.</param>
    /// <returns>An inline style, or the origin when there is no anchor.</returns>
    protected string AnchorStyle(Func<Model.Di.Bounds, double> left, Func<Model.Di.Bounds, double> top)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(top);

        var anchorValue = Anchor;
        var x = anchorValue is { } a ? left(a.Client) : 0;
        var y = anchorValue is { } b ? top(b.Client) : 0;
        if (_anchorStyle is not null && Equals(_styleAnchor, anchorValue) && _styleLeft == x && _styleTop == y)
            return _anchorStyle;
        _styleAnchor = anchorValue;
        _styleLeft = x;
        _styleTop = y;
        return _anchorStyle = anchorValue is not null
            ? $"left:{Px(x)};top:{Px(y)}" : "left:0;top:0";
    }

    private InvalidOperationException MissingCanvas() => new(
        $"{ComponentName} must be rendered inside a BpmnCanvas.");
}
