using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Validation;

/// <summary>
/// Engine profiles (specification sections 5.4 and 5.5, implementation plan M7).
/// </summary>
/// <remarks>
/// A profile is how a host adds rules the core has never heard of — Zeebe wanting a job type, a
/// house style wanting every Task named — without either of those becoming a BPMN rule. What these
/// are mostly about is the sentence in section 5.5 that says the layer runs "without weakening core
/// errors", because a profile that could silence the core would be a setting that turns validation
/// off.
/// </remarks>
public sealed class ProfileTests
{
    [Fact]
    public void AProfileAddsItsOwnFindings()
    {
        var report = BpmnValidator.Validate(Document(), [new Stub("PROF-001", BpmnSeverity.Error, "Every Service Task needs a job type.")]);

        var profile = Assert.Single(report.Diagnostics, diagnostic => diagnostic.RuleId == "PROF-001");
        Assert.Equal(BpmnSeverity.Error, profile.Severity);
        Assert.Contains("job type", profile.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AndSaysWhichProfileSaidIt()
    {
        // Several profiles may be registered and their rule numbers are their own. Without the
        // name, two engines using PROF-001 are indistinguishable in the panel.
        var report = BpmnValidator.Validate(Document(), [new Stub("PROF-001", BpmnSeverity.Warning, "Something.")]);

        Assert.Contains("(stub)", Assert.Single(report.Diagnostics).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARuleIdOutsideTheProfileRangeIsRefused()
    {
        /*
         * A profile emitting CT-001 would put its own words on a core rule's identity, and a reader
         * chasing that code would be reading about something else entirely. It is dropped, and the
         * refusal names the profile so the host can find it.
         */
        var report = BpmnValidator.Validate(Document(), [new Stub("CT-001", BpmnSeverity.Error, "Mine now.")]);

        var refusal = Assert.Single(report.Diagnostics);
        Assert.Equal("PROF-000", refusal.RuleId);
        Assert.Contains("not a PROF-nnn rule id", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(report.Diagnostics, diagnostic => diagnostic.RuleId == "CT-001");
    }

    [Theory]
    [InlineData("PROF-000")]
    [InlineData("PROF-200")]
    [InlineData("PROF-1")]
    [InlineData("prof-001")]
    [InlineData("")]
    public void SoIsAnIdThatOnlyLooksLikeOne(string ruleId)
    {
        var report = BpmnValidator.Validate(Document(), [new Stub(ruleId, BpmnSeverity.Warning, "Something.")]);

        Assert.Equal("PROF-000", Assert.Single(report.Diagnostics).RuleId);
    }

    [Fact]
    public void AThrowingProfileDoesNotTakeTheCoreDownWithIt()
    {
        /*
         * The weakening that would be easiest to introduce by accident: host code runs inside the
         * editor's validation, and an exception from it would otherwise discard the core findings
         * alongside the profile's.
         */
        var report = BpmnValidator.Validate(Broken(), [new Exploding()]);

        Assert.Contains(report.Diagnostics, diagnostic => diagnostic.RuleId.StartsWith("D0", StringComparison.Ordinal));

        var refusal = Assert.Single(report.Diagnostics, diagnostic => diagnostic.RuleId == "PROF-000");
        Assert.Contains("it failed", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AProfileCannotRemoveOrDowngradeACoreFinding()
    {
        // It can only add, so whatever it says, the core list is unchanged.
        var withoutProfile = BpmnValidator.Validate(Broken());
        var withProfile = BpmnValidator.Validate(Broken(), [new Stub("PROF-001", BpmnSeverity.Info, "All fine by me.")]);

        var core = withProfile.Diagnostics.Where(diagnostic => !diagnostic.RuleId.StartsWith("PROF-", StringComparison.Ordinal));

        Assert.Equal(withoutProfile.Diagnostics, core);
    }

    [Fact]
    public void NoProfilesMeansNothingChanges()
    {
        Assert.Equal(
            BpmnValidator.Validate(Broken()).Diagnostics,
            BpmnValidator.Validate(Broken(), []).Diagnostics);
    }

    // --- fixtures ----------------------------------------------------------------------

    private sealed class Stub(string ruleId, BpmnSeverity severity, string message) : IBpmnProfileValidator
    {
        public string Name => "stub";

        public IEnumerable<BpmnProfileDiagnostic> Validate(BpmnDocument document) =>
            [new BpmnProfileDiagnostic(ruleId, severity, message)];
    }

    private sealed class Exploding : IBpmnProfileValidator
    {
        public string Name => "exploding";

        public IEnumerable<BpmnProfileDiagnostic> Validate(BpmnDocument document) =>
            throw new InvalidOperationException("the engine adapter is misconfigured");
    }

    private static BpmnDocument Document() => new BpmnDocumentBuilder()
        .Node("Start_1", BpmnFlowNodeType.StartEvent, new Bounds(100, 100, 36, 36), "Begin")
        .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(200, 80, 100, 80), "Work")
        .Node("End_1", BpmnFlowNodeType.EndEvent, new Bounds(360, 100, 36, 36), "Done")
        .Flow("Flow_1", "Start_1", "Task_1", [new DiPoint(136, 118), new DiPoint(200, 118)])
        .Flow("Flow_2", "Task_1", "End_1", [new DiPoint(300, 118), new DiPoint(360, 118)])
        .Build();

    /// <summary>A document the core objects to, so "the core still speaks" means something.</summary>
    private static BpmnDocument Broken()
    {
        var document = Document();

        return document with
        {
            FlowNodes = [.. document.FlowNodes, new BpmnFlowNode("Task_Orphan", BpmnFlowNodeType.Task, document.Processes[0].Id, "Stranded")],
        };
    }
}
