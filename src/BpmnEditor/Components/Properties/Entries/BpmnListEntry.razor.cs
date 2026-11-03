using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>The standard rendering of the list entry property editor.</summary>
public partial class BpmnListEntry
{
    private Microsoft.Extensions.Localization.IStringLocalizer<Hasseware.AspNetCore.Components.Bpmn.Resources.BpmnEditorStrings> EditorL => Localizer<Hasseware.AspNetCore.Components.Bpmn.Resources.BpmnEditorStrings>();

    private IEnumerable<BpmnPropertyColumn> VisibleColumns =>
        (Entry.Columns ?? []).Where(column => column.Name != BpmnExtensionNavigator.CollectionRowIndexKey);
    private List<Dictionary<string, string?>> _rows = [];
    private IReadOnlyList<IReadOnlyDictionary<string, string?>>? _source;
    private int _revision = -1;
    private string? _entryId;
    private object _rowIdentity = new();

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(_source, Entry.Rows) || _revision != Context.Revision || _entryId != Entry.Id)
        {
            if (_revision != Context.Revision || _entryId != Entry.Id || !SameRows(Entry.Rows))
            {
                _rowIdentity = new();
            }
            _source = Entry.Rows;
            _revision = Context.Revision;
            _entryId = Entry.Id;
            _rows = (Entry.Rows ?? []).Select(row => new Dictionary<string, string?>(row)).ToList();
        }

    }

    private bool SameRows(IReadOnlyList<IReadOnlyDictionary<string, string?>>? rows) =>
        rows is not null && rows.Count == _rows.Count && rows.Select((row, index) =>
            row.Count == _rows[index].Count && row.All(pair =>
                _rows[index].TryGetValue(pair.Key, out var value) && value == pair.Value)).All(equal => equal);

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
        _rowIdentity = new();
        return CommitAsync();
    }

    private Task MoveAsync(int index, int offset)
    {
        if (ReadOnly || index + offset < 0 || index + offset >= _rows.Count) return Task.CompletedTask;
        (_rows[index], _rows[index + offset]) = (_rows[index + offset], _rows[index]);
        _rowIdentity = new();
        return CommitAsync();
    }

    private Task CommitAsync() => Context.OnRowsChanged.InvokeAsync(_rows
        .Select(row => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>(row)).ToList());
}
