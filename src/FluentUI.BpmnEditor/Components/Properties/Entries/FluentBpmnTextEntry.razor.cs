using Hasseware.AspNetCore.Components.Bpmn;
using Microsoft.AspNetCore.Components.Web;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn;

/// <summary>The Fluent UI rendering of the text entry property editor.</summary>
public partial class FluentBpmnTextEntry
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
    private void DraftChanged(string? value) { if (!ReadOnly) _value = value; }
    private Task CommitAsync(FocusEventArgs _) => ReadOnly ? Task.CompletedTask : SubmitAsync(_value);
}
