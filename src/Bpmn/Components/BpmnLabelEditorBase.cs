using System.Globalization;

using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Microsoft.AspNetCore.Components.Web;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// The logic of inline label editing (specification section 4.5); a UI kit supplies the markup.
/// </summary>
/// <remarks>
/// <para>
/// Section 4.5 permits no popup, dialog or modal, and asks the editor to match the canvas's own
/// text metrics. The box is positioned at the label's client rectangle, drawn with the font the
/// canvas reported and scaled by the paper's scale. Browser-side behavior keeps the box aligned
/// as the viewport moves and resizes the input as its content changes.
/// </para>
/// <para>
/// Enter commits and Shift+Enter inserts a line break, because a BPMN label legitimately contains
/// them. Escape abandons the edit; blur commits it, since a user who clicks away has finished
/// typing rather than changed their mind.
/// </para>
/// </remarks>
public abstract class BpmnLabelEditorBase : BpmnCanvasWidgetBase<BpmnLabelEditState>
{
    /// <inheritdoc />
    protected override bool ShouldRenderOnStateChange(BpmnEditorState previous, BpmnEditorState current) =>
        !ReferenceEquals(previous.Overlays.LabelEdit, current.Overlays.LabelEdit);

    private const double MinimumWidth = 80d;

    private string? _openedFor;

    /// <inheritdoc />
    protected override BpmnLabelEditState? WidgetState => State.Overlays.LabelEdit;

    /// <inheritdoc />
    protected override bool IsOpen => WidgetState is { IsOpen: true, Request: not null };

    /// <inheritdoc />
    protected override Anchor? Anchor => Edit?.Anchor;

    /// <inheritdoc />
    protected override string DefaultAriaLabel => Text("LabelEditor.AriaLabel");

    /// <summary>What the canvas reported: where the label is, what it says and how it is drawn.</summary>
    protected LabelEditRequested? Edit => WidgetState?.Request;

    /// <summary>The text being edited; bind it to the input.</summary>
    protected string EditText { get; set; } = string.Empty;

    /// <summary>
    /// The box, placed and sized in client pixels, centred on the label's own centre so a widened
    /// box grows evenly and the text does not jump sideways as editing starts. This is the initial
    /// placement; the canvas keeps it current while editing without a Blazor round trip.
    /// </summary>
    protected string BoxStyle
    {
        get
        {
            if (Edit is null) return string.Empty;

            var client = Edit.Anchor.Client;
            var width = Math.Max(client.Width, MinimumWidth * Edit.Scale);
            var height = Math.Max(client.Height, Edit.LineHeight * Edit.Scale);

            var x = client.X + (client.Width / 2) - (width / 2);
            var y = client.Y + (client.Height / 2) - (height / 2);

            return $"left:{Px(x)};top:{Px(y)};width:{Px(width)};height:{Px(height)}";
        }
    }

    /// <summary>The canvas's own text metrics, scaled to what is on screen.</summary>
    protected string TextStyle => Edit is null
        ? string.Empty
        : string.Create(
            CultureInfo.InvariantCulture,
            $"font-family:{Edit.FontFamily};font-size:{Edit.FontSize * Edit.Scale:0.##}px;"
            + $"line-height:{Edit.LineHeight * Edit.Scale:0.##}px;text-align:{Alignment}");

    /// <summary>An unrecognised alignment falls back to centred, which is BPMN's usual case.</summary>
    protected string Alignment => Edit?.TextAlign is "left" or "right" ? Edit.TextAlign : "center";

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        if (!IsOpen || Edit is null)
        {
            _openedFor = null;
            return;
        }

        // Seeded once per opening. Re-seeding on every render would overwrite what the user is
        // typing each time the parent re-renders for an unrelated reason.
        if (_openedFor == Edit.ElementId) return;

        _openedFor = Edit.ElementId;
        EditText = Edit.Text;
    }

    /// <summary>Commits the text being edited.</summary>
    /// <returns>A task that completes once the edit is committed.</returns>
    protected Task CommitAsync() => IsOpen ? Session.CommitLabelEditAsync(EditText) : Task.CompletedTask;

    /// <summary>Abandons the edit.</summary>
    /// <returns>A completed task.</returns>
    protected override Task DismissAsync()
    {
        Session.CancelLabelEdit();
        return Task.CompletedTask;
    }

    /// <summary>Enter commits, Shift+Enter is a line break, Escape cancels.</summary>
    /// <param name="args">The key event.</param>
    /// <returns>A task that completes once the key has been handled.</returns>
    protected override Task HandleKeyDownAsync(KeyboardEventArgs args) =>
        args.Key == "Enter" && !args.ShiftKey ? CommitAsync() : base.HandleKeyDownAsync(args);
}
