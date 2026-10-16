using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Serialization;

/// <summary>
/// What the reader says about what it changed or could not use (specification sections 8 and 10.6).
/// </summary>
/// <remarks>
/// Section 10.6 asks for every dropped, quarantined or normalized item to be reported, so a connector
/// whose ends were moved onto its shapes' outlines is named, rather than mentioned once for the file.
/// </remarks>
public sealed class ImportNoticeTests
{
    [Fact]
    public void NamesEachConnectorWhoseEndsWereMovedOntoTheOutline()
    {
        var result = Read("pizza-store.bpmn");

        var moved = result.Diagnostics
            .Where(diagnostic => diagnostic.Severity == BpmnImportSeverity.Degraded
                && diagnostic.Message.Contains("off the outline", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(moved);
        Assert.All(moved, diagnostic => Assert.NotNull(diagnostic.ElementId));
        Assert.All(moved, diagnostic => Assert.Contains($"'{diagnostic.ElementId}'", diagnostic.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void ReportsAConnectorOncePerReasonNotOncePerFix()
    {
        // Every waypoint of these flows is at the origin. That is reported as the defect it is, and
        // the ends the reader then derived for them are not reported again.
        var result = Read("simple-process.bpmn");

        var flows = result.Diagnostics
            .Where(diagnostic => diagnostic.ElementId?.StartsWith("Flow_", StringComparison.Ordinal) == true)
            .ToList();

        Assert.Equal(2, flows.Count);
        Assert.All(flows, diagnostic => Assert.Equal(BpmnImportSeverity.Degraded, diagnostic.Severity));
    }

    [Fact]
    public void ReportsNothingForAFileWhoseEndsAreAlreadyOnTheirShapes()
    {
        Assert.DoesNotContain(
            Read("order-process.bpmn").Diagnostics,
            diagnostic => diagnostic.Message.Contains("off the outline", StringComparison.Ordinal));
    }

    [Fact]
    public void RaisesAnExcludedConstructAsAWarningOncePerOccurrence()
    {
        // Section 8: a non-blocking "unsupported element" warning per occurrence. Degraded is the
        // reader's warning: the content is kept, but not rendered or editable.
        var excluded = Read("vendor-extensions.bpmn").Diagnostics
            .Where(diagnostic => diagnostic.Message.Contains("Choreography or Conversation", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, excluded.Count);
        Assert.All(excluded, diagnostic => Assert.Equal(BpmnImportSeverity.Degraded, diagnostic.Severity));
    }

    private static BpmnImportResult Read(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "playground")))
        {
            directory = directory.Parent;
        }

        var path = Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("the repository root was not found"),
            "playground",
            "Bpmn.Demo",
            "wwwroot",
            "samples",
            fileName);

        return BpmnXmlReader.Read(File.ReadAllText(path));
    }
}
