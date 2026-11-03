using Microsoft.AspNetCore.Components;

namespace Bpmn.Demo.Client.Pages;

public partial class FeelAuthoring
{
    /// <summary>Selects the standard HTML kit with <c>?plain=true</c>; defaults to the Fluent UI kit.</summary>
    [SupplyParameterFromQuery(Name = "plain")]
    public bool Plain { get; set; }
}
