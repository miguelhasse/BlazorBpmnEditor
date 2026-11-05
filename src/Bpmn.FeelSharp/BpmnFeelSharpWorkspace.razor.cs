using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Transactional expanded/standalone workspace. Only Apply persists its draft.</summary>
public partial class BpmnFeelSharpWorkspace : IAsyncDisposable
{
    internal BpmnFeelSharpField? _field;
    private string? _source;
    private string? _key;
    private int _revision;
    private bool _initialized;
    internal int _generation;
    internal string? _initial;
    private string? _draft;
    private string _sample = "{}";
    internal string _search = "";
    internal string _category = "all";
    internal string _tab = "Editor";
    internal readonly HashSet<string> _visited = ["Editor"];
    private readonly string _tabPrefix = "feel-tabs-" + Guid.NewGuid().ToString("N");
    internal static readonly string[] Tabs = ["Editor", "Reference", "Variables", "Evaluation"];
    private IReadOnlyList<BpmnFeelVariable> _declaredVariables = [];
    private IReadOnlyList<BpmnFeelVariable> _allVariables = [];
    internal static readonly string[] Categories = ["all", "number", "string", "temporal", "list", "context", "host"];
    private IReadOnlyList<BpmnFeelVariable> _sampleVariables = [];
    internal string? _result;
    internal string? _resultType;
    private BpmnFeelSharpDraft? _resultDraft;
    internal string? _message;
    internal string? _proposal;
    private BpmnFeelSharpDraft? _proposalBase;
    internal bool _busy;
    private bool _disposed;
    private IBpmnFeelSharpEvaluation? _authorizedEvaluator;
    private BpmnFeelSharpHostActions? _authorizedActions;
    private CancellationTokenSource _request = new();

    [Inject] private IStringLocalizer<Resources.BpmnStrings> Localizer { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IOptions<BpmnFeelSharpEditorOptions> Options { get; set; } = default!;
    [Inject] private ILogger<BpmnFeelSharpWorkspace> Logger { get; set; } = default!;
    /// <summary>Initial prefix-free expression, not a live persisted binding.</summary>
    [Parameter] public string? Value { get; set; }
    /// <summary>Explicit Apply callback. Invalid syntax remains advisory.</summary>
    [Parameter] public EventCallback<string?> Applied { get; set; }
    /// <summary>Cancel callback; no value is persisted.</summary>
    [Parameter] public EventCallback Cancelled { get; set; }
    /// <summary>Element/property identity used to invalidate asynchronous work.</summary>
    [Parameter] public string? ContextKey { get; set; }
    /// <summary>Reset revision, including resets to the same value.</summary>
    [Parameter] public int Revision { get; set; }
    /// <summary>Accessible label.</summary>
    [Parameter] public string Label { get; set; } = "FEEL";
    /// <summary>Prevent edits and Apply.</summary>
    [Parameter] public bool ReadOnly { get; set; }
    /// <summary>Explicit variable declarations.</summary>
    [Parameter] public IReadOnlyList<BpmnFeelVariable> Variables { get; set; } = [];
    /// <summary>Explicit trusted evaluator. Null disables Run; DI registration alone does not authorize this workspace.</summary>
    [Parameter] public IBpmnFeelSharpEvaluation? Evaluator { get; set; }
    /// <summary>Explicit host-owned actions, never invoked automatically.</summary>
    [Parameter] public BpmnFeelSharpHostActions? Actions { get; set; }
    /// <summary>Native or Fluent shared property renderer.</summary>
    [Parameter] public Type EditorType { get; set; } = typeof(BpmnFeelSharpCodeEditor);
    /// <summary>Show local Apply/Cancel actions; dialog hosts provide their own standard footer.</summary>
    [Parameter] public bool ShowCommitActions { get; set; } = true;
    /// <summary>Use the clean editor presentation inside a standard manager dialog.</summary>
    [Parameter] public bool MinimalEditor { get; set; }
    /// <summary>Optional UI-kit presentation over the shared guarded workspace state.</summary>
    [Parameter] public RenderFragment<BpmnFeelSharpWorkspace>? ContentTemplate { get; set; }

    internal string TabId(string tab) => _tabPrefix + "-" + tab;
    internal string PanelId(string tab) => TabId(tab) + "-panel";
    internal void SelectTab(string tab)
    {
        _tab = tab;
        _visited.Add(tab);
    }
    internal async Task TabKeyAsync(KeyboardEventArgs args, string tab)
    {
        var index = Array.IndexOf(Tabs, tab);
        var next = args.Key switch
        {
            "ArrowRight" => (index + 1) % Tabs.Length,
            "ArrowLeft" => (index + Tabs.Length - 1) % Tabs.Length,
            "Home" => 0,
            "End" => Tabs.Length - 1,
            _ => -1,
        };
        if (next < 0) return;
        SelectTab(Tabs[next]);
        var module = await JS.InvokeAsync<IJSObjectReference>("import",
            "./_content/Hasseware.AspNetCore.Components.Bpmn.FeelSharp/feelsharp-editor.js");
        try { await module.InvokeVoidAsync("focusTab", TabId(Tabs[next])); }
        finally { await module.DisposeAsync(); }
    }

    internal string Text(string key) => Localizer[key];
    internal string Sample
    {
        get => _sample;
        set
        {
            if (_sample != value)
            {
                _sample = value;
                _sampleVariables = [];
                RefreshVariables();
                Invalidate();
            }
        }
    }
    internal IReadOnlyList<BpmnFeelVariable> AllVariables => _allVariables;

    private void RefreshVariables() => _allVariables = _declaredVariables.Concat(_sampleVariables)
        .DistinctBy(variable => variable.Name, StringComparer.Ordinal).ToArray();
    internal IEnumerable<FeelSharpFunction> Reference => FeelSharpFunctionCatalog.Functions.Where(function =>
        function.Name.Contains(_search, StringComparison.OrdinalIgnoreCase) &&
        (_category == "all" || function.Category == _category));
    internal IEnumerable<BpmnFeelSharpFunction> HostFunctions => Options.Value.Functions.Where(function =>
        function.Name.Contains(_search, StringComparison.OrdinalIgnoreCase));
    internal string VariableText => string.Join(Environment.NewLine, Describe(AllVariables, ""));

    private static IEnumerable<string> Describe(IReadOnlyList<BpmnFeelVariable> variables, string prefix)
    {
        foreach (var variable in variables)
        {
            yield return $"{prefix}{variable.Name}: {variable.Detail ?? "unknown"} ({variable.Source ?? "host"})";
            foreach (var line in Describe(variable.Entries, prefix + variable.Name + "."))
            {
                yield return line;
            }
        }
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (!_declaredVariables.SequenceEqual(Variables))
        {
            _declaredVariables = Variables.ToArray();
            RefreshVariables();
        }
        if (!ReferenceEquals(_authorizedEvaluator, Evaluator) || !ReferenceEquals(_authorizedActions, Actions))
        {
            _authorizedEvaluator = Evaluator;
            _authorizedActions = Actions;
            Invalidate();
        }
        if (!_initialized || Value != _source || ContextKey != _key || Revision != _revision)
        {
            _source = Value;
            _key = ContextKey;
            _revision = Revision;
            _initialized = true;
            _initial = _draft = Value;
            _sample = "{}";
            _sampleVariables = [];
            RefreshVariables();
            _generation++;
            Invalidate();
        }
        if (ReadOnly)
        {
            Invalidate();
        }
    }

    private void Invalidate()
    {
        _request.Cancel();
        _request.Dispose();
        _request = new();
        _proposal = _result = _resultType = _message = null;
        _proposalBase = null;
        _resultDraft = null;
        _busy = false;
    }

    internal void CommitDraft(string? value) => _draft = value;
    // Draft notifications update private state; only clearing a visible result needs a workspace render.
    internal EventCallback<BpmnFeelSharpDraft> DraftCallback => new(null, (Func<BpmnFeelSharpDraft, Task>)DraftAsync);
    internal Task DraftAsync(BpmnFeelSharpDraft draft)
    {
        if (_draft != draft.Text)
        {
            _draft = draft.Text;
            var hadResult = _result is not null;
            _result = _resultType = null;
            _resultDraft = null;
            if (hadResult) StateHasChanged();
        }
        return Task.CompletedTask;
    }

    private BpmnFeelSharpField Field => _field ?? throw new InvalidOperationException("The FEEL workspace has not rendered.");
    /// <summary>Captures and applies the current draft; advisory diagnostics do not block persistence.</summary>
    public async Task ApplyAsync()
    {
        var generation = _generation;
        var draft = await Field.CaptureAsync();
        if (_disposed || ReadOnly || generation != _generation)
        {
            throw new InvalidOperationException("The FEEL workspace changed before Apply.");
        }
        await Applied.InvokeAsync(string.IsNullOrEmpty(draft.Text) ? null : draft.Text);
    }
    internal async Task CancelAsync()
    {
        Invalidate();
        _initial = _draft = Value;
        _generation++;
        await Cancelled.InvokeAsync();
    }
    internal Task InferAsync()
    {
        _sampleVariables = [];
        try
        {
            _sampleVariables = FeelSharpSampleVariables.FromJson(_sample);
            _message = null;
        }
        catch (JsonException exception)
        {
            _message = exception.Message;
        }
        RefreshVariables();
        return Task.CompletedTask;
    }

    internal async Task RunAsync()
    {
        var evaluator = Evaluator;
        if (evaluator is null || _busy || _disposed)
        {
            return;
        }
        SelectTab("Evaluation");
        var generation = _generation;
        var token = _request.Token;
        _busy = true;
        _result = _resultType = _message = null;
        try
        {
            var draft = await Field.CaptureAsync();
            if (token.IsCancellationRequested || _disposed || generation != _generation)
            {
                return;
            }
            var context = FeelSharpJsonContext.Read(_sample);
            await Task.Yield();
            if (token.IsCancellationRequested || _disposed || generation != _generation)
            {
                return;
            }
            var result = evaluator.Evaluate(draft.Text, context, token);
            if (token.IsCancellationRequested || _disposed || generation != _generation)
            {
                return;
            }
            var current = await Field.CaptureAsync();
            if (token.IsCancellationRequested || _disposed || generation != _generation || current.Text != draft.Text ||
                !ReferenceEquals(current.SourceIdentity, draft.SourceIdentity))
            {
                return;
            }
            if (result.IsSuccess)
            {
                _resultType = result.Value?.GetType().Name ?? "null";
                _result = FeelSharpResultJson.Serialize(result.Value?.ToObject());
                _resultDraft = current;
            }
            else
            {
                _message = result.Error;
            }
        }
        catch (JsonException exception)
        {
            _message = exception.Message;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Context replacement cancels publication, not synchronous interpreter execution.
        }
        catch (InvalidOperationException) when (token.IsCancellationRequested || _disposed || generation != _generation)
        {
            // The field may have been replaced during the final source check.
        }
        finally
        {
            if (!_disposed && !token.IsCancellationRequested && generation == _generation)
            {
                _busy = false;
            }
        }
    }

    internal Task SuggestAsync() => ProposalAsync(Actions?.Suggest);
    internal Task CollaborateAsync() => ProposalAsync(Actions?.Collaborate);
    private async Task ProposalAsync(Func<BpmnFeelSharpDraft, CancellationToken, Task<string>>? action)
    {
        if (action is null || ReadOnly || _busy || _disposed)
        {
            return;
        }
        var generation = _generation;
        var token = _request.Token;
        _busy = true;
        try
        {
            var draft = await Field.CaptureAsync();
            if (token.IsCancellationRequested || ReadOnly || _disposed || generation != _generation)
            {
                return;
            }
            var proposal = await action(draft, token);
            if (!_disposed && !ReadOnly && generation == _generation && !token.IsCancellationRequested)
            {
                _proposalBase = draft;
                _proposal = proposal;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A stale proposal cannot affect the current workspace.
        }
        catch (InvalidOperationException) when (token.IsCancellationRequested || _disposed || generation != _generation)
        {
            // Native capture may finish after its field was replaced.
        }
        finally
        {
            if (!_disposed && !token.IsCancellationRequested && generation == _generation)
            {
                _busy = false;
            }
        }
    }
    internal async Task AcceptProposalAsync()
    {
        if (_proposalBase is not null && _proposal is not null && !ReadOnly)
        {
            try
            {
                await Field.ApplyDraftAsync(_proposalBase, _proposal);
            }
            catch (InvalidOperationException)
            {
                _message = Text("Feel.StaleProposal");
            }
            _proposal = null;
            _proposalBase = null;
        }
    }
    internal async Task ShareAsync()
    {
        if (Actions?.Share is { } share)
        {
            var token = _request.Token;
            var draft = await Field.CaptureAsync();
            token.ThrowIfCancellationRequested();
            await share(draft, token);
        }
    }
    private async Task IndentAsync()
    {
        var draft = await Field.CaptureAsync();
        var start = draft.Start == 0 ? -1 : draft.Text.LastIndexOf('\n', draft.Start - 1);
        start = start < 0 ? 0 : start + 1;
        if (FeelSharpLanguage.Tokenize(draft.Text).Any(token => token.Kind is "string" or "comment" or "temporal" &&
            token.Text.Contains('\n', StringComparison.Ordinal) && token.Offset < draft.End && token.End > start))
        {
            _message = Text("Feel.NoSafeFix");
            return;
        }
        var selected = draft.Text[start..draft.End];
        var replacement = draft.Text[..start] + "  " + selected.Replace("\n", "\n  ", StringComparison.Ordinal) + draft.Text[draft.End..];
        await Field.ApplyDraftAsync(draft, replacement);
    }
    private async Task DelimiterAsync()
    {
        var draft = await Field.CaptureAsync();
        var stack = new Stack<char>();
        foreach (var token in FeelSharpLanguage.Tokenize(draft.Text))
        {
            if (token.Kind is "string" or "comment" or "temporal")
            {
                continue;
            }
            foreach (var character in token.Text)
            {
                if (character is '(' or '[' or '{')
                {
                    stack.Push(character);
                }
                else if (character is ')' or ']' or '}')
                {
                    if (stack.Count == 0 || !FeelSharpAuthoring.Matches(stack.Pop(), character))
                    {
                        _message = Text("Feel.NoSafeFix");
                        return;
                    }
                }
            }
        }
        if (stack.Count == 0)
        {
            _message = Text("Feel.NoSafeFix");
            return;
        }
        var closing = string.Concat(stack.Select(open => open == '(' ? ')' : open == '[' ? ']' : '}'));
        var replacement = draft.Text + closing;
        if (new FeelSharpSyntax().Validate(replacement) is not null)
        {
            _message = Text("Feel.NoSafeFix");
            return;
        }
        await Field.ApplyDraftAsync(draft, replacement);
    }
    internal async Task CopyAsync()
    {
        if (_result is not null && _resultDraft is not null && !_disposed)
        {
            var result = _result;
            var expected = _resultDraft;
            var token = _request.Token;
            var current = await Field.CaptureAsync();
            if (token.IsCancellationRequested || _disposed || current.Text != expected.Text ||
                !ReferenceEquals(current.SourceIdentity, expected.SourceIdentity))
            {
                _message = Text("Feel.StaleResult");
                return;
            }
            try
            {
                await JS.InvokeVoidAsync("navigator.clipboard.writeText", result);
            }
            catch (JSException exception)
            {
                LogClipboardFailure(Logger, exception);
                _message = Text("Feel.CopyFailed");
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not copy the FEEL result to the clipboard.")]
    private static partial void LogClipboardFailure(ILogger logger, Exception exception);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _request.Cancel();
        _request.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
