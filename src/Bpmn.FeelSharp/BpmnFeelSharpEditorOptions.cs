namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Shared options for native and Fluent FeelSharp editors, including registry-created editors.</summary>
public sealed class BpmnFeelSharpEditorOptions
{
    /// <summary>Whether the manager can author an expression from a literal property.</summary>
    public BpmnFeelSharpLiteralManagerBehavior LiteralManagerBehavior { get; set; }

    private TimeSpan _diagnosticsDelay;
    /// <summary>Show transactional expanded editing action. Disabled by default.</summary>
    public bool ExpandedEditor { get; set; }

    /// <summary>Evaluator explicitly authorized for expanded editors. Null keeps Run unavailable.</summary>
    public IBpmnFeelSharpEvaluation? WorkspaceEvaluator { get; set; }

    /// <summary>Explicit host-owned workspace actions.</summary>
    public BpmnFeelSharpHostActions? WorkspaceActions { get; set; }

    /// <summary>Prefer variables before functions and structural keywords. Default preserves alphabetical order.</summary>
    public bool RankCompletions { get; set; }

    /// <summary>Accept suggestions with Enter. Default preserves existing behavior.</summary>
    public bool AcceptEnter { get; set; } = true;

    /// <summary>Accept suggestions with Tab. Disable to retain normal focus traversal while suggestions are open.</summary>
    public bool AcceptTab { get; set; } = true;

    /// <summary>Show suggestions automatically; explicit requests remain available when disabled.</summary>
    public bool AutomaticCompletion { get; set; } = true;

    /// <summary>Offer structural snippets. Disabled by default.</summary>
    public bool Snippets { get; set; }

    /// <summary>Infer expression-local bindings using conservative lexical analysis. Disabled by default.</summary>
    public bool LocalVariables { get; set; }

    /// <summary>Display active-call parameter guidance. Disabled by default.</summary>
    public bool SignatureHelp { get; set; }

    /// <summary>Defer initial/reset syntax checks until after DOM attachment, then the idle delay.
    /// Disabled by default; commits always flush analysis.</summary>
    public bool DeferInitialDiagnostics { get; set; }

    /// <summary>Optional idle diagnostic delay. Zero preserves immediate analysis; commits always flush analysis.</summary>
    public TimeSpan DiagnosticsDelay
    {
        get => _diagnosticsDelay;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, TimeSpan.FromMilliseconds(4294967294));
            _diagnosticsDelay = value;
        }
    }

    /// <summary>Opt-in editor density; native input and highlighting share metrics.</summary>
    public BpmnFeelSharpDensity Density { get; set; }

    /// <summary>Trusted host function metadata; does not register executable functions.</summary>
    public IReadOnlyList<BpmnFeelSharpFunction> Functions { get; set; } = [];

    /// <summary>Optional asynchronous diagnostics observer. Exceptions propagate to the host.</summary>
    /// <remarks>Do not capture per-user mutable state in application-wide options.
    /// Messages may contain fragments of expressions; do not forward them externally without host authorization.</remarks>
    public Func<BpmnFeelSharpDiagnosticsUpdate, Task>? DiagnosticsObserver { get; set; }
}
