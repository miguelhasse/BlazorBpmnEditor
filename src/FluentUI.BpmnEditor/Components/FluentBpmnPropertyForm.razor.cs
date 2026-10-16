using Hasseware.AspNetCore.Components.Bpmn;
using Hasseware.AspNetCore.Components.Bpmn.Editing;

using Microsoft.AspNetCore.Components;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn;

/// <summary>
/// The controls for a list of property descriptors.
/// </summary>
/// <remarks>
/// <para>
/// Extracted so the context menu and the property panel cannot drift. They ask different questions
/// — a menu edits the element under the pointer, a panel edits the selection — but a Boolean has to
/// look and behave the same in both, and an editor where the same property behaves differently
/// depending on which surface you reached it through is worse than one with a single surface.
/// </para>
/// <para>
/// It renders descriptors and nothing else. It never sees the document or a JointJS cell, so it
/// cannot show a value the model did not put in the snapshot (section 7).
/// </para>
/// </remarks>
public sealed partial class FluentBpmnPropertyForm : ComponentBase
{
    [Inject]
    private BpmnDisplayLocalizer Display { get; set; } = default!;

    /// <summary>The properties to render, in the order the descriptor list gives them.</summary>
    [Parameter]
    public IReadOnlyList<BpmnPropertyDescriptor> Properties { get; set; } = [];

    /// <summary>
    /// How many edits the owner has submitted, included in every control's key.
    /// </summary>
    /// <remarks>
    /// The owner increments it, because the owner is what knows whether a submission completed.
    /// </remarks>
    [Parameter]
    public int Revision { get; set; }

    /// <summary>Raised when a control's value changes.</summary>
    [Parameter]
    public EventCallback<BpmnPropertyEdit> OnEdit { get; set; }

    /// <summary>The accessible name of the group the controls sit in.</summary>
    [Parameter]
    public string? Label { get; set; }

    [Inject]
    private Microsoft.Extensions.Localization.IStringLocalizer<Hasseware.AspNetCore.Components.Bpmn.Resources.BpmnStrings> L { get; set; } = default!;

    private string EffectiveLabel => Label ?? L["PropertyForm.AriaLabel"];

    /// <summary>Rows a multiline text box is given.</summary>
    [Parameter]
    public int MultilineRows { get; set; } = 2;

    /// <summary>
    /// The block the surrounding surface styles its controls with, such as <c>bpmn-menu</c>.
    /// </summary>
    /// <remarks>
    /// The form carries no styling of its own: a menu's controls are dense and a panel's are not,
    /// and that is a decision belonging to the surface rather than to the fields.
    /// </remarks>
    [Parameter]
    public string Block { get; set; } = "bpmn-menu";

    private string CssClass => $"{Block}__properties";

    private BpmnPropertyEntryContext BuildContext(BpmnPropertyDescriptor property) => new(
        Display.Property(BpmnGeneralPropertiesProvider.FromDescriptor(property)),
        EventCallback.Factory.Create<string?>(this, value => SubmitAsync(property, value)),
        default, property.IsReadOnly, Revision);

    private Task SubmitAsync(BpmnPropertyDescriptor property, string? value)
    {
        if (property.IsReadOnly) return Task.CompletedTask;

        // An empty text box means the attribute is absent, not present and empty. A choice keeps
        // its empty value, because that is the "(none)" option the descriptor declared.
        var submitted = property.Kind is BpmnPropertyKind.Text or BpmnPropertyKind.MultilineText
            && value is { Length: 0 }
                ? null
                : value;

        return OnEdit.InvokeAsync(new BpmnPropertyEdit(property.Name, submitted));
    }
}
