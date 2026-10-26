using Hasseware.AspNetCore.Components.Bpmn.Configuration;

using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>One action and every key combination bound to it.</summary>
/// <param name="Description">What the action does.</param>
/// <param name="Keys">The display text of each binding.</param>
public sealed record BpmnShortcutGroup(string Description, IReadOnlyList<string> Keys);

/// <summary>
/// The logic of the keyboard-shortcut overlay; a UI kit supplies the markup.
/// </summary>
/// <remarks>
/// Rendered from the same <see cref="KeyboardOptions"/> the canvas was configured with, so it can
/// never describe a binding the canvas does not have.
/// </remarks>
public abstract class BpmnShortcutHelpBase : BpmnEditorComponentBase
{
    /// <inheritdoc />
    protected override bool ShouldRenderOnStateChange(BpmnEditorState previous, BpmnEditorState current) =>
        previous.ShortcutHelpVisible != current.ShortcutHelpVisible
            || !ReferenceEquals(previous.Configuration, current.Configuration);

    /// <summary>A stable id prefix for the overlay's labelled-by relationships.</summary>
    protected string Id { get; } = $"bpmn-shortcuts-{Guid.NewGuid():N}";

    /// <summary>Heading text; a localized default when not set.</summary>
    [Parameter]
    public string? Title { get; set; }

    /// <summary>The heading in force.</summary>
    protected string EffectiveTitle => string.IsNullOrWhiteSpace(Title) ? Text("ShortcutHelp.Title") : Title;

    /// <summary>The localized text for an empty binding list.</summary>
    protected string EmptyMessage => Text("ShortcutHelp.Empty");

    /// <summary>The localized text between two alternative keys.</summary>
    protected string OrText => Text("ShortcutHelp.Or");

    /// <summary>The localized label of the close button.</summary>
    protected string CloseText => Text("ShortcutHelp.Close");

    /// <summary>Whether the overlay is shown.</summary>
    protected bool IsVisible => State.ShortcutHelpVisible;

    /// <summary>
    /// One row per action, with every key combination bound to it.
    /// </summary>
    /// <remarks>
    /// Grouped by description rather than listed per binding, because the defaults bind both
    /// <c>+</c> and <c>=</c> to zoom in and two rows saying "Zoom in" reads like a mistake.
    /// Bindings with no description are skipped: inventing a label would be guessing at the host's
    /// intent.
    /// </remarks>
    protected IReadOnlyList<BpmnShortcutGroup> Groups =>
        [.. State.Configuration.Keyboard.Bindings
            .Where(binding => !string.IsNullOrWhiteSpace(binding.Description))
            .GroupBy(binding => binding.Description, StringComparer.Ordinal)
            .Select(group => new BpmnShortcutGroup(
                Display.BuiltIn($"Shortcut.{group.First().Command}", group.Key),
                [.. group.Select(KeysOf)]))];

    private string KeysOf(ShortcutBinding binding)
    {
        var keys = new List<string>();
        if (binding.Ctrl) keys.Add(Text("ShortcutKey.Ctrl"));
        if (binding.Meta) keys.Add(Text("ShortcutKey.Cmd"));
        if (binding.Alt) keys.Add(Text("ShortcutKey.Alt"));
        if (binding.Shift) keys.Add(Text("ShortcutKey.Shift"));
        keys.Add(binding.Key.Length == 1 ? binding.Key.ToUpperInvariant() : TextOr($"ShortcutKey.{binding.Key}", binding.Key));
        return string.Join(" + ", keys);
    }

    /// <summary>Closes the overlay.</summary>
    /// <returns>A completed task.</returns>
    protected Task CloseAsync()
    {
        Session.HideShortcutHelp();
        return Task.CompletedTask;
    }

}
