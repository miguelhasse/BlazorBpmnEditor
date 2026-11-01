using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Managed FEEL highlighting, parse diagnostics and completion with a native text input.</summary>
public abstract partial class BpmnFeelSharpEditorBase : BpmnPropertyEntryComponent, IAsyncDisposable
{
    private readonly FeelSharpSyntax _syntax = new();
    private readonly CancellationTokenSource _disposal = new();
    internal ElementReference _surface;
    internal ElementReference _input;
    private IJSObjectReference? _module;
    private IJSObjectReference? _bridge;
    private DotNetObjectReference<BpmnFeelSharpEditorBase>? _reference;
    private string? _value;
    private string? _source;
    private string? _entryId;
    private string? _elementId;
    internal string _initialText = "";
    private string? _parsedText;
    private string? _parseError;
    private int _revision = -1;
    internal int _generation;
    private int _attachedGeneration = -1;
    private long _sequence;
    private int _selectionStart;
    private int _selectionEnd;
    internal int _selected;
    private bool _composing;
    internal bool _enhanced;
    private bool _unavailable;
    private bool _initializing;
    private bool _disposed;
    private bool _wasReadOnly;
    private IReadOnlyList<BpmnFeelVariable>? _variables;
    private BpmnFeelMode _mode;
    internal IReadOnlyList<FeelSharpCompletion> _completions = [];

    [Inject] private IJSRuntime JS { get; set; } = default!;

    internal bool FeelActive => Entry.Feel == BpmnFeelMode.Required ||
        Entry.Feel == BpmnFeelMode.Optional && _value?.StartsWith('=') == true;
    internal string Expression => FeelSharpSyntax.Normalize(_value ?? "");
    internal string DisplayValue => ReadOnly ? _value ?? "" : Expression;
    internal string InputId { get; } = "bpmn-feelsharp-" + Guid.NewGuid().ToString("N");
    internal string CompletionId => InputId + "-completion";
    internal string WarningId => InputId + "-warning";
    internal string OptionId(int index) => CompletionId + "-" + index;
    internal virtual ElementReference InputElement => _input;
    internal virtual string AttachMethod => "attach";
    internal string? Warning
    {
        get
        {
            if (!FeelActive)
            {
                return null;
            }
            if (_parsedText != Expression)
            {
                _parsedText = Expression;
                _parseError = _syntax.Validate(Expression);
            }
            return _parseError is null ? null :
                Localizer<Resources.BpmnStrings>()["Expression.Warning", Text("Expression.Feel"), _parseError].Value;
        }
    }
    internal string? DescribedBy => Entry.Error is not null
        ? Warning is not null ? ErrorId + " " + WarningId : ErrorId
        : Warning is not null ? WarningId : null;
    internal Type DefaultEditor => Editors.Resolve(Entry with { Feel = BpmnFeelMode.None });
    internal IDictionary<string, object> EditorParameters
    {
        get
        {
            var generation = _generation;
            return new Dictionary<string, object>
            {
                ["Context"] = Context with
                {
                    Entry = Entry with { Value = _value },
                    OnValueChanged = EventCallback.Factory.Create<string?>(this, value => SubmitLiteralAsync(value, generation)),
                },
            };
        }
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (_source != Entry.Value || _revision != Context.Revision || _entryId != Entry.Id ||
            _elementId != Context.ElementId || _mode != Entry.Feel || _wasReadOnly != ReadOnly)
        {
            _source = Entry.Value;
            _value = Entry.Value;
            _revision = Context.Revision;
            _entryId = Entry.Id;
            _elementId = Context.ElementId;
            _mode = Entry.Feel;
            _wasReadOnly = ReadOnly;
            ResetInput();
        }
        if (ReadOnly || !FeelActive)
        {
            _completions = [];
            _enhanced = false;
        }
        else if (!_composing && _enhanced && _completions.Count > 0 && !ReferenceEquals(_variables, Context.Variables))
        {
            _completions = FeelSharpLanguage.Complete(Expression, _selectionStart, _selectionEnd, Context.Variables);
            _selected = Math.Min(_selected, Math.Max(0, _completions.Count - 1));
        }
        _variables = Context.Variables;
    }

    private void ResetInput()
    {
        _generation++;
        _sequence = 0;
        _enhanced = false;
        _composing = false;
        _completions = [];
        _initialText = DisplayValue;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed || _initializing || _unavailable)
        {
            return;
        }
        if (ReadOnly || !FeelActive)
        {
            await ReleaseBridgeAsync();
            return;
        }
        if (_bridge is not null && _attachedGeneration == _generation)
        {
            return;
        }
        _initializing = true;
        var generation = _generation;
        try
        {
            await ReleaseBridgeAsync();
            if (_module is null)
            {
                var module = await JS.InvokeAsync<IJSObjectReference>("import", _disposal.Token,
                    "./_content/Hasseware.AspNetCore.Components.Bpmn.FeelSharp/feelsharp-editor.js");
                if (_disposed)
                {
                    await module.DisposeAsync();
                    return;
                }
                _module = module;
            }
            if (_disposed || ReadOnly || generation != _generation)
            {
                return;
            }
            _reference ??= DotNetObjectReference.Create(this);
            var bridge = await _module.InvokeAsync<IJSObjectReference>(AttachMethod, _disposal.Token, _surface, InputElement, _reference, generation);
            if (_disposed || ReadOnly || generation != _generation)
            {
                await bridge.InvokeVoidAsync("dispose");
                await bridge.DisposeAsync();
                return;
            }
            _bridge = bridge;
            _attachedGeneration = generation;
            _enhanced = true;
            StateHasChanged();
        }
        catch (OperationCanceledException) when (_disposed)
        {
            // Component disposal intentionally cancels pending DOM initialization.
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException)
        {
            _unavailable = true;
            _enhanced = false;
            if (Services.GetService(typeof(ILogger<BpmnFeelSharpCodeEditor>)) is ILogger<BpmnFeelSharpCodeEditor> logger)
            {
                LogEnhancementUnavailable(logger, exception);
            }
            else
            {
                throw new InvalidOperationException("FEEL editor enhancement failed.", exception);
            }
            if (!_disposed)
            {
                StateHasChanged();
            }
        }
        finally
        {
            _initializing = false;
            if (!_disposed && generation != _generation)
            {
                StateHasChanged();
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "FeelSharp editor DOM enhancement unavailable; using native text editing.")]
    private static partial void LogEnhancementUnavailable(ILogger logger, Exception exception);

    internal Task ToggleAsync()
    {
        if (ReadOnly)
        {
            return Task.CompletedTask;
        }
        _value = FeelActive ? Expression : "=" + (_value ?? "");
        ResetInput();
        return SubmitAsync(_value);
    }

    internal void Input(ChangeEventArgs args, int generation)
    {
        if (!_disposed && generation == _generation && !ReadOnly && !_enhanced)
        {
            _value = "=" + (args.Value as string ?? "");
        }
    }

    internal Task ChangeAsync(ChangeEventArgs args, int generation) =>
        generation != _generation || _enhanced ? Task.CompletedTask : CommitAsync(args.Value as string ?? "");

    internal Task KeyDownAsync(KeyboardEventArgs args, int generation) =>
        generation == _generation && !_enhanced && args.Key == "Enter" && (args.CtrlKey || args.MetaKey)
            ? CommitAsync(Expression) : Task.CompletedTask;

    private Task SubmitLiteralAsync(string? value, int generation)
    {
        if (_disposed || generation != _generation || ReadOnly)
        {
            return Task.CompletedTask;
        }
        _value = value;
        return SubmitAsync(value);
    }

    private async Task CommitAsync(string expression)
    {
        if (_disposed || ReadOnly || !FeelActive || _composing)
        {
            return;
        }
        _completions = [];
        _value = string.IsNullOrEmpty(expression) ? null : "=" + expression;
        await SubmitAsync(_value);
    }

    /// <summary>Receives a native UTF-16 selection snapshot, guarded against old DOM instances and callbacks.</summary>
    [JSInvokable]
    public Task SnapshotAsync(string text, int start, int end, long sequence, int generation, bool composing,
        bool focused) => InvokeAsync(() =>
    {
        if (_disposed || ReadOnly || !FeelActive || generation != _generation || sequence <= _sequence ||
            start < 0 || end < start || end > text.Length)
        {
            return;
        }
        _sequence = sequence;
        _selectionStart = start;
        _selectionEnd = end;
        _value = "=" + text;
        _composing = composing;
        _selected = 0;
        _completions = composing || !focused ? [] : FeelSharpLanguage.Complete(text, start, end, Context.Variables);
        StateHasChanged();
    });

    /// <summary>Handles keys after the bridge has synchronized the native text and caret.</summary>
    [JSInvokable]
    public Task NativeKeyAsync(string key, long sequence, int generation) => InvokeAsync(async () =>
    {
        if (_disposed || ReadOnly || generation != _generation || sequence != _sequence || _composing)
        {
            return;
        }
        if (key == "commit")
        {
            await CommitAsync(Expression);
        }
        else if (key == "Escape")
        {
            _completions = [];
        }
        else if (_completions.Count > 0)
        {
            if (key == "ArrowDown")
            {
                _selected = (_selected + 1) % _completions.Count;
            }
            else if (key == "ArrowUp")
            {
                _selected = (_selected + _completions.Count - 1) % _completions.Count;
            }
            else if (key is "Enter" or "Tab")
            {
                await ApplyCompletionAsync(_selected);
            }
        }
        StateHasChanged();
    });

    internal async Task ApplyCompletionAsync(int index)
    {
        if (_disposed || ReadOnly || _composing || _bridge is null || index < 0 || index >= _completions.Count)
        {
            return;
        }
        var completion = _completions[index];
        var sequence = _sequence;
        var generation = _generation;
        await _bridge.InvokeAsync<bool>("replace", Expression, completion.Start, completion.Length, completion.Text,
            sequence, generation);
        if (_sequence == sequence && _generation == generation)
        {
            _completions = [];
        }
    }

    private async ValueTask ReleaseBridgeAsync()
    {
        var bridge = _bridge;
        _bridge = null;
        _attachedGeneration = -1;
        if (bridge is not null)
        {
            await bridge.InvokeVoidAsync("dispose");
            await bridge.DisposeAsync();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        await _disposal.CancelAsync();
        try
        {
            await ReleaseBridgeAsync();
            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // A disconnected circuit cannot release browser-side handles.
        }
        finally
        {
            _reference?.Dispose();
            _disposal.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
