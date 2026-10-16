namespace Hasseware.AspNetCore.Components.Bpmn.Validation;

/// <summary>Host policy for Script Tasks, applied by <see cref="BpmnScriptTaskProfile"/>.</summary>
/// <remarks>
/// Policy, like the profile itself, can only add findings: nothing here makes a core error go away.
/// </remarks>
public sealed class BpmnScriptOptions
{
    /// <summary>
    /// The <c>scriptFormat</c> names the host's engine runs, compared ignoring case. Null accepts
    /// any format; a format not in the set is an error.
    /// </summary>
    public ISet<string>? AllowedFormats { get; set; }

    /// <summary>The longest script body accepted, in characters. Zero or less turns the limit off.</summary>
    public int MaxScriptLength { get; set; } = 64 * 1024;

    /// <summary>
    /// The deepest bracket nesting accepted, counting <c>(</c>, <c>[</c> and <c>{</c> alike. Zero or
    /// less turns the limit off.
    /// </summary>
    /// <remarks>
    /// A parser's work and stack use grow with nesting, not with length: a few thousand open
    /// parentheses overflow a Lua parser's stack and keep Roslyn busy for seconds. Real scripts nest a
    /// handful of levels. The count skips quoted strings but not comments or long strings, so it is a cheap
    /// pre-check and not a boundary; on a server, long scripts are also parsed on a thread with a large
    /// stack. A browser has no such thread, so there this limit and <see cref="MaxScriptLength"/> are
    /// the whole defence.
    /// </remarks>
    public int MaxNestingDepth { get; set; } = 100;

    /// <summary>How a Script Task with no <c>scriptFormat</c> is reported.</summary>
    public BpmnSeverity MissingFormatSeverity { get; set; } = BpmnSeverity.Warning;

    /// <summary>How a Script Task with no script is reported.</summary>
    public BpmnSeverity EmptyScriptSeverity { get; set; } = BpmnSeverity.Warning;

    /// <summary>How a format no registered validator handles is reported.</summary>
    public BpmnSeverity UnvalidatedFormatSeverity { get; set; } = BpmnSeverity.Info;

    /// <summary>
    /// How a language validator that throws is reported. A host that would rather not save a document
    /// it could not check raises this to <see cref="BpmnSeverity.Error"/>.
    /// </summary>
    public BpmnSeverity ValidatorFailureSeverity { get; set; } = BpmnSeverity.Warning;
}
