using System.Xml.Linq;

using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Serialization;

/// <summary>
/// Import, export, import again, and prove nothing moved.
/// </summary>
/// <remarks>
/// <para>
/// The milestone's exit criterion, and the specification's (section 11, "golden XML round-trip
/// tests"). Run over every sample the playground ships — real exports from real tools — plus two
/// hand-built fixtures that pack in the constructs real files only show one at a time.
/// </para>
/// <para>
/// Equality is asserted semantically, not byte for byte. Two serializers can disagree about
/// apostrophe escaping and attribute line breaks while meaning exactly the same thing, and a test
/// that failed on that would be measuring the XML writer rather than the round trip.
/// </para>
/// </remarks>
public sealed class RoundTripTests
{
    [Theory]
    [InlineData(BpmnWriteMode.Canonical)]
    [InlineData(BpmnWriteMode.Fidelity)]
    public void CollaborationArtifactsSurviveRepeatedRoundTrips(BpmnWriteMode mode)
    {
        var document = new BpmnDocumentBuilder()
            .Pool("Pool", "Pool", new Bounds(0, 0, 600, 250))
            .TextAnnotation("Note", "", new Bounds(700, 100, 100, 30))
            .Group("Group", "Group", new Bounds(650, 50, 200, 200))
            .Association("Association", "Pool", "Note", [new DiPoint(600, 115), new DiPoint(700, 115)])
            .Build();
        var collaborationId = document.Collaboration!.Id;
        document = document with
        {
            TextAnnotations = [.. document.TextAnnotations.Select(item => item with { ContainerId = collaborationId })],
            Associations = [.. document.Associations.Select(item => item with { ContainerId = collaborationId })],
            Groups = [.. document.Groups.Select(item => item with { ContainerId = collaborationId })],
        };
        var options = mode == BpmnWriteMode.Canonical ? BpmnWriteOptions.Canonical : BpmnWriteOptions.Fidelity;
        var first = BpmnXmlReader.Read(BpmnXmlWriter.Write(document, options).Xml).Document;
        var second = BpmnXmlReader.Read(BpmnXmlWriter.Write(first, options).Xml).Document;
        Assert.Equal(document.TextAnnotations, first.TextAnnotations);
        Assert.Equal(document.Associations, first.Associations);
        Assert.Equal(document.Groups, first.Groups);
        Assert.Equal(first.TextAnnotations, second.TextAnnotations);
        Assert.Equal(first.Associations, second.Associations);
        Assert.Equal(first.Groups, second.Groups);
    }

    public static TheoryData<string, BpmnWriteMode> Documents
    {
        get
        {
            var data = new TheoryData<string, BpmnWriteMode>();

            foreach (var file in InterchangeFixtures.All)
            {
                data.Add(file, BpmnWriteMode.Canonical);
                data.Add(file, BpmnWriteMode.Fidelity);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void SemanticContentSurvives(string fileName, BpmnWriteMode mode)
    {
        var (before, after) = InterchangeFixtures.RoundTrip(fileName, mode);

        Assert.Equal(Ids(before.FlowNodes.Select(x => x.Id)), Ids(after.FlowNodes.Select(x => x.Id)));
        Assert.Equal(Ids(before.SequenceFlows.Select(x => x.Id)), Ids(after.SequenceFlows.Select(x => x.Id)));
        Assert.Equal(Ids(before.MessageFlows.Select(x => x.Id)), Ids(after.MessageFlows.Select(x => x.Id)));
        Assert.Equal(Ids(before.Lanes.Select(x => x.Id)), Ids(after.Lanes.Select(x => x.Id)));
        Assert.Equal(Ids(before.Associations.Select(x => x.Id)), Ids(after.Associations.Select(x => x.Id)));
        Assert.Equal(Ids(before.TextAnnotations.Select(x => x.Id)), Ids(after.TextAnnotations.Select(x => x.Id)));
        Assert.Equal(Ids(before.Groups.Select(x => x.Id)), Ids(after.Groups.Select(x => x.Id)));
        Assert.Equal(Ids(before.DataObjects.Select(x => x.Id)), Ids(after.DataObjects.Select(x => x.Id)));
        Assert.Equal(
            Ids(before.DataObjectReferences.Select(x => x.Id)),
            Ids(after.DataObjectReferences.Select(x => x.Id)));
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void FlowNodeAttributesSurvive(string fileName, BpmnWriteMode mode)
    {
        var (before, after) = InterchangeFixtures.RoundTrip(fileName, mode);

        foreach (var node in before.FlowNodes)
        {
            var written = Assert.Single(after.FlowNodes, x => x.Id == node.Id);

            Assert.Equal(node.Type, written.Type);
            Assert.Equal(node.Name, written.Name);
            Assert.Equal(node.ContainerId, written.ContainerId);
            Assert.Equal(node.EventDefinition, written.EventDefinition);
            Assert.Equal(node.IsInterrupting, written.IsInterrupting);
            Assert.Equal(node.AttachedToRef, written.AttachedToRef);
            Assert.Equal(node.LoopCharacteristics, written.LoopCharacteristics);
            Assert.Equal(node.IsForCompensation, written.IsForCompensation);
            Assert.Equal(node.CalledElementRef, written.CalledElementRef);
            Assert.Equal(node.DefaultFlowRef, written.DefaultFlowRef);
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void ConnectorEndpointsSurvive(string fileName, BpmnWriteMode mode)
    {
        var (before, after) = InterchangeFixtures.RoundTrip(fileName, mode);

        foreach (var flow in before.SequenceFlows)
        {
            var written = Assert.Single(after.SequenceFlows, x => x.Id == flow.Id);

            Assert.Equal(flow.SourceRef, written.SourceRef);
            Assert.Equal(flow.TargetRef, written.TargetRef);
            Assert.Equal(flow.ContainerId, written.ContainerId);
            Assert.Equal(flow.Name, written.Name);
            Assert.Equal(flow.IsDefault, written.IsDefault);
            Assert.Equal(flow.ConditionExpression?.Trim(), written.ConditionExpression?.Trim());
        }
    }

    /// <summary>
    /// Geometry is the one thing that must come back bit for bit (section 10.6).
    /// </summary>
    /// <remarks>
    /// A writer that rounds or recomputes coordinates silently redraws the user's diagram on save.
    /// Comparing the doubles directly is the point: there is no tolerance to allow here.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Documents))]
    public void GeometrySurvivesExactly(string fileName, BpmnWriteMode mode)
    {
        var (before, after) = InterchangeFixtures.RoundTrip(fileName, mode);

        var beforeShapes = Shapes(before);
        var afterShapes = Shapes(after);
        Assert.Equal(beforeShapes.Keys.Order(StringComparer.Ordinal), afterShapes.Keys.Order(StringComparer.Ordinal));

        foreach (var (id, shape) in beforeShapes)
        {
            Assert.Equal(shape.Bounds, afterShapes[id].Bounds);
            Assert.Equal(shape.IsHorizontal, afterShapes[id].IsHorizontal);
            Assert.Equal(shape.IsExpanded, afterShapes[id].IsExpanded);
            Assert.Equal(shape.IsMarkerVisible, afterShapes[id].IsMarkerVisible);
            Assert.Equal(shape.Label?.Bounds, afterShapes[id].Label?.Bounds);

            // Section 10.1: a label is visible because the element is present, not because it has
            // bounds, so presence has to survive on its own.
            Assert.Equal(shape.Label is null, afterShapes[id].Label is null);
        }

        var beforeEdges = Edges(before);
        var afterEdges = Edges(after);
        Assert.Equal(beforeEdges.Keys.Order(StringComparer.Ordinal), afterEdges.Keys.Order(StringComparer.Ordinal));

        foreach (var (id, edge) in beforeEdges)
        {
            Assert.Equal(edge.Waypoints, afterEdges[id].Waypoints);
            Assert.Equal(edge.Label?.Bounds, afterEdges[id].Label?.Bounds);
        }
    }

    /// <summary>
    /// Writing twice gives the same bytes, so a save that changes nothing changes no file.
    /// </summary>
    [Theory]
    [MemberData(nameof(Documents))]
    public void ExportIsStable(string fileName, BpmnWriteMode mode)
    {
        var options = Options(mode);
        var first = BpmnXmlWriter.Write(InterchangeFixtures.Read(fileName).Document, options);
        var second = BpmnXmlWriter.Write(BpmnXmlReader.Read(first.Xml).Document, options);
        var third = BpmnXmlWriter.Write(BpmnXmlReader.Read(second.Xml).Document, options);

        Assert.Equal(second.Xml, third.Xml);
    }

    /// <summary>
    /// Every reference still resolves after an export — the milestone's other exit criterion.
    /// </summary>
    /// <remarks>
    /// This is what the measured spike found the previous interchange package failing: it kept the
    /// DI while dropping the semantic elements the DI pointed at, leaving a document whose
    /// <c>bpmnElement</c> attributes went nowhere (section 10.1).
    /// </remarks>
    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryReferenceResolves(string fileName, BpmnWriteMode mode)
    {
        var (_, after) = InterchangeFixtures.RoundTrip(fileName, mode);
        var dangling = new List<string>();

        void Check(string? reference, string owner)
        {
            if (reference is null || reference.Length == 0) return;
            if (after.Find(reference) is null) dangling.Add($"{owner} -> {reference}");
        }

        foreach (var node in after.FlowNodes)
        {
            Check(node.AttachedToRef, $"{node.Id}.attachedToRef");

            foreach (var definition in node.EventDefinitions)
            {
                Check(definition.Ref, $"{node.Id}.{definition.Type}Ref");
            }
        }

        foreach (var flow in after.SequenceFlows)
        {
            Check(flow.SourceRef, $"{flow.Id}.sourceRef");
            Check(flow.TargetRef, $"{flow.Id}.targetRef");
        }

        foreach (var reference in after.DataObjectReferences) Check(reference.DataObjectRef, $"{reference.Id}.dataObjectRef");
        foreach (var reference in after.DataStoreReferences) Check(reference.DataStoreRef, $"{reference.Id}.dataStoreRef");
        foreach (var group in after.Groups) Check(group.CategoryValueRef, $"{group.Id}.categoryValueRef");
        foreach (var participant in after.Collaboration?.Participants ?? []) Check(participant.ProcessRef, $"{participant.Id}.processRef");

        foreach (var shape in after.Diagrams.SelectMany(d => d.Plane.Shapes)) Check(shape.BpmnElement, $"{shape.Id}.bpmnElement");
        foreach (var edge in after.Diagrams.SelectMany(d => d.Plane.Edges)) Check(edge.BpmnElement, $"{edge.Id}.bpmnElement");

        Assert.Empty(dangling);
    }

    /// <summary>The global definitions section 1.0 makes the editor responsible for.</summary>
    [Theory]
    [MemberData(nameof(Documents))]
    public void GlobalDefinitionsSurvive(string fileName, BpmnWriteMode mode)
    {
        var (before, after) = InterchangeFixtures.RoundTrip(fileName, mode);

        Assert.Equal(Ids(before.Catalog.Messages.Select(x => x.Id)), Ids(after.Catalog.Messages.Select(x => x.Id)));
        Assert.Equal(Ids(before.Catalog.Signals.Select(x => x.Id)), Ids(after.Catalog.Signals.Select(x => x.Id)));
        Assert.Equal(Ids(before.Catalog.Errors.Select(x => x.Id)), Ids(after.Catalog.Errors.Select(x => x.Id)));
        Assert.Equal(Ids(before.Catalog.Escalations.Select(x => x.Id)), Ids(after.Catalog.Escalations.Select(x => x.Id)));
        Assert.Equal(
            Ids(before.Catalog.ItemDefinitions.Select(x => x.Id)),
            Ids(after.Catalog.ItemDefinitions.Select(x => x.Id)));
        Assert.Equal(Ids(before.Catalog.DataStores.Select(x => x.Id)), Ids(after.Catalog.DataStores.Select(x => x.Id)));
        Assert.Equal(Ids(before.Catalog.Categories.Select(x => x.Id)), Ids(after.Catalog.Categories.Select(x => x.Id)));
        Assert.Equal(Ids(before.Catalog.Interfaces.Select(x => x.Id)), Ids(after.Catalog.Interfaces.Select(x => x.Id)));
        Assert.Equal(
            Ids(before.Catalog.GlobalTasks.Select(x => x.Id)),
            Ids(after.Catalog.GlobalTasks.Select(x => x.Id)));
        Assert.Equal(before.Catalog.Imports.Count, after.Catalog.Imports.Count);
        Assert.Equal(before.Catalog.Preserved.Count, after.Catalog.Preserved.Count);
    }

    /// <summary>Vendor extensions and the attributes that hide beside them (section 10.5).</summary>
    [Theory]
    [MemberData(nameof(Documents))]
    public void PreservedContentSurvives(string fileName, BpmnWriteMode mode)
    {
        var (before, after) = InterchangeFixtures.RoundTrip(fileName, mode);

        foreach (var node in before.FlowNodes.Where(x => !x.Extras.IsEmpty))
        {
            var written = Assert.Single(after.FlowNodes, x => x.Id == node.Id);

            Assert.Equal(node.Extras.Documentation?.Trim(), written.Extras.Documentation?.Trim());
            Assert.Equal(
                node.Extras.ForeignAttributes.OrderBy(x => x.Key, StringComparer.Ordinal),
                written.Extras.ForeignAttributes.OrderBy(x => x.Key, StringComparer.Ordinal));
            Assert.Equal(node.Extras.Extensions, written.Extras.Extensions);
        }
    }

    private static Dictionary<string, BpmnShape> Shapes(BpmnDocument document) =>
        document.Diagrams
            .SelectMany(diagram => diagram.Plane.Shapes)
            .ToDictionary(shape => shape.Id, StringComparer.Ordinal);

    private static Dictionary<string, BpmnEdge> Edges(BpmnDocument document) =>
        document.Diagrams
            .SelectMany(diagram => diagram.Plane.Edges)
            .ToDictionary(edge => edge.Id, StringComparer.Ordinal);

    private static List<string> Ids(IEnumerable<string> ids) => [.. ids.Order(StringComparer.Ordinal)];

    internal static BpmnWriteOptions Options(BpmnWriteMode mode) =>
        mode == BpmnWriteMode.Fidelity
            ? BpmnWriteOptions.Fidelity
            : BpmnWriteOptions.Canonical with { AllowLossyCanonicalExport = true };
}

/// <summary>Locates the documents the interchange suite reads, and runs one round trip.</summary>
internal static class InterchangeFixtures
{
    /// <summary>
    /// Every sample the playground ships, including the two hand-built interchange fixtures.
    /// </summary>
    /// <remarks>
    /// The interchange fixtures live beside the real ones rather than under the test project so
    /// there is one copy to keep correct, and so the demo's interchange page can round-trip them
    /// in a browser as well.
    /// </remarks>
    internal static readonly string[] All =
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

    internal static BpmnImportResult Read(string fileName) => BpmnXmlReader.Read(File.ReadAllText(PathFor(fileName)));

    internal static (BpmnDocument Before, BpmnDocument After) RoundTrip(string fileName, BpmnWriteMode mode)
    {
        var before = Read(fileName).Document;
        var exported = BpmnXmlWriter.Write(before, RoundTripTests.Options(mode));

        return (before, BpmnXmlReader.Read(exported.Xml).Document);
    }

    /// <summary>The exported XML, as a tree, for assertions about the file rather than the model.</summary>
    internal static XDocument Export(string fileName, BpmnWriteMode mode) =>
        XDocument.Parse(BpmnXmlWriter.Write(Read(fileName).Document, RoundTripTests.Options(mode)).Xml);

    internal static string PathFor(string fileName) =>
        Path.Combine(FindRepositoryRoot(), "playground", "Bpmn.Demo", "wwwroot", "samples", fileName);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "icons"))
                && Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output folder.");
    }
}
