using Hasseware.AspNetCore.Components.Bpmn.Editing;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// What a file would bring in, described before anything changes (implementation plan M6).
/// </summary>
/// <remarks>
/// An import replaces the whole document, which makes it the one operation undo cannot help with:
/// the history belongs to the file being replaced. The milestone's exit criterion is about the
/// answer "no" — a preview must be rejectable "without corrupting the current graph" — and that
/// holds here structurally, because a preview is a parsed document held to one side and nothing
/// reaches the editor's own until it is accepted.
/// </remarks>
public sealed class ImportPreviewTests
{
    [Fact]
    public void DescribesWhatIsInTheFile()
    {
        var preview = BpmnImportPreview.Of("simple-process.bpmn", Sample("simple-process.bpmn"));

        Assert.True(preview.CanAccept);
        Assert.Equal(3, preview.FlowNodeCount);
        Assert.Equal(2, preview.ConnectorCount);
        Assert.Equal(1, preview.DiagramCount);
        Assert.Null(preview.Error);
    }

    [Fact]
    public void ReportsWhatTheValidatorMakesOfIt()
    {
        // pizza-store models a received message as a throwing Event, which the validator catches.
        // Worth knowing before opening it, not after.
        var preview = BpmnImportPreview.Of("pizza-store.bpmn", Sample("pizza-store.bpmn"));

        Assert.NotEmpty(preview.Validation.Diagnostics);
    }

    [Fact]
    public void CountsWhatWouldNotSurviveARoundTrip()
    {
        /*
         * The number that actually decides it. Section 10.6 wants a round trip to be lossless, and
         * a file containing something this editor cannot represent is where it will not be — the
         * user is entitled to know that before replacing what is on screen.
         */
        const string WithChoreography = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1" targetNamespace="http://example.org">
              <bpmn:choreography id="Choreography_1">
                <bpmn:choreographyTask id="ChoreographyTask_1" name="Order" />
              </bpmn:choreography>
              <bpmn:process id="Process_1" />
            </bpmn:definitions>
            """;

        var preview = BpmnImportPreview.Of("choreography.bpmn", WithChoreography);

        Assert.True(preview.CanAccept);
        Assert.True(
            preview.DroppedCount > 0 || preview.Diagnostics.Count > 0,
            "a Choreography should be reported rather than silently ignored");
    }

    // --- the file that will not open ----------------------------------------------------

    [Fact]
    public void AFileThatIsNotXmlIsAnAnswerRatherThanAnException()
    {
        // The user picked the wrong file. An exception crossing into the UI would take the editor
        // down over a bad drop, which is a much worse outcome than "this is not a BPMN file".
        var preview = BpmnImportPreview.Of("notes.txt", "this is not xml at all");

        Assert.False(preview.CanAccept);
        Assert.NotNull(preview.Error);
        Assert.Null(preview.Document);
    }

    [Fact]
    public void WellFormedXmlThatIsNotBpmnIsRefusedToo()
    {
        var preview = BpmnImportPreview.Of("other.xml", "<?xml version=\"1.0\"?><html><body>hello</body></html>");

        Assert.False(preview.CanAccept);
        Assert.NotNull(preview.Error);
    }

    [Fact]
    public void AnUnreadableFileCountsNothing()
    {
        // Every count has to be safe to render beside the error, because the offer shows both.
        var preview = BpmnImportPreview.Of("broken.bpmn", "<definitions");

        Assert.Equal(0, preview.FlowNodeCount);
        Assert.Equal(0, preview.ConnectorCount);
        Assert.Equal(0, preview.DiagramCount);
        Assert.Equal(0, preview.DroppedCount);
        Assert.Empty(preview.Validation.Diagnostics);
    }

    [Fact]
    public void PreviewingDoesNotDependOnAnythingBeingOpen()
    {
        // The whole point: reading a file is a question about the file. Nothing else is consulted,
        // so nothing else can be disturbed by asking.
        var first = BpmnImportPreview.Of("a.bpmn", Sample("simple-process.bpmn"));
        var second = BpmnImportPreview.Of("b.bpmn", Sample("order-process.bpmn"));

        Assert.NotEqual(first.FlowNodeCount, second.FlowNodeCount);
        Assert.NotSame(first.Document, second.Document);
    }

    private static string Sample(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "playground")))
        {
            directory = directory.Parent;
        }

        return File.ReadAllText(Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("the repository root was not found"),
            "playground",
            "Bpmn.Demo",
            "wwwroot",
            "samples",
            fileName));
    }
}
