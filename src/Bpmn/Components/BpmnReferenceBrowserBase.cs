using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// The logic of find-usages: every place the document names the selected element (implementation
/// plan M6); a UI kit supplies the markup.
/// </summary>
/// <remarks>
/// It lists what is there and navigates; it never edits. The question it answers is the one to ask
/// before renaming or deleting anything: what breaks.
/// </remarks>
public abstract class BpmnReferenceBrowserBase : BpmnEditorComponentBase
{
    /// <inheritdoc />
    protected override bool ShouldRenderOnStateChange(BpmnEditorState previous, BpmnEditorState current) =>
        previous.Selection != current.Selection
            || !ReferenceEquals(previous.Document, current.Document);

    /// <summary>The section title; a localized default when not set.</summary>
    [Parameter]
    public string? Heading { get; set; }

    /// <summary>The title in force.</summary>
    protected string EffectiveHeading => string.IsNullOrWhiteSpace(Heading) ? Text("ReferenceBrowser.Heading") : Heading;

    /// <summary>The places the selected element is named.</summary>
    protected IReadOnlyList<BpmnReferenceSite> Usages => Session.SelectionUsages;

    /// <summary>The localized kind and invariant owner id of a reference.</summary>
    protected string TitleOf(BpmnReferenceSite usage) => Text("ReferenceBrowser.OwnerTitle",
        Display.BuiltIn($"ReferenceKind.{usage.OwnerKind.Replace(" ", string.Empty, StringComparison.Ordinal)}", usage.OwnerKind),
        usage.OwnerId);

    /// <summary>Selects and reveals the owner of a usage.</summary>
    /// <param name="usage">The usage chosen.</param>
    /// <returns>A task that completes once the owner is selected.</returns>
    protected Task SelectAsync(BpmnReferenceSite usage) => Session.GoToUsageAsync(usage);
}
