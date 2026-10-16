using Hasseware.AspNetCore.Components.Bpmn.Icons;

using Microsoft.AspNetCore.Components;

namespace Bpmn.Demo.Client.Pages;

/// <summary>
/// Renders every glyph in the shared icon manifest, through the C# registry.
/// </summary>
/// <remarks>
/// This is the review surface for a font bump. Regenerating the manifest changes path data
/// wholesale, and the extraction that strips outer rings and diamonds is calibrated to one pinned
/// font version — so the only honest check is to look at the result. It doubles as proof that the
/// C# side loads the same manifest the canvas bundles.
/// </remarks>
public sealed partial class Icons : ComponentBase
{
    private static BpmnIconRegistry Registry => BpmnIconRegistry.Default;

    private (string Kind, string Title)[] Families =>
    [
        ("gateway", Localizer["Icons.Gateways"]),
        ("eventTrigger", Localizer["Icons.EventTriggers"]),
        ("taskType", Localizer["Icons.TaskTypes"]),
        ("activityMarker", Localizer["Icons.ActivityMarkers"]),
    ];

    /// <summary>The distinct glyphs a family assigns, in manifest order.</summary>
    private static List<BpmnIcon> AssignedIcons(string kind)
    {
        var ids = Registry.Manifest.Assignments
            .Where(assignment => assignment.Kind == kind)
            .Select(assignment => assignment.IconId)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        return Registry.Icons.Where(icon => ids.Contains(icon.IconId)).ToList();
    }
}
