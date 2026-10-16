using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn;

/// <summary>The Fluent UI rendering of the feel entry property editor.</summary>
public partial class FluentBpmnFeelEntry
{
    private Microsoft.Extensions.Localization.IStringLocalizer<Hasseware.FluentUI.AspNetCore.Components.Bpmn.Resources.FluentBpmnEditorStrings> EditorL => Localizer<Hasseware.FluentUI.AspNetCore.Components.Bpmn.Resources.FluentBpmnEditorStrings>();

    private readonly string _warningId = "bpmn-feel-" + Guid.NewGuid().ToString("N");
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
    private string? DescribedBy => Entry.Error is not null
        ? Warning is not null ? ErrorId + " " + _warningId : ErrorId
        : Warning is not null ? _warningId : null;

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

    private void DraftChanged(string? value)
    {
        if (!ReadOnly) _value = "=" + (value ?? "");
    }

    private Task CommitExpressionAsync(FocusEventArgs _)
    {
        if (ReadOnly) return Task.CompletedTask;
        _value = string.IsNullOrEmpty(Expression) ? null : "=" + Expression;
        return SubmitAsync(_value);
    }

    private Task SubmitLiteralAsync(string? value)
    {
        if (ReadOnly) return Task.CompletedTask;
        _value = value;
        return SubmitAsync(value);
    }
}
