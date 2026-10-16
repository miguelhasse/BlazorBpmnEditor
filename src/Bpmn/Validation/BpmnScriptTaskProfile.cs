using System.Runtime.ExceptionServices;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Microsoft.Extensions.Options;

namespace Hasseware.AspNetCore.Components.Bpmn.Validation;

/// <summary>
/// Engine-neutral Script Task rules (<c>PROF-111</c> through <c>PROF-118</c>), delegating syntax to
/// whichever <see cref="IBpmnScriptValidator"/> owns the task's <c>scriptFormat</c>.
/// </summary>
/// <remarks>
/// The profile asks what a task says, never what the script does: it reads the format and the body,
/// applies the host's <see cref="BpmnScriptOptions"/>, and hands the body to a parser. Nothing here
/// runs a script, which matters because the body is code from whoever wrote the document.
/// </remarks>
public sealed class BpmnScriptTaskProfile : IBpmnProfileValidator
{
    /// <summary>
    /// Stack reserved for a parse, which is address space and not memory until it is used.
    /// </summary>
    /// <remarks>
    /// A script may nest as deeply as its length allows, and a recursive-descent parser spends a stack
    /// frame or two per level. Ten thousand open parentheses, well inside the length limit, overflow
    /// Roslyn's parser on an ordinary thread, and a stack overflow cannot be caught: it ends the host
    /// process. The script is untrusted, so its parser gets room for the deepest script the limit lets
    /// through rather than the host's own stack.
    /// </remarks>
    private const int ParserStackBytes = 128 * 1024 * 1024;

    /// <summary>What a host short of address space is offered when the full reservation cannot be made.</summary>
    private const int FallbackParserStackBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Scripts this short are parsed on the caller's thread: even a chain of operators cannot nest
    /// deeply enough in a thousand characters to threaten an ordinary stack, and almost every real
    /// script is shorter, so the thread is paid for only by the scripts that could need it.
    /// </summary>
    private const int InlineScriptLength = 1000;

    private readonly Dictionary<string, IBpmnScriptValidator> _validators = new(StringComparer.OrdinalIgnoreCase);
    private readonly BpmnScriptOptions _options;

    /// <summary>Creates the profile.</summary>
    /// <param name="validators">The registered language validators.</param>
    /// <param name="options">Host policy.</param>
    public BpmnScriptTaskProfile(IEnumerable<IBpmnScriptValidator> validators, IOptions<BpmnScriptOptions> options)
    {
        ArgumentNullException.ThrowIfNull(validators);
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;

        // First registration wins, so a host's own validator for a format registered ahead of a
        // package's default takes precedence without the package having to know about it.
        foreach (var validator in validators)
        {
            foreach (var format in validator.Formats) _validators.TryAdd(format.Trim(), validator);
        }
    }

    /// <inheritdoc />
    public string Name => "script-task";

    /// <inheritdoc />
    public IEnumerable<BpmnProfileDiagnostic> Validate(BpmnDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var node in document.FlowNodes)
        {
            if (node.Type != BpmnFlowNodeType.ScriptTask) continue;

            foreach (var diagnostic in Check(node)) yield return diagnostic;
        }
    }

    private IEnumerable<BpmnProfileDiagnostic> Check(BpmnFlowNode node)
    {
        var format = node.ScriptFormat?.Trim();
        var script = node.Script;
        var label = node.Name is { Length: > 0 } name ? $"'{name}'" : $"'{node.Id}'";
        var allowed = true;

        if (string.IsNullOrEmpty(format))
        {
            yield return From("PROF-111", _options.MissingFormatSeverity, node,
                BpmnDiagnosticText.Create("PROF-111", "Script Task {0} has no scriptFormat, so no engine knows how to run it.", label));
        }
        else if (_options.AllowedFormats is { } allowedFormats
            && !allowedFormats.Contains(format, StringComparer.OrdinalIgnoreCase))
        {
            allowed = false;
            yield return From("PROF-112", BpmnSeverity.Error, node,
                BpmnDiagnosticText.Create("PROF-112", "Script Task {0} uses scriptFormat '{1}', which the engine does not run.", label, format));
        }

        if (string.IsNullOrWhiteSpace(script))
        {
            yield return From("PROF-113", _options.EmptyScriptSeverity, node,
                BpmnDiagnosticText.Create("PROF-113", "Script Task {0} has no script.", label));
            yield break;
        }

        if (_options.MaxScriptLength > 0 && script.Length > _options.MaxScriptLength)
        {
            // Not parsed: the limit is there so a document cannot make validation do unbounded work.
            yield return From("PROF-114", BpmnSeverity.Error, node,
                BpmnDiagnosticText.Create("PROF-114", "Script Task {0} has a script of {1} characters; the limit is {2}.",
                    label, script.Length, _options.MaxScriptLength));
            yield break;
        }

        if (_options.MaxNestingDepth > 0 && NestingDepth(script, _options.MaxNestingDepth) > _options.MaxNestingDepth)
        {
            yield return From("PROF-118", BpmnSeverity.Error, node,
                BpmnDiagnosticText.Create("PROF-118", "Script Task {0} nests brackets deeper than {1} levels.",
                    label, _options.MaxNestingDepth));
            yield break;
        }

        // A format the engine does not run has already been rejected; saying that no validator
        // exists for it as well would be the same finding twice.
        if (string.IsNullOrEmpty(format) || !allowed) yield break;

        if (!_validators.TryGetValue(format, out var validator))
        {
            yield return From("PROF-115", _options.UnvalidatedFormatSeverity, node,
                BpmnDiagnosticText.Create("PROF-115", "No syntax validator is registered for scriptFormat '{0}' (Script Task {1}).", format, label));
            yield break;
        }

        IReadOnlyList<BpmnScriptDiagnostic> found;
        BpmnDiagnosticText? failure = null;

        try
        {
            found = ValidateOnLargeStack(validator, format, script, new BpmnScriptContext(node.Id, node.Name));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            // One misbehaving language backend must not hide the findings for every other task.
            found = [];
            failure = BpmnDiagnosticText.Create("PROF-117", "The '{0}' script validator failed on Script Task {1}: {2}.", format, label, error.Message);
        }

        if (failure is not null) yield return From("PROF-117", _options.ValidatorFailureSeverity, node, failure);

        foreach (var diagnostic in found)
        {
            var (line, column) = Locate(script, diagnostic.Offset);
            yield return From("PROF-116", diagnostic.Severity, node,
                BpmnDiagnosticText.Create("PROF-116", "Script Task {0}, line {1}, column {2}: {3}", label, line, column, diagnostic.Message));
        }
    }

    private static IReadOnlyList<BpmnScriptDiagnostic> ValidateOnLargeStack(
        IBpmnScriptValidator validator,
        string format,
        string script,
        BpmnScriptContext context)
    {
        // A browser has one thread, so there is no larger stack to move to; there the bracket limit
        // and the length limit are the whole defence. A short script cannot nest deeply enough to need one.
        if (OperatingSystem.IsBrowser() || script.Length <= InlineScriptLength)
        {
            return validator.Validate(format, script, context);
        }

        IReadOnlyList<BpmnScriptDiagnostic>? result = null;
        Exception? failure = null;

        void Parse()
        {
            try
            {
                result = validator.Validate(format, script, context);
            }
            catch (Exception error)
            {
                failure = error;
            }
        }

        // A host short of address space or threads cannot always grant the full reservation. A smaller
        // stack still beats none; what is never done is parsing a long script on the caller's own
        // stack, because that is exactly the overflow this exists to prevent.
        foreach (var stackBytes in new[] { ParserStackBytes, FallbackParserStackBytes })
        {
            var thread = new Thread(Parse, stackBytes) { IsBackground = true };

            try
            {
                thread.Start();
            }
            catch (Exception error) when (error is OutOfMemoryException or ThreadStartException)
            {
                continue;
            }

            thread.Join();

            if (failure is not null) ExceptionDispatchInfo.Throw(failure);
            return result ?? [];
        }

        throw new InvalidOperationException("No thread with room to parse this script could be started.");
    }

    private static BpmnProfileDiagnostic From(string ruleId, BpmnSeverity severity, BpmnFlowNode node, BpmnDiagnosticText text) =>
        new(ruleId, severity, text.Message, node.Id)
        {
            MessageResourceKey = text.ResourceKey,
            MessageArguments = text.Arguments,
        };

    /// <summary>The deepest bracket nesting in a script, or one past the limit as soon as it is exceeded.</summary>
    /// <remarks>
    /// Counted two ways, and the deeper count wins. Counting every bracket, strings included, cannot
    /// be hidden from by a stray quote, but a string of closers can cancel real openers. Skipping
    /// quoted strings (a quote ends at the line's end) cannot be offset by closers in a string, but
    /// one stray apostrophe, say in a regex or a comment, hides the rest of its line. Defeating both
    /// at once takes a script built for it. This is a cheap pre-check, not a boundary: the parser's
    /// own large stack is what survives a nesting it misjudges.
    /// </remarks>
    internal static int NestingDepth(string source, int limit)
    {
        int rawDepth = 0, rawDeepest = 0, awareDepth = 0, awareDeepest = 0;
        var quote = '\0';

        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            var opens = character is '(' or '[' or '{';
            var closes = character is ')' or ']' or '}';

            if (opens && ++rawDepth > rawDeepest) rawDeepest = rawDepth;
            else if (closes && rawDepth > 0) rawDepth--;

            if (quote != '\0')
            {
                if (character == '\\') index++;
                else if (character == quote || character is '\n' or '\r') quote = '\0';
            }
            else if (character is '"' or '\'')
            {
                quote = character;
            }
            else if (opens && ++awareDepth > awareDeepest)
            {
                awareDeepest = awareDepth;
            }
            else if (closes && awareDepth > 0)
            {
                awareDepth--;
            }

            if (Math.Max(rawDeepest, awareDeepest) > limit) break;
        }

        return Math.Max(rawDeepest, awareDeepest);
    }

    /// <summary>The 1-based line and column of an offset, clamped into the script.</summary>
    internal static (int Line, int Column) Locate(string source, int offset)
    {
        var end = Math.Clamp(offset, 0, source.Length);
        var line = 1;
        var lineStart = 0;

        for (var index = 0; index < end; index++)
        {
            // CRLF is one break, and a lone CR is a break too, as it is to every parser here.
            if (source[index] == '\r' && index + 1 < source.Length && source[index + 1] == '\n') continue;
            if (source[index] is '\r' or '\n') { line++; lineStart = index + 1; }
        }

        return (line, end - lineStart + 1);
    }
}
