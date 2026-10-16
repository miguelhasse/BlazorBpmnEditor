using System.Text.RegularExpressions;

using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Validation;

/// <summary>Something an execution-engine profile has to say about a document.</summary>
/// <param name="RuleId">The profile's own rule, which must be in the <c>PROF-nnn</c> range.</param>
/// <param name="Severity">How much it matters.</param>
/// <param name="Message">What to show a reader.</param>
/// <param name="ElementId">The element it is about, when it is about one.</param>
public sealed record BpmnProfileDiagnostic(
    string RuleId,
    BpmnSeverity Severity,
    string Message,
    string? ElementId = null)
{
    /// <summary>Arguments for the localized profile message.</summary>
    public IReadOnlyList<object?>? MessageArguments { get; init; }

    /// <summary>Optional resource key for a profile-specific message format.</summary>
    public string? MessageResourceKey { get; init; }

    /// <summary>Compares profile findings independently of presentation metadata.</summary>
    public bool Equals(BpmnProfileDiagnostic? other) => other is not null
        && RuleId == other.RuleId && Severity == other.Severity
        && Message == other.Message && ElementId == other.ElementId;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(RuleId, Severity, Message, ElementId);
}

/// <summary>
/// A host-supplied validator for rules that are an engine's, not BPMN's (specification section 5.4).
/// </summary>
/// <remarks>
/// <para>
/// Zeebe requires a job type on every Service Task; Camunda 7 wants a delegate expression; neither
/// is a BPMN rule and neither belongs in the core engine, which is why section 5.4 reserves the
/// <c>PROF-</c> range for them and section 5.5 runs them in their own layer. A profile is how a
/// host adds those without the core having heard of its engine.
/// </para>
/// <para>
/// A profile may only add. It cannot remove a core finding, downgrade one, or make a document that
/// BPMN says is invalid look valid — section 5.5 is explicit that the extension and profile layer
/// runs "without weakening core errors", and a profile that could silence the core would be a
/// setting that turns validation off.
/// </para>
/// </remarks>
public interface IBpmnProfileValidator
{
    /// <summary>What this profile is called, for a diagnostic a reader has to make sense of.</summary>
    public string Name { get; }

    /// <summary>Whatever this profile has to say about the document.</summary>
    /// <param name="document">The document to check.</param>
    public IEnumerable<BpmnProfileDiagnostic> Validate(BpmnDocument document);
}

/// <summary>
/// Runs registered profiles and keeps them inside their own range.
/// </summary>
/// <remarks>
/// The guard is not politeness. A profile emitting <c>CT-001</c> would put its own words on a core
/// rule's identity, and a reader chasing that code would be reading about something else entirely;
/// one emitting no id at all would produce a finding nothing can be filtered or suppressed by. Both
/// are dropped with a note saying which profile did it, because a profile misbehaving must not take
/// the document's own validation down with it.
/// </remarks>
public static partial class BpmnProfileRunner
{
    /// <summary>Everything the registered profiles have to say, in registration order.</summary>
    /// <param name="document">The document to check.</param>
    /// <param name="profiles">The profiles to run.</param>
    public static IReadOnlyList<BpmnDiagnostic> Run(
        BpmnDocument document,
        IReadOnlyList<IBpmnProfileValidator> profiles)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(profiles);

        var found = new List<BpmnDiagnostic>();

        foreach (var profile in profiles)
        {
            /*
             * A throwing profile is reported, not propagated. It is host code running inside the
             * editor's validation, and an exception from it would otherwise take out the core
             * findings alongside it — which is exactly the weakening this layer is forbidden.
             */
            IReadOnlyList<BpmnProfileDiagnostic> reported;

            try
            {
                reported = [.. profile.Validate(document)];
            }
            catch (Exception failure) when (failure is not OutOfMemoryException and not StackOverflowException)
            {
                found.Add(Refused(BpmnDiagnosticText.Create("PROF-000.Detail.Failed",
                    "The '{0}' profile reported nothing usable: it failed: {1}.", profile.Name, failure.Message)));
                continue;
            }

            foreach (var diagnostic in reported)
            {
                if (!ProfileRuleId().IsMatch(diagnostic.RuleId))
                {
                    found.Add(Refused(BpmnDiagnosticText.Create("PROF-000.Detail.InvalidId",
                        "The '{0}' profile reported nothing usable: '{1}' is not a PROF-nnn rule id.", profile.Name, diagnostic.RuleId)));
                    continue;
                }

                found.Add(new BpmnDiagnostic(
                    diagnostic.RuleId,
                    diagnostic.Severity,
                    $"{diagnostic.Message} ({profile.Name})",
                    diagnostic.ElementId)
                {
                    MessageResourceKey = diagnostic.MessageResourceKey,
                    MessageArguments = diagnostic.MessageArguments,
                    ProfileName = profile.Name,
                });
            }
        }

        return found;
    }

    private static BpmnDiagnostic Refused(BpmnDiagnosticText text) => new(
        "PROF-000", BpmnSeverity.Warning, text.Message)
    {
        MessageResourceKey = text.ResourceKey,
        MessageArguments = text.Arguments,
    };

    /// <summary>The range section 5.4 reserves for engine profiles.</summary>
    [GeneratedRegex(@"^PROF-(0[0-9][1-9]|0[1-9][0-9]|1[0-9][0-9])$")]
    private static partial Regex ProfileRuleId();
}
