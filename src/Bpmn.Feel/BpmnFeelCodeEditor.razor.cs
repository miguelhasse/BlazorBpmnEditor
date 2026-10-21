using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Hasseware.AspNetCore.Components.Bpmn.Feel;

/// <summary>Optional rich FEEL expression editor with a plain-text fallback.</summary>
public partial class BpmnFeelCodeEditor : IAsyncDisposable
{
    private readonly CancellationTokenSource _disposalCts = new();
    private ElementReference _container;
    private IJSObjectReference? _module;
    private IJSObjectReference? _editor;
    private DotNetObjectReference<BpmnFeelCodeEditor>? _reference;
    private string? _value;
    private string? _source;
    private string? _entryId;
    private string? _elementId;
    private int _revision = -1;
    private int _generation;
    private BpmnFeelMode _mode;
    private bool _syncValue;
    private bool _ready;
    private bool _initializing;
    private bool _failed;
    private bool _disposed;
    private IReadOnlyList<BpmnFeelVariable>? _variables;
    private string[] _diagnostics = [];

    [Inject] private IJSRuntime JS { get; set; } = default!;
    private bool FeelActive => Entry.Feel == BpmnFeelMode.Required ||
        Entry.Feel == BpmnFeelMode.Optional && _value?.StartsWith('=') == true;
    private bool UseRichEditor => FeelActive && !ReadOnly;
    private string Expression => _value?.StartsWith('=') == true ? _value[1..] : _value ?? "";
    private string? Warning => !FeelActive ? null : ExpressionError(_value) ?? (_diagnostics.Length > 0
        ? Localizer<Resources.BpmnStrings>()["Expression.Warnings", _diagnostics.Length, string.Join("; ", _diagnostics)].Value : null);
    private string WarningId { get; } = "bpmn-feel-code-" + Guid.NewGuid().ToString("N");
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
            _diagnostics = [];
            _generation++;
            _syncValue = true;
        }
        if (!UseRichEditor) _ready = false;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed || _initializing) return;
        var cancellationToken = _disposalCts.Token;
        try
        {
            if (!UseRichEditor)
            {
                await DisposeEditorAsync();
                return;
            }
            if (_failed) return;
            if (_editor is null)
            {
                _initializing = true;
                if (_module is null)
                {
                    var module = await JS.InvokeAsync<IJSObjectReference>("import", cancellationToken,
                        BpmnFeelInteropContract.ModulePath);
                    if (_disposed)
                    {
                        await InvokeSafelyAsync(async () => await module.DisposeAsync());
                        return;
                    }
                    _module = module;
                }
                if (!UseRichEditor) return;
                _reference ??= DotNetObjectReference.Create(this);
                var variables = Context.Variables;
                var editor = await _module.InvokeAsync<IJSObjectReference>("create", cancellationToken, _container, _reference, new
                {
                    value = Expression,
                    variables,
                    readOnly = ReadOnly,
                    label = Entry.Label,
                    describedBy = DescribedBy ?? "",
                    invalid = Entry.Error is not null || Warning is not null,
                    placeholder = Entry.Placeholder ?? "",
                    revision = _generation,
                });
                if (_disposed || !UseRichEditor)
                {
                    await ReleaseEditorAsync(editor);
                    return;
                }
                _editor = editor;
                _variables = variables;
                _ready = true;
                StateHasChanged();
            }
            if (_syncValue)
            {
                _syncValue = false;
                await _editor.InvokeVoidAsync("setValue", cancellationToken, Expression, _generation);
            }
            if (!ReferenceEquals(_variables, Context.Variables))
            {
                _variables = Context.Variables;
                await _editor.InvokeVoidAsync("setVariables", cancellationToken, Context.Variables);
            }
            await _editor.InvokeVoidAsync("setReadOnly", cancellationToken, ReadOnly);
            await _editor.InvokeVoidAsync("setAttributes", cancellationToken, Entry.Label, DescribedBy ?? "",
                Entry.Error is not null || Warning is not null);
        }
        catch (Exception exception) when (IsInteropUnavailable(exception))
        {
            _failed = true;
            _ready = false;
            await DisposeEditorAsync();
            if (!_disposed) StateHasChanged();
        }
        finally
        {
            _initializing = false;
        }
    }

    private Task ToggleAsync()
    {
        if (ReadOnly) return Task.CompletedTask;
        _value = FeelActive ? Expression : "=" + (_value ?? "");
        _generation++;
        _diagnostics = [];
        _syncValue = true;
        _ready = false;
        return SubmitAsync(_value);
    }
    private void Input(ChangeEventArgs args)
    {
        if (!ReadOnly) _value = "=" + (args.Value as string ?? "");
    }
    private Task ChangeAsync(ChangeEventArgs args) => CommitExpressionAsync(args.Value as string ?? "", _generation);
    private Task KeyDownAsync(KeyboardEventArgs args) => args.Key == "Enter" && (args.CtrlKey || args.MetaKey)
        ? CommitExpressionAsync(Expression, _generation) : Task.CompletedTask;
    private Task SubmitLiteralAsync(string? value)
    {
        if (ReadOnly) return Task.CompletedTask;
        _value = value;
        return SubmitAsync(value);
    }

    /// <summary>Commits the current expression on blur or Ctrl/Cmd+Enter.</summary>
    /// <param name="expression">Expression without its persistence prefix.</param>
    /// <param name="generation">Editor revision guarding stale callbacks.</param>
    [JSInvokable]
    public Task CommitExpressionAsync(string expression, int generation) => InvokeAsync(async () =>
    {
        if (_disposed || ReadOnly || !FeelActive || generation != _generation) return;
        _value = string.IsNullOrEmpty(expression) ? null : "=" + expression;
        await SubmitAsync(_value);
        StateHasChanged();
    });

    /// <summary>Displays advisory upstream parser diagnostics without blocking commits.</summary>
    /// <param name="messages">Diagnostic messages.</param>
    /// <param name="generation">Editor revision guarding stale callbacks.</param>
    [JSInvokable]
    public Task ReportLintAsync(string[] messages, int generation) => InvokeAsync(() =>
    {
        if (_disposed || ReadOnly || !FeelActive || generation != _generation) return;
        _diagnostics = messages;
        StateHasChanged();
    });

    private static bool IsInteropUnavailable(Exception exception) => exception is JSException or JSDisconnectedException or
        OperationCanceledException or ObjectDisposedException or InvalidOperationException;

    private async ValueTask DisposeEditorAsync()
    {
        var editor = _editor;
        _editor = null;
        _ready = false;
        _variables = null;
        await ReleaseEditorAsync(editor);
    }

    private static async ValueTask ReleaseEditorAsync(IJSObjectReference? editor)
    {
        if (editor is null) return;
        await InvokeSafelyAsync(async () => await editor.InvokeVoidAsync("dispose"));
        await InvokeSafelyAsync(async () => await editor.DisposeAsync());
    }

    private static async Task InvokeSafelyAsync(Func<Task> call)
    {
        try { await call().ConfigureAwait(false); }
        catch (JSDisconnectedException) { }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _disposalCts.CancelAsync();
        await DisposeEditorAsync();
        if (_module is not null)
        {
            await InvokeSafelyAsync(async () => await _module.DisposeAsync());
            _module = null;
        }
        _reference?.Dispose();
        _reference = null;
        _disposalCts.Dispose();
        GC.SuppressFinalize(this);
    }
}
