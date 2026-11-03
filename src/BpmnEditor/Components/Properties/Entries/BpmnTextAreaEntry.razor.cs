using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>The standard rendering of the text area entry property editor.</summary>
public partial class BpmnTextAreaEntry
{
    private string? _value;
    private (string?, string, string?, int)? _source;
    private BpmnPropertyEntryContext ValidationContext => ValidatedContext(_value);
    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // Keep an uncommitted draft across unrelated parent re-renders; reset only when the source changes.
        var source = (Entry.Value, Entry.Id, Context.ElementId, Context.Revision);
        if (_source == source) return;
        _source = source;
        _value = Entry.Value;
    }
    private Task Input(ChangeEventArgs args)
    {
        if (ReadOnly || InputEnhancement?.IsEnhanced == true) return Task.CompletedTask;
        _value = args.Value as string;
        return InputEnhancement?.DraftChanged?.Invoke(_value) ?? Task.CompletedTask;
    }
    private Task ChangeAsync(ChangeEventArgs args)
    {
        if (ReadOnly || InputEnhancement?.IsEnhanced == true) return Task.CompletedTask;
        _value = args.Value as string;
        return SubmitAsync(_value);
    }
}
