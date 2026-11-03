using Hasseware.AspNetCore.Components.Bpmn;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn;

/// <summary>Standard Fluent textarea whose native draft can be owned by a property enhancement.</summary>
/// <param name="configuration">Fluent component configuration.</param>
public sealed class FluentBpmnPropertyTextArea(LibraryConfiguration configuration) : FluentTextArea(configuration)
{
    [CascadingParameter] private BpmnPropertyInputEnhancement? InputEnhancement { get; set; }

    /// <inheritdoc />
    protected override bool ShouldRender() => InputEnhancement?.IsEnhanced != true && base.ShouldRender();
}
