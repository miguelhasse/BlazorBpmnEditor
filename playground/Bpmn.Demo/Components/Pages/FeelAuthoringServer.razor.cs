using Microsoft.AspNetCore.Components;

namespace Bpmn.Demo.Components.Pages;

public partial class FeelAuthoringServer
{
    /// <summary>Selects the standard HTML kit with <c>?plain=true</c>; defaults to the Fluent UI kit.</summary>
    [SupplyParameterFromQuery(Name = "plain")]
    public bool Plain { get; set; }
}
