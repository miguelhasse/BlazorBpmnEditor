using System.Globalization;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn;

/// <summary>The Fluent UI rendering of the number entry property editor.</summary>
public partial class FluentBpmnNumberEntry
{
    private double? NumberValue => double.TryParse(Entry.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
}
