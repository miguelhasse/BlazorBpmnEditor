using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Serialization;

/// <summary>
/// Reads every sample document shipped with the playground.
/// </summary>
/// <remarks>
/// These are real exports from real modelling tools, which is the point: they use optional
/// attributes, element orderings and vendor extensions that hand-written test XML never does.
/// Each one must read without throwing, produce geometry, and leave the model self-consistent.
/// </remarks>
public sealed class SampleFixtureTests
{
    public static TheoryData<string> SampleFiles =>
    [
        "simple-process.bpmn",
        "order-process.bpmn",
        "collaboration.bpmn",
        "linked-processes.bpmn",
        "nested-subprocesses.bpmn",
        "pizza-store.bpmn",
        "recruitment-and-selection.bpmn",
        "self-serve-restaurant.bpmn",
        "smart-parking.bpmn",
        "zeebe-process.bpmn",
        "zeebe-extensions.bpmn",
        "large-virtualization-stress.bpmn",
        "vendor-extensions.bpmn",
        "global-catalog.bpmn",
    ];

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void ReadsWithoutThrowing(string fileName)
    {
        var result = ReadSample(fileName);

        Assert.NotNull(result.Document);
        Assert.NotEmpty(result.Document.Definitions.TargetNamespace);
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void ProducesRenderableGeometry(string fileName)
    {
        var document = ReadSample(fileName).Document;

        Assert.NotEmpty(document.Diagrams);

        foreach (var diagram in document.Diagrams)
        {
            Assert.All(diagram.Plane.Shapes, shape => Assert.True(
                shape.Bounds.IsPositive,
                $"{fileName}: shape '{shape.Id}' has non-positive bounds."));
        }
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void EveryShapeReferencesAnElementThatExists(string fileName)
    {
        // Specification section 10.1: an unresolvable bpmnElement reference is a document defect,
        // and one that would leave the canvas drawing a shape with no semantics behind it.
        var document = ReadSample(fileName).Document;

        var unresolved = document.Diagrams
            .SelectMany(diagram => diagram.Plane.Shapes)
            .Where(shape => document.Find(shape.BpmnElement) is null)
            .Select(shape => shape.BpmnElement)
            .ToList();

        Assert.Empty(unresolved);
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void EverySequenceFlowConnectsElementsThatExist(string fileName)
    {
        var document = ReadSample(fileName).Document;

        var dangling = document.SequenceFlows
            .Where(flow => document.Find(flow.SourceRef) is null || document.Find(flow.TargetRef) is null)
            .Select(flow => flow.Id)
            .ToList();

        Assert.Empty(dangling);
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void SatisfiesTheModelInvariants(string fileName)
    {
        var document = ReadSample(fileName).Document;
        var violations = BpmnDocumentInvariants.Check(document);

        Assert.Empty(violations.Select(violation => $"{violation.RuleId} {violation.ElementId}: {violation.Message}"));
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void AssignsEveryFlowNodeToExactlyOneContainer(string fileName)
    {
        var document = ReadSample(fileName).Document;

        var orphans = document.FlowNodes
            .Where(node => string.IsNullOrEmpty(node.ContainerId))
            .Select(node => node.Id)
            .ToList();

        Assert.Empty(orphans);
    }

    [Fact]
    public void ReadsACollaborationWithItsParticipantsAndMessageFlows()
    {
        var document = ReadSample("collaboration.bpmn").Document;

        Assert.NotNull(document.Collaboration);
        Assert.NotEmpty(document.Collaboration.Participants);
        Assert.NotEmpty(document.MessageFlows);
    }

    [Fact]
    public void ReadsNestedSubProcessContentInPlace()
    {
        var document = ReadSample("nested-subprocesses.bpmn").Document;

        var containers = document.FlowNodes.Where(node => node.FlowElementsOwned).Select(node => node.Id).ToList();
        Assert.NotEmpty(containers);

        // Nested content keeps its own container rather than being hoisted to the Process.
        var nested = document.FlowNodes.Where(node => containers.Contains(node.ContainerId)).ToList();
        Assert.NotEmpty(nested);
    }

    [Fact]
    public void RetainsZeebeExtensions()
    {
        var document = ReadSample("zeebe-extensions.bpmn").Document;

        var withExtensions = document.FlowNodes.Where(node => node.Extras.Extensions.Count > 0).ToList();
        Assert.NotEmpty(withExtensions);
    }

    private static BpmnImportResult ReadSample(string fileName)
    {
        var path = Path.Combine(FindRepositoryRoot(), "playground", "Bpmn.Demo", "wwwroot", "samples", fileName);
        return BpmnXmlReader.Read(File.ReadAllText(path));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "playground"))
                && Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output folder.");
    }
}
