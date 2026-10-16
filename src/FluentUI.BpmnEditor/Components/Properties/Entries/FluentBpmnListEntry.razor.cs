using Hasseware.AspNetCore.Components.Bpmn;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn;

/// <summary>The Fluent UI rendering of the list entry property editor.</summary>
public partial class FluentBpmnListEntry
{
    private Microsoft.Extensions.Localization.IStringLocalizer<Hasseware.FluentUI.AspNetCore.Components.Bpmn.Resources.FluentBpmnEditorStrings> EditorL => Localizer<Hasseware.FluentUI.AspNetCore.Components.Bpmn.Resources.FluentBpmnEditorStrings>();

    private IEnumerable<BpmnPropertyColumn> VisibleColumns =>
        (Entry.Columns ?? []).Where(column => column.Name != BpmnExtensionNavigator.CollectionRowIndexKey);
    private List<Dictionary<string, string?>> _rows = [];
    private IReadOnlyList<IReadOnlyDictionary<string, string?>>? _source;
    private int _revision = -1;
    private string? _entryId;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(_source, Entry.Rows) || _revision != Context.Revision || _entryId != Entry.Id)
        {
            _source = Entry.Rows;
            _revision = Context.Revision;
            _entryId = Entry.Id;
            _rows = (Entry.Rows ?? []).Select(row => new Dictionary<string, string?>(row)).ToList();
        }
    }

    private BpmnPropertyEntryContext CellContext(int index, BpmnPropertyColumn column) => Context with
    {
        Entry = new BpmnPropertyEntry($"{Entry.Id}:{index}:{column.Name}", EditorL["ListEntry.CellLabel", column.Label, index + 1],
            BpmnPropertyEntryKind.Text, _rows[index].GetValueOrDefault(column.Name))
        {
            Feel = column.Feel,
            ExpressionLanguage = column.ExpressionLanguage,
            Placeholder = Entry.Placeholder,
            IsReadOnly = ReadOnly,
        },
        OnValueChanged = EventCallback.Factory.Create<string?>(this, value => ChangeCellAsync(index, column.Name, value)),
    };

    private Task ChangeCellAsync(int index, string name, string? value)
    {
        if (ReadOnly) return Task.CompletedTask;
        _rows[index][name] = value;
        return CommitAsync();
    }

    private Task AddAsync()
    {
        if (ReadOnly) return Task.CompletedTask;
        _rows.Add(VisibleColumns.ToDictionary(column => column.Name, _ => (string?)null));
        return CommitAsync();
    }

    private Task RemoveAsync(int index)
    {
        if (ReadOnly) return Task.CompletedTask;
        _rows.RemoveAt(index);
        return CommitAsync();
    }

    private Task MoveAsync(int index, int offset)
    {
        if (ReadOnly || index + offset < 0 || index + offset >= _rows.Count) return Task.CompletedTask;
        (_rows[index], _rows[index + offset]) = (_rows[index + offset], _rows[index]);
        return CommitAsync();
    }

    private Task CommitAsync() => Context.OnRowsChanged.InvokeAsync(_rows
        .Select(row => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>(row)).ToList());
}
