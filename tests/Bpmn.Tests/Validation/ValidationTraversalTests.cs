using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Validation;

public sealed class ValidationTraversalTests
{
    [Theory]
    [InlineData(BpmnFlowNodeType.StartEvent, "D001", true)]
    [InlineData(BpmnFlowNodeType.EndEvent, "D002", false)]
    [InlineData(BpmnFlowNodeType.BoundaryEvent, "D003", true)]
    [InlineData(BpmnFlowNodeType.IntermediateThrowEvent, "SF-017", false)]
    [InlineData(BpmnFlowNodeType.IntermediateCatchEvent, "SF-018", true)]
    public void EventFlowDiagnosticsKeepFirstFlowAndTotalCount(BpmnFlowNodeType type, string rule, bool incoming)
    {
        var document = Document(
            [new("event", type, "process", EventDefinition: BpmnEventDefinitionType.Link, AttachedToRef: type == BpmnFlowNodeType.BoundaryEvent ? "host" : null),
                new("host", BpmnFlowNodeType.Task, "process")],
            [new("secondLexically", incoming ? "host" : "event", incoming ? "event" : "host", "process"),
                new("firstLexically", incoming ? "host" : "event", incoming ? "event" : "host", "process")]);

        var diagnostic = Assert.Single(BpmnValidator.Validate(document).Diagnostics, diagnostic => diagnostic.RuleId == rule);

        Assert.Equal("secondLexically", diagnostic.RelatedId);
        Assert.Equal(new object?[] { "'event'", 2 }, diagnostic.MessageArguments);
        Assert.Equal($"{rule}.Detail", diagnostic.DetailResourceKey);
    }

    [Theory]
    [InlineData(false, "unconditional", "D004.Detail.2", 1)]
    [InlineData(true, "first", "D004.Detail", 2)]
    public void GatewayDiagnosticKeepsFirstUnconditionalOrFirstFlow(bool multipleDefaults, string related, string detail, int count)
    {
        var document = Document(
            [new("gateway", BpmnFlowNodeType.ExclusiveGateway, "process"), new("target", BpmnFlowNodeType.Task, "process")],
            [new("first", "gateway", "target", "process", ConditionExpression: "ready", IsDefault: multipleDefaults),
                new("unconditional", "gateway", "target", "process", IsDefault: multipleDefaults)]);

        var diagnostic = Assert.Single(BpmnValidator.Validate(document).Diagnostics, diagnostic => diagnostic.RuleId == "D004");

        Assert.Equal(related, diagnostic.RelatedId);
        Assert.Equal(detail, diagnostic.DetailResourceKey);
        Assert.Equal(new object?[] { "'gateway'", count }, diagnostic.MessageArguments);
    }

    [Fact]
    public void ReachabilityKeepsFirstSeenContainerAndNodeOrder()
    {
        var document = Document(
            [new("bStart", BpmnFlowNodeType.StartEvent, "b"),
                new("aStart", BpmnFlowNodeType.StartEvent, "a"),
                new("bSecond", BpmnFlowNodeType.Task, "b"),
                new("aFirst", BpmnFlowNodeType.Task, "a"),
                new("bFirst", BpmnFlowNodeType.Task, "b"),
                new("bEnd", BpmnFlowNodeType.EndEvent, "b"),
                new("aEnd", BpmnFlowNodeType.EndEvent, "a")],
            [new("bFlow", "bStart", "bEnd", "b"), new("aFlow", "aStart", "aEnd", "a")]) with
        { Processes = [new("b"), new("a")] };

        var diagnostics = BpmnValidator.Validate(document).Diagnostics.Where(diagnostic => diagnostic.RuleId == "D009").ToArray();

        Assert.Equal(["bSecond", "bFirst", "aFirst"], diagnostics.Select(diagnostic => diagnostic.ElementId));
        Assert.All(diagnostics, diagnostic => Assert.Equal("D009.Detail", diagnostic.DetailResourceKey));
    }

    [Fact]
    public void ReachabilityFollowsBoundaryEscapeAndTerminatesOnCycles()
    {
        var document = Document(
            [new("start", BpmnFlowNodeType.StartEvent, "process"),
                new("host", BpmnFlowNodeType.Task, "process"),
                new("boundary", BpmnFlowNodeType.BoundaryEvent, "process",
                    EventDefinition: BpmnEventDefinitionType.Timer, AttachedToRef: "host"),
                new("escape", BpmnFlowNodeType.Task, "process"),
                new("end", BpmnFlowNodeType.EndEvent, "process")],
            [new("begin", "start", "host", "process"), new("finish", "host", "end", "process"),
                new("escapeFlow", "boundary", "escape", "process"), new("cycle", "escape", "host", "process")]);

        Assert.DoesNotContain(BpmnValidator.Validate(document).Diagnostics, diagnostic => diagnostic.RuleId == "D009");
    }

    [Fact]
    public void ReachabilityRetainsTraversalAcrossMalformedContainerBoundaries()
    {
        var document = Document(
            [new("start", BpmnFlowNodeType.StartEvent, "a"), new("remote", BpmnFlowNodeType.Task, "b"),
                new("end", BpmnFlowNodeType.EndEvent, "a")],
            [new("out", "start", "remote", "a"), new("back", "remote", "end", "b")]) with
        { Processes = [new("a"), new("b")] };

        var report = BpmnValidator.Validate(document);

        Assert.Contains(report.Diagnostics, diagnostic => diagnostic.RuleId == "SF-007");
        Assert.DoesNotContain(report.Diagnostics, diagnostic => diagnostic.RuleId == "D009");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReachabilitySkipsContainersWithoutStartsOrEnds(bool start)
    {
        var document = Document(
            [new("event", start ? BpmnFlowNodeType.StartEvent : BpmnFlowNodeType.EndEvent, "process"),
                new("task", BpmnFlowNodeType.Task, "process")],
            [new("flow", start ? "event" : "task", start ? "task" : "event", "process")]);

        Assert.DoesNotContain(BpmnValidator.Validate(document).Diagnostics, diagnostic => diagnostic.RuleId == "D009");
    }

    [Fact]
    public void EventSubProcessesCountOnlyTheirOwnStarts()
    {
        var document = Document(
            [new("second", BpmnFlowNodeType.EventSubProcess, "process", FlowElementsOwned: true),
                new("first", BpmnFlowNodeType.EventSubProcess, "process", FlowElementsOwned: true),
                new("start1", BpmnFlowNodeType.StartEvent, "first", EventDefinition: BpmnEventDefinitionType.Timer),
                new("start2", BpmnFlowNodeType.StartEvent, "first", EventDefinition: BpmnEventDefinitionType.Timer)], []);

        var diagnostics = BpmnValidator.Validate(document).Diagnostics.Where(diagnostic => diagnostic.RuleId == "D010").ToArray();

        Assert.Equal(["second", "first"], diagnostics.Select(diagnostic => diagnostic.ElementId));
        Assert.Equal(new object?[] { "'second'", 0 }, diagnostics[0].MessageArguments);
        Assert.Equal(new object?[] { "'first'", 2 }, diagnostics[1].MessageArguments);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(600)]
    public void AnnotationDiagnosticsCountSelfReferencesOnceAndDuplicateAssociationsSeparately(int count)
    {
        var notes = Enumerable.Range(0, count).Select(index => new BpmnTextAnnotation($"note{index}", "Note", "process")).ToArray();
        var associations = Enumerable.Range(0, count).Select(index =>
            new BpmnAssociation($"self{index}", $"note{index}", $"note{index}", "process")).ToList();
        associations.Add(new("extraLast", $"note{count - 1}", $"note{count - 1}", "process"));
        associations.Add(new("extraFirst", "note0", "note0", "process"));
        var document = Document([], []) with { TextAnnotations = notes, Associations = associations };

        var diagnostics = BpmnValidator.Validate(document).Diagnostics.Where(diagnostic => diagnostic.RuleId == "AN-001").ToArray();

        Assert.Equal(["note0", $"note{count - 1}"], diagnostics.Select(diagnostic => diagnostic.ElementId));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AnnotationValidationPreservesNullHostReferenceBehavior(bool nullId, bool reverse)
    {
        var notes = Enumerable.Range(0, 65).Select(index => new BpmnTextAnnotation($"note{index}", "Note", "process")).ToArray();
        if (nullId) notes[0] = notes[0] with { Id = null! };
        var associations = Enumerable.Range(0, 65).Select(index =>
            new BpmnAssociation($"association{index}", notes[index].Id, "missing", "process")).ToList();
        associations.Add(new("nullEndpoint", reverse ? notes[0].Id : null!, reverse ? null! : notes[0].Id, "process"));
        var document = Document([], []) with { TextAnnotations = notes, Associations = associations };

        if (nullId)
        {
            Assert.Throws<ArgumentNullException>(() => BpmnValidator.Validate(document));
            return;
        }

        var diagnostic = Assert.Single(BpmnValidator.Validate(document).Diagnostics, diagnostic => diagnostic.RuleId == "AN-001");

        Assert.Equal(notes[0].Id, diagnostic.ElementId);
    }

    [Fact]
    public void ContainerGroupingDoesNotSurviveTheValidationOperation()
    {
        var nodes = new List<BpmnFlowNode>
        {
            new("start", BpmnFlowNodeType.StartEvent, "process"),
            new("end", BpmnFlowNodeType.EndEvent, "process"),
        };
        var document = Document(nodes, [new("flow", "start", "end", "process")]);
        Assert.DoesNotContain(BpmnValidator.Validate(document).Diagnostics, diagnostic => diagnostic.RuleId == "D009");
        nodes.Add(new("orphan", BpmnFlowNodeType.Task, "process"));

        var diagnostic = Assert.Single(BpmnValidator.Validate(document).Diagnostics, diagnostic => diagnostic.RuleId == "D009");

        Assert.Equal("orphan", diagnostic.ElementId);
    }

    [Fact]
    public void FlatValidationAvoidsPerNodeFlowCopiesAndTraversalIterators()
    {
        var nodes = Enumerable.Range(0, 600).Select(index => new BpmnFlowNode(
            $"node{index}", index == 0 ? BpmnFlowNodeType.StartEvent
                : index == 599 ? BpmnFlowNodeType.EndEvent : BpmnFlowNodeType.Task, "process")).ToArray();
        var flows = Enumerable.Range(1, 599).Select(index => new BpmnSequenceFlow(
            $"flow{index}", $"node{index - 1}", $"node{index}", "process")).ToArray();
        var document = Document(nodes, flows);
        for (var index = 0; index < 10; index++) BpmnValidator.Validate(document);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var report = BpmnValidator.Validate(document);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Empty(report.Diagnostics);
        Assert.InRange(allocated, 0, 560_000);
    }

    private static BpmnDocument Document(IReadOnlyList<BpmnFlowNode> nodes, IReadOnlyList<BpmnSequenceFlow> flows) => new()
    {
        Definitions = new("definitions", "urn:validation-tests"),
        Processes = [new("process")],
        FlowNodes = nodes,
        SequenceFlows = flows,
    };
}
