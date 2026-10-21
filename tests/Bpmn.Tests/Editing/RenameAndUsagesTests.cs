using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// Find usages and safe rename (implementation plan M6).
/// </summary>
/// <remarks>
/// <para>
/// An id is what other elements were written against, so changing one is a whole-document rewrite.
/// The failure mode is not a wrong answer but a document that still serialises and no longer
/// loads, which no amount of looking at the canvas afterwards will reveal — the shape simply is
/// not drawn.
/// </para>
/// <para>
/// The load-bearing test here is <see cref="NoSiteIsLeftNamingTheOldId"/>. The map that finds
/// references and the rewriter that moves them are two enumerations of the same facts, and that
/// test is what stops them drifting: whatever the map can find, the rewriter must have moved.
/// </para>
/// </remarks>
public sealed class RenameAndUsagesTests
{
    // --- find usages -------------------------------------------------------------------

    [Fact]
    public void FindsEveryPlaceAnElementIsNamed()
    {
        var usages = BpmnReferenceMap.UsagesOf(Collaboration(), "Task_1");

        // Two flows, the Lane that claims it, and the shape that draws it. The Lane and the shape
        // are the two a property panel would never show and a rename must never miss.
        Assert.Equal(
            [
                ("Flow_1", "targetRef"),
                ("Flow_2", "sourceRef"),
                ("Lane_1", "flowNodeRef"),
                ("Task_1_di", "bpmnElement"),
            ],
            usages.Select(site => (site.OwnerId, site.Attribute)).OrderBy(pair => pair.OwnerId, StringComparer.Ordinal));
    }

    [Fact]
    public void ADiUsageSaysWhichDiagramItIsOn()
    {
        // Section 10.1: the same element may be drawn on more than one plane, so "where is it
        // used" is not answerable without naming the diagram.
        var usage = Assert.Single(
            BpmnReferenceMap.UsagesOf(Collaboration(), "Task_1"),
            site => site.Attribute == "bpmnElement");

        Assert.Equal("Diagram_1", usage.DiagramId);
    }

    [Fact]
    public void AnUnusedElementHasNoUsages()
    {
        Assert.Empty(BpmnReferenceMap.UsagesOf(Collaboration(), "Nothing_Refers_To_This"));
    }

    [Fact]
    public void AnEmptyReferenceIsNotAUsage()
    {
        // A null calledElementRef is the absence of a reference, not a reference to nothing.
        Assert.DoesNotContain(
            BpmnReferenceMap.Sites(Collaboration()),
            site => string.IsNullOrEmpty(site.TargetId));
    }

    // --- the invariant that keeps the two halves honest ---------------------------------

    [Theory]
    [InlineData("Task_1")]
    [InlineData("Flow_1")]
    [InlineData("Lane_1")]
    [InlineData("Pool_1")]
    [InlineData("Process_1")]
    [InlineData("Start_1")]
    public void NoSiteIsLeftNamingTheOldId(string elementId)
    {
        /*
         * The map and the rewriter enumerate the same facts twice, which is exactly the shape of
         * thing that drifts. This ties them together: whatever the map can find naming the old id,
         * the rewriter must have moved — and it must have moved all of it, not some.
         */
        var document = Collaboration();
        var before = BpmnReferenceMap.UsagesOf(document, elementId).Count;

        var result = Apply(document, new RenameElementCommand(elementId, "Renamed_1"));

        Assert.True(result.IsApplied, Codes(result));
        Assert.Empty(BpmnReferenceMap.UsagesOf(result.Document, elementId));
        Assert.Equal(before, BpmnReferenceMap.UsagesOf(result.Document, "Renamed_1").Count);
    }

    [Fact]
    public void TheRenamedDocumentStillValidatesAndStillLoads()
    {
        // The real test of a rename: the document is no worse than it was, and a round trip does
        // not lose anything. A missed reference shows up here as a diagnostic, not as a crash.
        var document = Collaboration();
        var before = BpmnValidator.Validate(document);

        var renamed = Apply(document, new RenameElementCommand("Task_1", "Task_Renamed")).Document;
        var after = BpmnValidator.Validate(renamed);

        Assert.Equal(before.Diagnostics.Count, after.Diagnostics.Count);

        var written = BpmnXmlReader.Read(BpmnXmlWriter.Write(renamed).Xml).Document;

        Assert.NotNull(written.FindFlowNode("Task_Renamed"));
        Assert.Null(written.FindFlowNode("Task_1"));
        Assert.DoesNotContain(BpmnValidator.Validate(written).Diagnostics, d => d.RuleId is "D017" or "D022");
    }

    [Fact]
    public void TheShapeStillDrawsTheRenamedElement()
    {
        // The reference a rename is most likely to leave behind, because nothing complains: the
        // element is fine, the shape is fine, and the diagram is empty where it used to be.
        var renamed = Apply(Collaboration(), new RenameElementCommand("Task_1", "Task_Renamed")).Document;

        var shape = Assert.Single(
            renamed.Diagrams.SelectMany(diagram => diagram.Plane.Shapes),
            candidate => candidate.BpmnElement == "Task_Renamed");

        Assert.Equal("Task_1_di", shape.Id);
    }

    [Fact]
    public void RenamingAProcessMovesWhatItContainsAndThePoolThatDrawsIt()
    {
        var renamed = Apply(Collaboration(), new RenameElementCommand("Process_1", "Process_Renamed")).Document;

        Assert.Equal("Process_Renamed", renamed.FindFlowNode("Task_1")!.ContainerId);
        Assert.Equal("Process_Renamed", renamed.Collaboration!.Participants[0].ProcessRef);
        Assert.Equal("Process_Renamed", Assert.Single(renamed.LaneSets).ContainerId);
    }

    [Fact]
    public void RenamingALaneKeepsItsMembershipAndItsSet()
    {
        var renamed = Apply(Collaboration(), new RenameElementCommand("Lane_1", "Lane_Renamed")).Document;

        var lane = Assert.Single(renamed.Lanes);

        Assert.Equal("Lane_Renamed", lane.Id);
        Assert.Contains("Task_1", lane.FlowNodeRefs);
        Assert.Contains("Lane_Renamed", Assert.Single(renamed.LaneSets).LaneIds);
    }

    [Fact]
    public void RenamingALocalCalledElementPreservesQNameNamespaceAndExternalNames()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:tns="https://example.test/target"
                         xmlns:ext="https://example.test/external"
                         id="Definitions_1" targetNamespace="https://example.test/target">
              <process id="Process_1" />
              <process id="Caller">
                <callActivity id="Call_local" calledElement="tns:Process_1" />
                <callActivity id="Call_external" calledElement="ext:Process_1" />
              </process>
            </definitions>
            """;
        var document = BpmnXmlReader.Read(xml).Document;

        var renamed = Apply(document, new RenameElementCommand("Process_1", "Process_Renamed")).Document;

        var local = renamed.FindFlowNode("Call_local")!.CalledElementQName!;
        Assert.Equal("Process_Renamed", local.LocalName);
        Assert.Equal("https://example.test/target", local.NamespaceUri);
        Assert.Equal("tns:Process_Renamed", local.LexicalValue);
        var external = renamed.FindFlowNode("Call_external")!.CalledElementQName!;
        Assert.Equal("Process_1", external.LocalName);
        Assert.Equal("https://example.test/external", external.NamespaceUri);
        Assert.Equal("ext:Process_1", external.LexicalValue);
    }

    // --- what is refused ---------------------------------------------------------------

    [Fact]
    public void ADuplicateIdIsRefused()
    {
        // D018 by another route: two elements sharing an id means every later reference resolves
        // to whichever the index happened to keep.
        var result = Apply(Collaboration(), new RenameElementCommand("Task_1", "Start_1"));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-074", Assert.Single(result.Rejections).Code);
    }

    [Theory]
    [InlineData("has a space")]
    [InlineData("has:a:colon")]
    [InlineData("1StartsWithADigit")]
    [InlineData("")]
    [InlineData("   ")]
    public void AnIdThatIsNotAnXmlNameIsRefused(string proposed)
    {
        // An id is an xsd:ID. A document with an invalid one is a document other tools refuse to
        // open, which is a worse failure than declining the edit (section 10.6).
        var result = Apply(Collaboration(), new RenameElementCommand("Task_1", proposed));

        Assert.False(result.IsApplied);
        Assert.Same(Collaboration().FindFlowNode("Task_1")!.Id, result.Document.FindFlowNode("Task_1")!.Id);
    }

    [Fact]
    public void RenamingSomethingThatIsNotThereIsRefused()
    {
        var result = Apply(Collaboration(), new RenameElementCommand("Nope_1", "Fine_1"));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-072", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesToRenameCatalogDefinitionsUntilTheirReferencesCanBeRewritten()
    {
        var document = Collaboration() with
        {
            Catalog = BpmnRootCatalog.Empty with { Messages = [new BpmnMessage("Message_1")] },
        };

        var result = Apply(document, new RenameElementCommand("Message_1", "Message_Renamed"));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-075", Assert.Single(result.Rejections).Code);
        Assert.NotNull(result.Document.Find("Message_1"));
        Assert.Null(result.Document.Find("Message_Renamed"));
    }

    [Fact]
    public void RenamingToTheSameIdChangesNothing()
    {
        // Applied, because nothing is wrong with it — but the document must be the same instance,
        // or the undo stack gains a step that undoes nothing.
        var document = Collaboration();
        var result = Apply(document, new RenameElementCommand("Task_1", "Task_1"));

        Assert.True(result.IsApplied);
        Assert.Same(document, result.Document);
    }

    [Fact]
    public void TheIdIsTrimmedRatherThanRefusedForPadding()
    {
        var result = Apply(Collaboration(), new RenameElementCommand("Task_1", "  Task_Padded  "));

        Assert.True(result.IsApplied, Codes(result));
        Assert.NotNull(result.Document.FindFlowNode("Task_Padded"));
    }

    // --- undo -------------------------------------------------------------------------

    [Fact]
    public void UndoPutsEveryReferenceBack()
    {
        var history = new BpmnEditHistory(Collaboration());

        history.Apply(new RenameElementCommand("Task_1", "Task_Renamed"));
        var restored = history.Undo();

        Assert.Equal(
            BpmnReferenceMap.UsagesOf(Collaboration(), "Task_1").Count,
            BpmnReferenceMap.UsagesOf(restored, "Task_1").Count);
    }

    // --- a real file ------------------------------------------------------------------

    [Fact]
    public void RenamingInEveryFixtureLeavesItNoWorse()
    {
        /*
         * The fixtures are the only documents here nobody wrote to be renamed: nested
         * Sub-Processes, Boundary Events, Message Flows across Pools, Data Associations. If a site
         * is missing from the map, one of them acquires a diagnostic it did not have.
         */
        foreach (var path in Directory.EnumerateFiles(SamplesPath(), "*.bpmn"))
        {
            var document = BpmnXmlReader.Read(File.ReadAllText(path)).Document;
            var target = document.FlowNodes.Count > 0 ? document.FlowNodes[0] : null;

            if (target is null) continue;

            var before = BpmnValidator.Validate(document).Diagnostics.Count;
            var result = Apply(document, new RenameElementCommand(target.Id, "Renamed_By_The_Test"));

            Assert.True(result.IsApplied, $"{Path.GetFileName(path)}: {Codes(result)}");
            Assert.Empty(BpmnReferenceMap.UsagesOf(result.Document, target.Id));
            Assert.Equal(before, BpmnValidator.Validate(result.Document).Diagnostics.Count);
        }
    }

    [Fact]
    public void TwoRenamesInARowBehaveLikeOne()
    {
        // What the browser did: rename, then try to rename the result onto an id that is taken.
        // The second must be refused and must leave the first one standing.
        var history = new BpmnEditHistory(Collaboration());

        var once = history.Apply(new RenameElementCommand("Task_1", "Task_Renamed"));
        Assert.True(once.IsApplied, Codes(once));
        Assert.NotNull(once.Document.FindFlowNode("Task_Renamed"));

        var twice = history.Apply(new RenameElementCommand("Task_Renamed", "Start_1"));

        Assert.False(twice.IsApplied);
        Assert.Equal("EDIT-074", Assert.Single(twice.Rejections).Code);
        Assert.NotNull(twice.Document.FindFlowNode("Task_Renamed"));
        Assert.NotNull(twice.Document.FindFlowNode("Start_1"));
    }

    // --- helpers ----------------------------------------------------------------------

    private static BpmnEditResult Apply(BpmnDocument document, BpmnEditCommand command) =>
        BpmnDocumentEditor.Apply(document, command);

    private static string Codes(BpmnEditResult result) =>
        string.Join(", ", result.Rejections.Select(rejection => $"{rejection.Code}: {rejection.Message}"));

    private static string SamplesPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "playground")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("the repository root was not found"),
            "playground",
            "Bpmn.Demo",
            "wwwroot",
            "samples");
    }

    /// <summary>A Pool over a Process with a Lane, so containment and DI are both in play.</summary>
    private static BpmnDocument Collaboration()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Start_1", BpmnFlowNodeType.StartEvent, new Bounds(160, 140, 36, 36), "Start")
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(260, 120, 100, 80), "Work")
            .Node("End_1", BpmnFlowNodeType.EndEvent, new Bounds(420, 140, 36, 36), "Done")
            .Flow("Flow_1", "Start_1", "Task_1", [new DiPoint(196, 158), new DiPoint(260, 158)])
            .Flow("Flow_2", "Task_1", "End_1", [new DiPoint(360, 158), new DiPoint(420, 158)])
            .Build();

        var processId = document.Processes[0].Id;

        return document with
        {
            Collaboration = new BpmnCollaboration("Collaboration_1", null,
                [new BpmnParticipant("Pool_1", "Pool", processId)]),
            LaneSets = [new BpmnLaneSet("LaneSet_1", processId, ["Lane_1"])],
            Lanes = [new BpmnLane("Lane_1", "Lane", "LaneSet_1", ["Start_1", "Task_1", "End_1"])],
        };
    }
}
