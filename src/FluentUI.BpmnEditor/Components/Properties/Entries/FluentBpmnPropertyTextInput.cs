using Hasseware.AspNetCore.Components.Bpmn;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn;

/// <summary>Standard Fluent input whose native draft can be owned by a property enhancement.</summary>
/// <param name="configuration">Fluent component configuration.</param>
public sealed class FluentBpmnPropertyTextInput(LibraryConfiguration configuration) : FluentTextInput(configuration)
{
    [CascadingParameter] private BpmnPropertyInputEnhancement? InputEnhancement { get; set; }

    /// <inheritdoc />
    protected override bool ShouldRender() => InputEnhancement?.IsEnhanced != true && base.ShouldRender();
}
