using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Validation;

/// <summary>
/// The validator against the real fixtures.
/// </summary>
/// <remarks>
/// <para>
/// A validator that flags nothing is untested and one that flags everything is unusable, and only
/// real files tell you which you have written. These twelve are other tools' output — Camunda,
/// Signavio, hand-written — so every finding here was examined against the XML before being written
/// down, and each is either a defect in the file or was a defect in a rule.
/// </para>
/// <para>
/// D004 is intentionally product lint rather than a conformance rule: flow names are presentation
/// text, not formal <c>conditionExpression</c> values, so labelled but unconditioned branches are
/// reported as ambiguous for machine execution.
/// </para>
/// <para>
/// Everything below is a defect in the file. They are pinned by rule id rather than deleted from
/// the fixtures, because a sample that is wrong in a realistic way is worth more than one that has
/// been tidied — and because a rule that stops reporting one of these has regressed.
/// </para>
/// </remarks>
public sealed class FixtureValidationTests
{
    /// <summary>
    /// The fixtures that validate completely clean.
    /// </summary>
    /// <remarks>
    /// Exportable <em>and</em> silent. A new rule that starts reporting one of these is wrong until
    /// proven otherwise, because these files were valid the day before it was written.
    /// </remarks>
    [Theory]
    [InlineData("simple-process.bpmn")]
    [InlineData("collaboration.bpmn")]
    [InlineData("nested-subprocesses.bpmn")]
    [InlineData("linked-processes.bpmn")]
    [InlineData("vendor-extensions.bpmn")]
    [InlineData("zeebe-process.bpmn")]
    [InlineData("global-catalog.bpmn")]
    [InlineData("large-virtualization-stress.bpmn")]
    public void TheseFixturesValidateClean(string fileName)
    {
        var report = BpmnValidator.Validate(Read(fileName));

        Assert.True(report.Diagnostics.Count == 0, Describe(fileName, report));
    }

    [Fact]
    public void OrderProcessHasALabelledButUnconditionedDecision()
    {
        var report = BpmnValidator.Validate(Read("order-process.bpmn"));

        Assert.Equal(["D004"], Codes(report));
        Assert.False(report.IsExportable);
    }

    [Fact]
    public void PizzaStoreThrowsWhereItMeansToCatch()
    {
        /*
         * 'pizza received' is modelled as an intermediateThrowEvent carrying a messageEventDefinition.
         * It is the target of an Event-Based Gateway branch and of a Message Flow, and neither is
         * legal for a throwing Event — the customer receives the pizza, so it should catch.
         */
        var report = BpmnValidator.Validate(Read("pizza-store.bpmn"));

        Assert.Equal(["D006", "MF-008"], Codes(report));
        Assert.False(report.IsExportable);
    }

    [Fact]
    public void SelfServeRestaurantRunsASequenceFlowIntoATextAnnotation()
    {
        // DA-003: a Text Annotation is tied on with an Association, never reached by process flow.
        var report = BpmnValidator.Validate(Read("self-serve-restaurant.bpmn"));

        Assert.Equal(["D017"], Codes(report));
    }

    [Fact]
    public void RecruitmentRunsASequenceFlowOutOfADataObject()
    {
        // DA-004: a Data Object connects only through a Data Association. A token cannot come from
        // one, because data is not control flow.
        var report = BpmnValidator.Validate(Read("recruitment-and-selection.bpmn"));

        Assert.Equal(["D017"], Codes(report));
    }

    [Fact]
    public void SmartParkingHasAnUnexpressedSplitAndAnotherThrowWhereItCatches()
    {
        /*
         * Two findings. One Exclusive split has two branches that are anonymous and unconditioned,
         * and 'Recieve availability information' is a throw Event on the receiving end of a Message
         * Flow; the name says what it was meant to be.
         */
        var report = BpmnValidator.Validate(Read("smart-parking.bpmn"));

        Assert.Equal(["D004", "MF-008"], Codes(report));
    }

    [Fact]
    public void ValidatingTheStressFixtureStaysLinear()
    {
        /*
         * 602 shapes and 601 connectors. Every rule that asks "what arrives at this node" would
         * walk the flow collection to find out, which turns a linear document into a quadratic
         * validation — so the lookups are built once. This asserts the consequence rather than the
         * implementation: a validation the editor can afford to run on every edit.
         */
        var document = Read("large-virtualization-stress.bpmn");

        var started = System.Diagnostics.Stopwatch.StartNew();
        var report = BpmnValidator.Validate(document);
        started.Stop();

        Assert.True(
            started.ElapsedMilliseconds < 2000,
            $"validating 602 shapes took {started.ElapsedMilliseconds}ms");

        Assert.True(report.IsExportable);
    }

    private static IReadOnlyList<string> Codes(BpmnValidationReport report) =>
        [.. report.Diagnostics.Select(diagnostic => diagnostic.RuleId).Distinct().Order(StringComparer.Ordinal)];

    private static string Describe(string fileName, BpmnValidationReport report) =>
        $"{fileName}: {string.Join("; ", report.Diagnostics.Select(d => $"{d.RuleId} on {d.ElementId}: {d.Message}"))}";

    private static Model.BpmnDocument Read(string fileName)
    {
        var path = Path.Combine(FindRepositoryRoot(), "playground", "Bpmn.Demo", "wwwroot", "samples", fileName);

        return BpmnXmlReader.Read(File.ReadAllText(path)).Document;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "playground")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("the repository root was not found");
    }
}
