using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;

using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// The logic of the element-template row of the property panel; a UI kit supplies the markup.
/// </summary>
public abstract class BpmnTemplateChooserBase : BpmnEditorComponentBase
{
    private string? _elementId;

    /// <summary>Single selected element and its persisted template identity.</summary>
    [Parameter, EditorRequired]
    public BpmnElementSnapshot Element { get; set; } = default!;

    /// <summary>Whether the chooser list is open.</summary>
    protected bool IsOpen { get; private set; }

    /// <summary>The current search text.</summary>
    protected string Search { get; private set; } = string.Empty;

    /// <summary>Whether template changes are disabled while inspection stays possible.</summary>
    protected bool Locked => State.IsReadOnly;

    /// <summary>Registry, engine versions and template options.</summary>
    protected BpmnTemplateOptions Options => Session.Templates;

    /// <summary>Templates that may be applied to the element.</summary>
    protected IReadOnlyList<BpmnElementTemplate> Applicable => BpmnTemplateSelection.Applicable(Element, Options);

    /// <summary>The template the element carries, when the registry has it.</summary>
    protected BpmnElementTemplate? Applied => Element.TemplateId is { } id ? Options.Registry.TryGetExact(id, Element.TemplateVersion) : null;

    /// <summary>The newer version of the applied template, if any.</summary>
    protected BpmnElementTemplate? Upgrade => Applied is { } template ? BpmnTemplateSelection.Upgrade(Element, template, Options) : null;

    /// <summary>Applicable templates matching the search text.</summary>
    protected IEnumerable<BpmnElementTemplate> Filtered => Applicable.Where(template =>
        string.Join(' ', template.Name, template.Description, template.Category?.Name, string.Join(' ', template.Keywords))
            .Contains(Search, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether the row has anything to show.</summary>
    protected bool HasContent => Element.TemplateId is not null || Applicable.Count > 0;

    /// <summary>Category heading for a template.</summary>
    /// <param name="template">The template.</param>
    /// <returns>The category name, or a localized "Other".</returns>
    protected string CategoryOf(BpmnElementTemplate template) => template.Category?.Name ?? Text("TemplateChooser.Other");

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        if (_elementId != Element.ElementId || Element.TemplateId is not null || Locked)
        {
            IsOpen = false;
            Search = string.Empty;
        }

        _elementId = Element.ElementId;
    }

    /// <summary>Opens or closes the list.</summary>
    protected void Toggle()
    {
        if (!Locked) IsOpen = !IsOpen;
    }

    /// <summary>Sets the search text.</summary>
    /// <param name="args">The input event.</param>
    protected void SetSearch(ChangeEventArgs args) => Search = args.Value?.ToString() ?? string.Empty;

    /// <summary>Applies a template.</summary>
    /// <param name="template">The template.</param>
    /// <returns>A task that completes once the command is applied.</returns>
    protected Task ApplyAsync(BpmnElementTemplate template) =>
        SubmitAsync(new ApplyElementTemplateCommand(Element.ElementId, template, Options.Registry));

    /// <summary>Unlinks the applied template, keeping its properties.</summary>
    /// <returns>A task that completes once the command is applied.</returns>
    protected Task UnlinkAsync() => SubmitAsync(new UnlinkElementTemplateCommand(Element.ElementId));

    /// <summary>Removes the applied template and what it contributed.</summary>
    /// <returns>A task that completes once the command is applied.</returns>
    protected Task RemoveAsync() => Applied is { } template
        ? SubmitAsync(new RemoveElementTemplateCommand(Element.ElementId, template))
        : Task.CompletedTask;

    /// <summary>Upgrades the applied template to its newer version.</summary>
    /// <returns>A task that completes once the command is applied.</returns>
    protected Task UpgradeAsync() => Applied is { } old && Upgrade is { } next
        ? SubmitAsync(new UpgradeElementTemplateCommand(Element.ElementId, old, next))
        : Task.CompletedTask;

    private async Task SubmitAsync(BpmnEditCommand command)
    {
        if (Locked) return;
        IsOpen = false;
        await Session.ApplyPropertyCommandAsync(command);
    }
}
