using Microsoft.AspNetCore.Components;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn;

/// <summary>The Fluent UI rendering of the property entry frame property editor.</summary>
public partial class FluentBpmnPropertyEntryFrame
{
    /// <summary>The editor control.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }
}
