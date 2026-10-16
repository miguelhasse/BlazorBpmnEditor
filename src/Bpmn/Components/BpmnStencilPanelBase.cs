using Hasseware.AspNetCore.Components.Bpmn.Editing;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// The logic of the element palette; a UI kit supplies the markup, and draws entries with
/// <see cref="BpmnStencilPresentation"/>.
/// </summary>
/// <remarks>
/// <para>
/// Entries come from <see cref="BpmnStencil"/>, which computes them from the active container
/// rather than showing one fixed palette: a stencil that offered a creation the editor would then
/// refuse is worse than one that shows it greyed out with a reason.
/// </para>
/// <para>
/// It holds no canvas reference. A tool is armed on the session and the canvas turns the drop into
/// a command, so nothing but <c>BpmnCanvas</c> talks to JavaScript.
/// </para>
/// </remarks>
public abstract class BpmnStencilPanelBase : BpmnEditorComponentBase
{
    /// <summary>Accessible name of the palette; a localized default when not set.</summary>
    [Parameter]
    public string? AriaLabel { get; set; }

    /// <summary>Shown when the active container permits no creations at all; a localized default when not set.</summary>
    [Parameter]
    public string? EmptyMessage { get; set; }

    /// <summary>The search text of the template list; bind it to the search box.</summary>
    protected string TemplateSearch { get; set; } = string.Empty;

    /// <summary>The accessible name in force.</summary>
    protected string EffectiveAriaLabel => string.IsNullOrWhiteSpace(AriaLabel) ? Text("StencilPanel.AriaLabel") : AriaLabel;

    /// <summary>The empty-state text in force.</summary>
    protected string EffectiveEmptyMessage => string.IsNullOrWhiteSpace(EmptyMessage) ? Text("StencilPanel.Empty") : EmptyMessage;

    /// <summary>The groups to show, curated for the active container.</summary>
    protected IReadOnlyList<BpmnStencilGroup> Groups => Session.StencilGroups;

    /// <summary>The template groups, enabled against the container's uncurated rules.</summary>
    protected IReadOnlyList<BpmnStencilGroup> TemplateGroups =>
        BpmnTemplateStencil.GroupsFor(Session.Templates, Session.RuleStencilGroups);

    /// <summary>The tool being dragged, so the palette shows which one is in flight.</summary>
    protected BpmnStencilEntry? Armed => State.ArmedTool;

    /// <summary>The templates of a group that match <see cref="TemplateSearch"/>.</summary>
    /// <param name="group">The template group.</param>
    /// <returns>The matching entries.</returns>
    protected IEnumerable<BpmnStencilEntry> MatchingTemplates(BpmnStencilGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        return group.Entries.Where(entry => string.IsNullOrWhiteSpace(TemplateSearch)
            || entry.Label.Contains(TemplateSearch, StringComparison.OrdinalIgnoreCase)
            || (entry.Description?.Contains(TemplateSearch, StringComparison.OrdinalIgnoreCase) ?? false)
            || group.Title.Contains(TemplateSearch, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The tooltip for a tool, saying why it is unavailable when it is.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The tooltip.</returns>
    protected string TitleOf(BpmnStencilEntry entry) => entry.IsEnabled
        ? entry.Description ?? Display.StencilEntry(entry)
        : Text("StencilPanel.Unavailable", entry.Description ?? Display.StencilEntry(entry),
            DisabledReasonOf(entry));

    private string DisabledReasonOf(BpmnStencilEntry entry)
    {
        if (entry.DisabledReason is not { } reason) return string.Empty;
        var translated = L[$"StencilReason.{reason}"];
        return translated.ResourceNotFound ? reason : Text("StencilReason.WithCode", reason, translated.Value);
    }

    /// <summary>The state modifier for a tool: unavailable, armed for a drop, or neither.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>A CSS modifier class, or <see langword="null"/>.</returns>
    protected string? ModifierFor(BpmnStencilEntry entry) => entry switch
    {
        { IsEnabled: false } => "bpmn-stencil__tool--disabled",
        _ when ReferenceEquals(entry, Armed) || (entry.Template is { } template
            && Armed?.Template is { } armed && template.Id == armed.Id && template.Version == armed.Version) => "bpmn-stencil__tool--armed",
        _ => null,
    };

    /// <summary>Arms a tool for a drop, by starting a drag.</summary>
    /// <param name="entry">The tool.</param>
    /// <returns>A completed task.</returns>
    protected Task BeginDragAsync(BpmnStencilEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.IsEnabled) Session.ArmTool(entry);
        return Task.CompletedTask;
    }

    /// <summary>Disarms the tool when a drag ends, whether or not it landed anywhere.</summary>
    /// <returns>A completed task.</returns>
    protected Task EndDragAsync()
    {
        Session.DisarmTool();
        return Task.CompletedTask;
    }

    /// <summary>Activates a tool without a drop to follow; the editor places it.</summary>
    /// <param name="entry">The tool.</param>
    /// <returns>A task that completes once the tool is placed.</returns>
    protected Task ActivateAsync(BpmnStencilEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.IsEnabled ? Session.ActivateToolAsync(entry) : Task.CompletedTask;
    }

    /// <summary>Enter and Space activate the focused tool; a palette that could only be dragged would be unusable without a pointer.</summary>
    /// <param name="args">The key event.</param>
    /// <param name="entry">The tool.</param>
    /// <returns>A task that completes once the key has been handled.</returns>
    protected Task HandleKeyDownAsync(KeyboardEventArgs args, BpmnStencilEntry entry)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Key is "Enter" or " " ? ActivateAsync(entry) : Task.CompletedTask;
    }
}
