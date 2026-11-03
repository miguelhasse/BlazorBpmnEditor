using Hasseware.AspNetCore.Components.Bpmn.Editing;
using System.Collections.Immutable;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private string? _expressionSource;
    private string _expression = "";
    private string? _source;
    private string? _entryId;
    private string? _elementId;
    internal string _initialText = "";
    private string? _parsedText;
    private string? _parseError;
    private int _diagnosticGeneration = -1;
    private int _renderedGeneration = -1;
    private bool _diagnosticFeelActive;
    private long _analysisVersion;
    private CancellationTokenSource _analysisCancellation = new();
    private string? _scheduledExpression;
    private int _scheduledGeneration = -1;
    private int _revision = -1;
    internal int _generation;
    private object _draftIdentity = new();
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
    private bool _propertyFocused;
    private BpmnPropertyInputEnhancement? _propertyEnhancement;
    private bool _wasPropertyInput;
    [CascadingParameter] private BpmnEditorSession? EditorSession { get; set; }

    /// <summary>Enhance the existing property input; false selects the standalone rich editor.</summary>
    [Parameter] public bool PropertyInput { get; set; } = true;
    /// <summary>Hide redundant visible chrome when hosted inside an expression manager.</summary>
    [Parameter] public bool Minimal { get; set; }

    internal bool PrefixInput => PropertyInput && Entry.Feel == BpmnFeelMode.Optional;
    internal bool IsPropertyReadOnly => ReadOnly || _expanded;
    internal string? PropertyDescribedBy => string.Join(" ", new[] { Entry.Error is not null ? ErrorId : null,
        Warning is not null ? WarningId : null, SelectedCompletion is not null ? HelpId : null }.Where(value => value is not null));
    internal bool CanManage => ExpandedEnabled && !ReadOnly && !_disposed &&
        (FeelActive || Options.LiteralManagerBehavior == BpmnFeelSharpLiteralManagerBehavior.AllowConversionOnApply);
    internal string ManagerTitle => Localizer<Resources.BpmnStrings>()["Feel.ManagerProperty", Entry.Label];
    internal BpmnPropertyInputEnhancement PropertyEnhancement
    {
        get
        {
            if (_propertyEnhancement is null)
            {
                var generation = _generation;
                _propertyEnhancement = new()
                {
                    DraftChanged = value => generation == _generation ? PropertyFallbackAsync(value) : Task.CompletedTask,
                    KeyDown = args => generation == _generation && args.Key == "Enter" && (args.CtrlKey || args.MetaKey)
                        ? PropertyCommitAsync(PrefixInput || !FeelActive ? _value ?? "" : Expression) : Task.CompletedTask,
                };
            }
            _propertyEnhancement.IsEnhanced = _enhanced;
            return _propertyEnhancement;
        }
    }
    internal IDictionary<string, object> PropertyParameters
    {
        get
        {
            var generation = _generation;
            return new Dictionary<string, object>
            {
                ["Context"] = Context with
                {
                    Entry = Entry with { Feel = BpmnFeelMode.None, ExpressionLanguage = Validation.BpmnExpressionLanguage.None, Value = _initialText },
                    IsReadOnly = ReadOnly || _expanded,
                    ErrorId = ErrorId,
                    OnValueChanged = EventCallback.Factory.Create<string?>(this, value =>
                        generation == _generation ? CommitPropertyAsync(value) : Task.CompletedTask),
                },
            };
        }
    }

    /// <summary>Marks this property as the editor toolbar's current target without committing it.</summary>
    [JSInvokable]
    public Task ActivatePropertyAsync() => InvokeAsync(() =>
    {
        if (PropertyInput && !_disposed && !_expanded)
        {
            _propertyFocused = true;
            RefreshPropertyAction(true);
        }
    });

    private void RefreshPropertyAction(bool activate = false)
    {
        if (_propertyFocused && Context.ElementId is { } id && EditorSession is { } session)
        {
            if (!activate && !ReferenceEquals(session.PropertyAction?.Owner, this)) return;
            session.ActivatePropertyAction(new(this, id, Text("Expression.Feel"), CanManage, OpenExpandedAsync));
        }
    }

    private Task PropertyFallbackAsync(string? value) => PropertySnapshotAsync(value ?? "", 0, 0,
        _sequence + 1, _generation, false, false);

    private Task CommitPropertyAsync(string? value) => _enhanced ? Task.CompletedTask :
        PropertyCommitAsync(value ?? "");

    private async Task PropertyCommitAsync(string text)
    {
        if (ReadOnly || _expanded || _disposed || _composing) return;
        if (PrefixInput || !FeelActive)
        {
            _value = text;
            await SubmitAsync(string.IsNullOrEmpty(text) ? null : text);
            await AnalyzeAsync(true);
        }
        else
        {
            await CommitAsync(text);
        }
        RefreshPropertyAction();
    }

    /// <summary>Receives the actual property input value, retaining optional literal/prefix semantics.</summary>
    [JSInvokable]
    public Task PropertySnapshotAsync(string text, int start, int end, long sequence, int generation, bool composing, bool focused) =>
        InvokeAsync(async () =>
        {
            if (_disposed || ReadOnly || _expanded || generation != _generation || sequence <= _sequence ||
                start < 0 || end < start || end > text.Length) return;
            _value = PrefixInput || !FeelActive ? text : "=" + text;
            var prefix = PrefixInput && FeelActive && text.StartsWith('=') ? 1 : 0;
            _sequence = sequence;
            _selectionStart = Math.Max(0, start - prefix);
            _selectionEnd = Math.Max(0, end - prefix);
            _composing = composing;
            _selected = 0;
            _completions = !FeelActive || composing || !focused || !Options.AutomaticCompletion ? [] : Complete();
            await AnalyzeAsync();
            RefreshPropertyAction();
            if (!_disposed && generation == _generation)
            {
                if (!composing) await DraftChanged.InvokeAsync(new(FeelActive ? Expression : text, _selectionStart, _selectionEnd));
                StateHasChanged();
            }
        });

    /// <summary>Dispatches native property keys without converting literal values implicitly.</summary>
    [JSInvokable]
    public Task PropertyKeyAsync(string key, long sequence, int generation) =>
        key == "commit" && PropertyInput
            ? InvokeAsync(() => sequence == _sequence && generation == _generation
                ? PropertyCommitAsync(PrefixInput || !FeelActive ? _value ?? "" : Expression) : Task.CompletedTask)
            : NativeKeyAsync(key, sequence, generation);
    private bool _wasReadOnly;
    private IReadOnlyList<BpmnFeelVariable>? _variables;
    private BpmnFeelMode _mode;
    internal IReadOnlyList<FeelSharpCompletion> _completions = [];
    internal bool _expanded;
    internal bool _managerMounted;
    internal string? _expandedValue;
    private int _expandedGeneration;
    internal bool ExpandedEnabled => EnableExpansion && Options.ExpandedEditor;
    internal IBpmnFeelSharpEvaluation? WorkspaceEvaluator => Options.WorkspaceEvaluator;
    internal BpmnFeelSharpHostActions? WorkspaceActions => Options.WorkspaceActions;
    internal virtual Type WorkspaceEditorType => typeof(BpmnFeelSharpCodeEditor);
    internal virtual async Task OpenExpandedAsync()
    {
        if (!ReadOnly && !_disposed && !_managerMounted && (!PropertyInput || CanManage))
        {
            var generation = _generation;
            var draft = await CaptureAsync();
            if (_disposed || ReadOnly || generation != _generation || _managerMounted)
            {
                return;
            }
            _expandedValue = draft.Text;
            _expandedGeneration = generation;
            _expanded = true;
            _managerMounted = true;
            _completions = [];
            RefreshPropertyAction();
            await InvokeAsync(StateHasChanged);
        }
    }
    internal async Task ApplyExpandedAsync(string? value)
    {
        if (_disposed || ReadOnly || !_expanded || !_managerMounted || _expandedGeneration != _generation)
        {
            throw new InvalidOperationException("The BPMN entry changed while expanded editing was open.");
        }
        _expanded = false;
        if (PropertyInput && !FeelActive)
        {
            _value = "=" + (value ?? "");
        }
        await CommitAsync(value ?? "");
        ResetInput();
    }
    internal void CancelExpanded()
    {
        _expanded = false;
        StateHasChanged();
    }
    internal void ManagerClosed()
    {
        _managerMounted = false;
        RefreshPropertyAction();
        EditorSession?.ReturnPropertyActionFocus(this);
    }
    private string? _tokenText;
    private IReadOnlyList<FeelSharpToken> _tokens = [];
    private BpmnFeelSharpEditorOptions Options =>
        Services.GetService(typeof(IOptions<BpmnFeelSharpEditorOptions>)) is IOptions<BpmnFeelSharpEditorOptions> options
            ? options.Value : _defaultOptions;
    private readonly BpmnFeelSharpEditorOptions _defaultOptions = new();
    internal IReadOnlyList<FeelSharpToken> Tokens
    {
        get
        {
            if (_tokenText != DisplayValue)
            {
                _tokenText = DisplayValue;
                _tokens = FeelSharpLanguage.Tokenize(DisplayValue);
            }
            return _tokens;
        }
    }
    internal FeelSharpSignature? ActiveSignature => Options.SignatureHelp
        ? FeelSharpAuthoring.Signature(Expression, _selectionStart, Tokens, Options.Functions) : null;
    internal bool AcceptEnter => Options.AcceptEnter;
    internal bool AcceptTab => Options.AcceptTab;
    internal string Density => Options.Density switch
    {
        BpmnFeelSharpDensity.Compact => "compact",
        BpmnFeelSharpDensity.Comfortable => "comfortable",
        _ => "standard",
    };
    private IReadOnlyList<FeelSharpCompletion> Complete(bool explicitlyRequested = false) =>
        FeelSharpLanguage.Complete(Expression, _selectionStart, _selectionEnd, Context.Variables, explicitlyRequested, Options, Tokens);

    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>Current immutable advisory parse diagnostics; source ranges may be unavailable.</summary>
    public ImmutableArray<BpmnFeelSharpDiagnostic> Diagnostics { get; private set; } = [];

    /// <summary>Notifies directly rendered editors' hosts of meaningful diagnostic analysis updates.</summary>
    [Parameter] public EventCallback<BpmnFeelSharpDiagnosticsUpdate> DiagnosticsChanged { get; set; }

    /// <summary>Reports finalized draft text separately from committed values; no automatic evaluation.</summary>
    [Parameter] public EventCallback<BpmnFeelSharpDraft> DraftChanged { get; set; }

    /// <summary>Allows expansion when enabled by registration options; workspace fields disable nested expansion.</summary>
    [Parameter] public bool EnableExpansion { get; set; } = true;

    /// <summary>Captures the current native draft for an explicit host action, rejecting stale inputs.</summary>
    public async Task<BpmnFeelSharpDraft> CaptureAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var generation = _generation;
        var draft = _bridge is null ? new(Expression, _selectionStart, _selectionEnd) :
            await _bridge.InvokeAsync<BpmnFeelSharpDraft>("read");
        if (_disposed || generation != _generation)
        {
            throw new InvalidOperationException("The FEEL entry changed while capturing its draft.");
        }
        return draft with { SourceIdentity = _draftIdentity };
    }

    /// <summary>Applies an explicitly accepted host edit only if text and generation still match.</summary>
    public async Task ApplyDraftAsync(BpmnFeelSharpDraft expected, string replacement)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(replacement);
        if (_disposed || ReadOnly || _expanded || _composing || _bridge is null)
        {
            throw new InvalidOperationException("Native FEEL editing is unavailable.");
        }
        if (!ReferenceEquals(expected.SourceIdentity, _draftIdentity))
        {
            throw new InvalidOperationException("Capture the current FEEL entry before applying an edit.");
        }
        var applied = await _bridge.InvokeAsync<bool>("replace", expected.Text, 0, expected.Text.Length,
            replacement, _sequence, _generation);
        if (!applied)
        {
            throw new InvalidOperationException("The FEEL draft changed before the edit could be applied.");
        }
    }

    internal bool FeelActive => Entry.Feel == BpmnFeelMode.Required ||
        Entry.Feel == BpmnFeelMode.Optional && _value?.StartsWith('=') == true;
    internal string Expression
    {
        get
        {
            if (!ReferenceEquals(_expressionSource, _value))
            {
                _expressionSource = _value;
                _expression = FeelSharpSyntax.Normalize(_value ?? "");
            }
            return _expression;
        }
    }
    internal string DisplayValue => ReadOnly ? _value ?? "" : Expression;
    internal string InputId { get; } = "bpmn-feelsharp-" + Guid.NewGuid().ToString("N");
    internal string CompletionId => InputId + "-completion";
    internal string WarningId => InputId + "-warning";
    internal string HelpId => InputId + "-help";
    internal string ShortcutsId => InputId + "-shortcuts";
    internal FeelSharpCompletion? SelectedCompletion => _selected >= 0 && _selected < _completions.Count
        ? _completions[_selected] : null;
    internal string? CompletionInfo => SelectedCompletion switch
    {
        { PlaceholderStart: not null, Info: { } key } => Text(key),
        { Example: not null, HostFunction: false } completion => Text("Feel.Function." + completion.Label),
        { } completion => completion.Info,
        _ => null,
    };
    internal string CompletionAnnouncement => _completions.Count == 0 ? "" :
        Localizer<Resources.BpmnStrings>()["Feel.CompletionCount", _completions.Count].Value;
    internal string OptionId(int index) => CompletionId + "-" + index;
    internal virtual ElementReference InputElement => _input;
    internal virtual string AttachMethod => PropertyInput ? "attachProperty" : "attach";
    internal string? Warning
    {
        get
        {
            if (!FeelActive)
            {
                return null;
            }
            return _parseError is null ? null :
                Localizer<Resources.BpmnStrings>()["Expression.Warning", Text("Expression.Feel"), _parseError].Value;
        }
    }
    internal string? DescribedBy => Entry.Error is not null
        ? Warning is not null ? ErrorId + " " + WarningId : ErrorId
        : Warning is not null ? WarningId : null;
    internal string InputDescribedBy => string.Join(" ", new[]
        { DescribedBy, ShortcutsId, SelectedCompletion?.Info is not null ? HelpId : null }
        .Where(value => value is not null));
    internal Type DefaultEditor
    {
        get
        {
            var renderer = Editors.Resolve(Entry with { Feel = BpmnFeelMode.None });
            if (typeof(BpmnFeelSharpEditorBase).IsAssignableFrom(renderer))
            {
                throw new InvalidOperationException("The underlying property renderer must be a text input or textarea, not a FEEL editor.");
            }
            return renderer;
        }
    }
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
        if (_elementId != Context.ElementId || _entryId != Entry.Id || ReadOnly || _wasPropertyInput != PropertyInput)
        {
            _propertyFocused = false;
            EditorSession?.ReleasePropertyAction(this);
        }
        if (_source != Entry.Value || _revision != Context.Revision || _entryId != Entry.Id ||
            _elementId != Context.ElementId || _mode != Entry.Feel || _wasReadOnly != ReadOnly || _wasPropertyInput != PropertyInput)
        {
            _source = Entry.Value;
            _value = Entry.Value;
            _revision = Context.Revision;
            _entryId = Entry.Id;
            _elementId = Context.ElementId;
            _mode = Entry.Feel;
            _wasReadOnly = ReadOnly;
            _wasPropertyInput = PropertyInput;
            ResetInput();
        }
        if (ReadOnly || !FeelActive && !PropertyInput)
        {
            _completions = [];
            _enhanced = false;
        }
        else if (!_composing && _enhanced && _completions.Count > 0 && !ReferenceEquals(_variables, Context.Variables))
        {
            _completions = Complete(true);
            _selected = Math.Min(_selected, Math.Max(0, _completions.Count - 1));
        }
        _variables = Context.Variables;
        RefreshPropertyAction();
    }

    /// <inheritdoc />
    protected override Task OnParametersSetAsync() => AnalyzeAsync();

    private async Task AnalyzeAsync(bool immediate = false)
    {
        if (_disposed)
        {
            return;
        }
        if (_composing)
        {
            if (_scheduledGeneration != -1)
            {
                CancelAnalysis();
            }
            return;
        }
        var expression = FeelActive ? Expression : null;
        if (_diagnosticGeneration == _generation && _diagnosticFeelActive == FeelActive && _parsedText == expression)
        {
            if (_scheduledGeneration != -1)
            {
                CancelAnalysis();
            }
            return;
        }
        var deferInitial = Options.DeferInitialDiagnostics && _diagnosticGeneration != _generation;
        if (!immediate && deferInitial && _renderedGeneration != _generation)
        {
            return;
        }
        if (!immediate && (deferInitial || _diagnosticGeneration == _generation && Options.DiagnosticsDelay > TimeSpan.Zero))
        {
            if (_scheduledGeneration != _generation || _scheduledExpression != expression)
            {
                CancelAnalysis();
                _scheduledGeneration = _generation;
                _scheduledExpression = expression;
                _analysisVersion++;
                _ = AnalyzeAfterIdleAsync(_generation, _analysisCancellation.Token);
            }
            return;
        }
        CancelAnalysis();
        _diagnosticGeneration = _generation;
        _diagnosticFeelActive = FeelActive;
        _parsedText = expression;
        _parseError = expression is null ? null : _syntax.Validate(expression);
        Diagnostics = _parseError is null ? [] : [new("FEEL.Syntax", _parseError, "FeelSharp 1.3.0")];
        var update = new BpmnFeelSharpDiagnosticsUpdate(Context.ElementId, Entry.Id, Context.Revision, _generation, Diagnostics);
        var version = ++_analysisVersion;
        await DiagnosticsChanged.InvokeAsync(update);
        if (_disposed || update.Generation != _generation || version != _analysisVersion)
        {
            return;
        }
        if (Services.GetService(typeof(IOptions<BpmnFeelSharpEditorOptions>)) is IOptions<BpmnFeelSharpEditorOptions> options &&
            options.Value.DiagnosticsObserver is { } observer)
        {
            await observer(update);
        }
    }

    private async Task AnalyzeAfterIdleAsync(int generation, CancellationToken token)
    {
        try
        {
            var delay = Options.DiagnosticsDelay;
            await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(1), token);
            await InvokeAsync(async () =>
            {
                if (!_disposed && !token.IsCancellationRequested && generation == _generation)
                {
                    await AnalyzeAsync(true);
                    if (!_disposed && generation == _generation)
                    {
                        StateHasChanged();
                    }
                }
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Replaced drafts and disposal cancel delayed publication.
        }
        catch (Exception exception)
        {
            await DispatchExceptionAsync(exception);
        }
    }

    private void CancelAnalysis()
    {
        _analysisCancellation.Cancel();
        _analysisCancellation.Dispose();
        _analysisCancellation = new();
        _scheduledGeneration = -1;
    }

    private void ResetInput()
    {
        _expanded = false;
        CancelAnalysis();
        _generation++;
        _draftIdentity = new();
        _sequence = 0;
        _enhanced = false;
        _propertyEnhancement = null;
        _composing = false;
        _completions = [];
        if (Options.DeferInitialDiagnostics)
        {
            _parseError = null;
            Diagnostics = [];
        }
        _initialText = PrefixInput || PropertyInput && !FeelActive ? _value ?? "" : DisplayValue;
    }

    private Task ScheduleInitialAnalysisAsync()
    {
        _renderedGeneration = _generation;
        return Options.DeferInitialDiagnostics && _diagnosticGeneration != _generation
            ? AnalyzeAsync() : Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed || _initializing)
        {
            return;
        }
        if (_unavailable)
        {
            await ScheduleInitialAnalysisAsync();
            return;
        }
        if (ReadOnly || !FeelActive && !PropertyInput)
        {
            await ReleaseBridgeAsync();
            await ScheduleInitialAnalysisAsync();
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
            if (!_disposed && generation == _generation)
            {
                await ScheduleInitialAnalysisAsync();
            }
            if (!_disposed && generation != _generation)
            {
                StateHasChanged();
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "FeelSharp editor DOM enhancement unavailable; using native text editing.")]
    private static partial void LogEnhancementUnavailable(ILogger logger, Exception exception);

    internal async Task ToggleAsync()
    {
        if (ReadOnly)
        {
            return;
        }
        var generation = _generation;
        var wasFeel = FeelActive;
        var draft = PropertyInput ? await CaptureAsync() : new BpmnFeelSharpDraft(Expression, _selectionStart, _selectionEnd);
        if (_disposed || ReadOnly || generation != _generation) return;
        _value = wasFeel ? draft.Text : "=" + draft.Text;
        ResetInput();
        await SubmitAsync(_value);
        await AnalyzeAsync(true);
        RefreshPropertyAction();
    }

    internal EventCallback<ChangeEventArgs> FallbackInput(int generation) => _enhanced ? default :
        EventCallback.Factory.Create<ChangeEventArgs>(this, args => InputAsync(args, generation));
    internal EventCallback<ChangeEventArgs> FallbackChange(int generation) => _enhanced ? default :
        EventCallback.Factory.Create<ChangeEventArgs>(this, args => ChangeAsync(args, generation));
    internal EventCallback<KeyboardEventArgs> FallbackKeyDown(int generation) => _enhanced ? default :
        EventCallback.Factory.Create<KeyboardEventArgs>(this, args => KeyDownAsync(args, generation));

    internal async Task InputAsync(ChangeEventArgs args, int generation)
    {
        if (!_disposed && generation == _generation && !ReadOnly && !_expanded && !_enhanced)
        {
            _value = "=" + (args.Value as string ?? "");
            await AnalyzeAsync();
            if (!_disposed && generation == _generation)
            {
                await DraftChanged.InvokeAsync(new(Expression, _selectionStart, _selectionEnd));
            }
        }
    }

    internal Task ChangeAsync(ChangeEventArgs args, int generation) =>
        generation != _generation || _enhanced ? Task.CompletedTask : CommitAsync(args.Value as string ?? "");

    internal Task KeyDownAsync(KeyboardEventArgs args, int generation) =>
        generation == _generation && !_enhanced && args.Key == "Enter" && (args.CtrlKey || args.MetaKey)
            ? CommitAsync(Expression) : Task.CompletedTask;

    private async Task SubmitLiteralAsync(string? value, int generation)
    {
        if (_disposed || generation != _generation || ReadOnly)
        {
            return;
        }
        _value = value;
        await SubmitAsync(value);
        await AnalyzeAsync();
    }

    private async Task CommitAsync(string expression)
    {
        if (_disposed || ReadOnly || _expanded || !FeelActive || _composing)
        {
            return;
        }
        _completions = [];
        _value = string.IsNullOrEmpty(expression) ? null : "=" + expression;
        await SubmitAsync(_value);
        await AnalyzeAsync(true);
    }

    /// <summary>Receives a native UTF-16 selection snapshot, guarded against old DOM instances and callbacks.</summary>
    [JSInvokable]
    public Task SnapshotAsync(string text, int start, int end, long sequence, int generation, bool composing,
        bool focused) => InvokeAsync(async () =>
    {
        if (_disposed || ReadOnly || _expanded || !FeelActive || generation != _generation || sequence <= _sequence ||
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
        _completions = composing || !focused || !Options.AutomaticCompletion ? [] : Complete();
        await AnalyzeAsync();
        if (!_disposed && generation == _generation)
        {
            if (!composing)
            {
                await DraftChanged.InvokeAsync(new(text, start, end));
            }
            if (!_disposed && generation == _generation)
            {
                StateHasChanged();
            }
        }
    });

    /// <summary>Handles keys after the bridge has synchronized the native text and caret.</summary>
    [JSInvokable]
    public Task NativeKeyAsync(string key, long sequence, int generation) => InvokeAsync(async () =>
    {
        if (_disposed || ReadOnly || !FeelActive || generation != _generation || sequence != _sequence || _composing)
        {
            return;
        }
        if (key == "commit")
        {
            await CommitAsync(Expression);
        }
        else if (key == "complete")
        {
            _selected = 0;
            _completions = Complete(true);
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
            else if (key == "Enter" && Options.AcceptEnter || key == "Tab" && Options.AcceptTab)
            {
                await ApplyCompletionAsync(_selected);
            }
        }
        if (!_disposed && generation == _generation)
        {
            StateHasChanged();
        }
    });

    internal async Task RequestCompletionAsync()
    {
        if (!_disposed && !ReadOnly && _enhanced && !_composing && _bridge is not null)
        {
            await _bridge.InvokeVoidAsync("complete", _generation);
        }
    }

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
            sequence, generation, completion.PlaceholderStart, completion.PlaceholderLength);
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
    public virtual async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        EditorSession?.ReleasePropertyAction(this);
        _analysisCancellation.Cancel();
        _analysisCancellation.Dispose();
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
