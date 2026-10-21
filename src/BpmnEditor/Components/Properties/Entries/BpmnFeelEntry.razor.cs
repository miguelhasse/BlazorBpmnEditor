using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>The standard rendering of the feel entry property editor.</summary>
public partial class BpmnFeelEntry
{
    private Microsoft.Extensions.Localization.IStringLocalizer<Hasseware.AspNetCore.Components.Bpmn.Resources.BpmnEditorStrings> EditorL => Localizer<Hasseware.AspNetCore.Components.Bpmn.Resources.BpmnEditorStrings>();

    private string? _value;
    private string? _source;
    private string? _entryId;
    private string? _elementId;
    private int _revision = -1;
    private BpmnFeelMode _mode;

    private bool FeelActive => Entry.Feel == BpmnFeelMode.Required ||
        Entry.Feel == BpmnFeelMode.Optional && _value?.StartsWith('=') == true;
    private string Expression => _value?.StartsWith('=') == true ? _value[1..] : _value ?? "";
    private string? Warning => FeelActive ? ExpressionError(_value) : null;
    private string WarningId { get; } = "bpmn-feel-" + Guid.NewGuid().ToString("N");
    private string? DescribedBy => Entry.Error is not null
        ? Warning is not null ? ErrorId + " " + WarningId : ErrorId
        : Warning is not null ? WarningId : null;

    private Type DefaultEditor => Editors.Resolve(Entry with { Feel = BpmnFeelMode.None });
    private IDictionary<string, object> EditorParameters => new Dictionary<string, object>
    {
        ["Context"] = Context with
        {
            Entry = Entry with { Value = _value },
            OnValueChanged = EventCallback.Factory.Create<string?>(this, SubmitLiteralAsync),
        },
    };

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (_source != Entry.Value || _revision != Context.Revision || _entryId != Entry.Id ||
            _elementId != Context.ElementId || _mode != Entry.Feel)
        {
            _source = Entry.Value;
            _value = Entry.Value;
            _revision = Context.Revision;
            _entryId = Entry.Id;
            _elementId = Context.ElementId;
            _mode = Entry.Feel;
        }
    }

    private Task ToggleAsync()
    {
        if (ReadOnly) return Task.CompletedTask;
        _value = FeelActive ? Expression : "=" + (_value ?? "");
        return SubmitAsync(_value);
    }

    private void Input(ChangeEventArgs args)
    {
        if (!ReadOnly) _value = "=" + (args.Value as string ?? "");
    }

    private Task ChangeAsync(ChangeEventArgs args)
    {
        if (ReadOnly) return Task.CompletedTask;
        var text = args.Value as string;
        _value = string.IsNullOrEmpty(text) ? null : "=" + text;
        return SubmitAsync(_value);
    }

    private Task SubmitLiteralAsync(string? value)
    {
        if (ReadOnly) return Task.CompletedTask;
        _value = value;
        return SubmitAsync(value);
    }
}
