using System.Globalization;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// The logic of the zoom, history, color and help controls; a UI kit supplies the controls.
/// </summary>
/// <remarks>
/// Commands go to the <see cref="BpmnEditorSession"/> and the readouts come from
/// <see cref="BpmnEditorState"/>, so the toolbar needs no host wiring and tracks wheel and drag
/// zoom as well as button presses.
/// </remarks>
public abstract class BpmnToolbarBase : BpmnEditorComponentBase
{
    /// <inheritdoc />
    protected override bool ShouldRenderOnStateChange(BpmnEditorState previous, BpmnEditorState current) =>
        ChildContent is not null || previous.Zoom != current.Zoom || previous.IsReadOnly != current.IsReadOnly
            || previous.CanUndo != current.CanUndo || previous.CanRedo != current.CanRedo
            || previous.Document != current.Document || previous.MessageCatalog != current.MessageCatalog
            || previous.MessageFocusOrigin != current.MessageFocusOrigin
            || ShowColorPicker && (previous.Document != current.Document
                || previous.ActiveDiagramId != current.ActiveDiagramId || previous.Selection != current.Selection);

    /// <summary>Whether selected-item color controls are shown. Hidden by default.</summary>
    [Parameter]
    public bool ShowColorPicker { get; set; }

    /// <summary>Whether zoom controls, zoom readout, reset and fit are shown. Shown by default.</summary>
    [Parameter]
    public bool ShowZoom { get; set; } = true;

    /// <summary>Whether undo and redo controls are shown. Shown by default.</summary>
    [Parameter]
    public bool ShowUndoRedo { get; set; } = true;

    /// <summary>Whether the keyboard shortcut help button is shown. Shown by default.</summary>
    [Parameter]
    public bool ShowShortcuts { get; set; } = true;

    /// <summary>Whether the message catalog button is shown. Shown by default.</summary>
    [Parameter]
    public bool ShowMessages { get; set; } = true;

    /// <summary>The independently editable color channels.</summary>
    protected IReadOnlyList<BpmnColorChannel> ColorChannels { get; } =
        [BpmnColorChannel.Background, BpmnColorChannel.Border, BpmnColorChannel.Text];

    /// <summary>The latest rejected color edit, if any.</summary>
    protected string? ColorError { get; private set; }

    private BpmnPlane? ColorPlane => State.Document?.Diagrams
        .FirstOrDefault(diagram => diagram.Id == State.ActiveDiagramId)?.Plane
        ?? (State.ActiveDiagramId is null && State.Document is { Diagrams.Count: > 0 } document
            ? document.Diagrams[0].Plane : null);

    private IEnumerable<string?> SelectedColors(BpmnColorChannel channel)
    {
        var plane = ColorPlane;

        if (plane is null)
            yield break;

        foreach (var shape in plane.Shapes.Where(shape => State.Selection.Contains(shape.BpmnElement)))
        {
            yield return channel switch
            {
                BpmnColorChannel.Background => shape.BackgroundColor,
                BpmnColorChannel.Border => shape.BorderColor,
                _ => shape.Label?.Color,
            };
        }

        if (channel == BpmnColorChannel.Background)
            yield break;

        foreach (var edge in plane.Edges.Where(edge => State.Selection.Contains(edge.BpmnElement)))
            yield return channel == BpmnColorChannel.Border ? edge.BorderColor : edge.Label?.Color;
    }

    /// <summary>Whether the selected DI items support this color channel.</summary>
    protected bool CanColor(BpmnColorChannel channel) => !State.IsReadOnly && SelectedColors(channel).Any();

    /// <summary>The common selected override, or null for mixed/default values.</summary>
    protected string? ColorValue(BpmnColorChannel channel)
    {
        var values = SelectedColors(channel).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    /// <summary>The localized channel name.</summary>
    protected string ColorLabel(BpmnColorChannel channel) => Text($"Toolbar.Color.{channel}");

    /// <summary>Distinguishes absent overrides from a mixed selection.</summary>
    protected string ColorStatus(BpmnColorChannel channel) =>
        SelectedColors(channel).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).Count() > 1
            ? Text("Toolbar.Color.Mixed") : ColorValue(channel) ?? Text("Toolbar.Color.Default");

    /// <summary>Commits one color gesture as one history entry.</summary>
    protected async Task SetColorAsync(BpmnColorChannel channel, string? value)
    {
        var diagram = State.Document?.Diagrams.FirstOrDefault(item => item.Plane == ColorPlane);

        if (State.IsReadOnly || diagram is null || State.Selection.IsEmpty)
            return;

        var result = await Session.ApplyAsync(new SetColorsCommand(diagram.Id, State.Selection, channel, value)).ConfigureAwait(true);
        var rejections = result.Rejections.Select(item => TextOr($"Toolbar.Color.Error.{item.Code}", item.Message)).ToArray();
        ColorError = rejections.Length == 0 ? null : string.Join(" ", rejections);
    }

    /// <summary>Accessible name of the toolbar; a localized default when not set.</summary>
    [Parameter]
    public string? AriaLabel { get; set; }

    /// <summary>Extra controls appended to the toolbar.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>The accessible name in force.</summary>
    protected string EffectiveAriaLabel => string.IsNullOrWhiteSpace(AriaLabel) ? Text("Toolbar.AriaLabel") : AriaLabel;

    /// <summary>The zoom readout, such as <c>100%</c>.</summary>
    protected string ZoomLabel => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(State.Zoom * 100d):0}%");

    /// <summary>Whether there is an edit to undo.</summary>
    protected bool CanUndo => !State.IsReadOnly && State.CanUndo;

    /// <summary>Whether there is an edit to redo.</summary>
    protected bool CanRedo => !State.IsReadOnly && State.CanRedo;

    /// <summary>Requests a viewport change.</summary>
    /// <param name="command">The change.</param>
    /// <returns>A task that completes once the canvas has acted.</returns>
    protected Task ExecuteAsync(ViewportCommand command) => Session.SetViewportAsync(command).AsTask();

    /// <summary>Undoes the last edit.</summary>
    /// <returns>A task that completes once the edit is undone.</returns>
    protected Task UndoAsync() => Session.UndoAsync();

    /// <summary>Redoes the last undone edit.</summary>
    /// <returns>A task that completes once the edit is redone.</returns>
    protected Task RedoAsync() => Session.RedoAsync();

    /// <summary>Opens the keyboard shortcut help.</summary>
    protected void ShowShortcutHelp() => Session.ShowShortcutHelp();

    /// <summary>The document catalog's invoking control.</summary>
    protected ElementReference MessagesButton { get; set; }

    /// <summary>Whether a document catalog can be opened.</summary>
    protected bool CanManageMessages => State.Document is not null && Session.HasMessageCatalog;

    /// <summary>Opens the document-level Message catalog.</summary>
    protected void OpenMessageCatalog() => Session.ShowMessages();

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Session.TakeMessageFocus("toolbar") && ShowMessages)
            await MessagesButton.FocusAsync(preventScroll: true).ConfigureAwait(true);
    }
}
