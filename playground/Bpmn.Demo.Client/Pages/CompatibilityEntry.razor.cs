using Microsoft.AspNetCore.Components;

namespace Bpmn.Demo.Client.Pages;

public partial class CompatibilityEntry
{
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private Task ChangedAsync(ChangeEventArgs args) => SubmitAsync(args.Value as string);
}
