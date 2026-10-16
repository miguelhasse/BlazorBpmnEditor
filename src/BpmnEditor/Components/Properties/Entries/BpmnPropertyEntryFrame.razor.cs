using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>The standard rendering of the property entry frame property editor.</summary>
public partial class BpmnPropertyEntryFrame
{
    /// <summary>The editor control.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }
}
