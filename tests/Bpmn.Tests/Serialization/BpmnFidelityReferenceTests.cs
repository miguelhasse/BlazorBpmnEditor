using System.Xml.Linq;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Serialization;

public sealed class BpmnFidelityReferenceTests
{
    private static readonly XNamespace Model = "http://www.omg.org/spec/BPMN/20100524/MODEL";

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void CorrectFlowReferencesPreserveWhitespaceAttributesAndNodeIdentity(int otherOwners)
    {
        var root = XElement.Parse("""
            <process xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL">
              <task id="task"><incoming keep="yes"> flow_in </incoming><!--keep-->
                <outgoing keep="yes"> flow_out </outgoing></task>
            </process>
            """, LoadOptions.PreserveWhitespace);
        var original = new XElement(root);
        var incoming = Assert.Single(root.Descendants(Model + "incoming"));
        var outgoing = Assert.Single(root.Descendants(Model + "outgoing"));
        var document = new BpmnDocument
        {
            Definitions = new("definitions", "urn:test"),
            SequenceFlows = [new("flow_in", "source", "task", "process"), new("flow_out", "task", "target", "process")],
        };

        new BpmnFidelityWriter(document, []).SynchroniseFlowReferences(WrapWithOtherOwners(root, otherOwners));

        Assert.True(XNode.DeepEquals(original, root));
        Assert.Same(incoming, Assert.Single(root.Descendants(Model + "incoming")));
        Assert.Same(outgoing, Assert.Single(root.Descendants(Model + "outgoing")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void ChangedFlowReferencesPreserveModelOrderAndDuplicateFlowIds(int otherOwners)
    {
        var root = XElement.Parse("""
            <process xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL">
              <task id="task"><incoming>old</incoming><!--keep--><outgoing>old</outgoing></task>
            </process>
            """);
        var document = new BpmnDocument
        {
            Definitions = new("definitions", "urn:test"),
            SequenceFlows = [new("second", "source", "task", "process"), new("first", "source", "task", "process"),
                new("first", "source", "task", "process"), new("loop", "task", "task", "process"),
                new("out", "task", "target", "process")],
        };

        new BpmnFidelityWriter(document, []).SynchroniseFlowReferences(WrapWithOtherOwners(root, otherOwners));

        Assert.Equal(["second", "first", "first", "loop"], root.Descendants(Model + "incoming").Select(item => item.Value));
        Assert.Equal(["loop", "out"], root.Descendants(Model + "outgoing").Select(item => item.Value));
        Assert.Single(root.DescendantNodes().OfType<XComment>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void RemovedFlowsRemoveOnlyStoredReferencesOnIdentifiedOwners(int otherOwners)
    {
        var root = XElement.Parse("""
            <process xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:vendor="urn:vendor">
              <task id="task"><incoming>old1</incoming><incoming>old2</incoming>
                <outgoing>old3</outgoing><vendor:incoming>foreign</vendor:incoming><!--keep--></task>
              <task><incoming>unidentified</incoming></task>
            </process>
            """);
        var document = new BpmnDocument { Definitions = new("definitions", "urn:test") };

        new BpmnFidelityWriter(document, []).SynchroniseFlowReferences(WrapWithOtherOwners(root, otherOwners));

        Assert.Equal("unidentified", Assert.Single(root.Descendants(Model + "incoming")).Value);
        Assert.Empty(root.Descendants(Model + "outgoing"));
        Assert.Equal("foreign", Assert.Single(root.Descendants(XName.Get("incoming", "urn:vendor"))).Value);
        Assert.Single(root.DescendantNodes().OfType<XComment>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void StoredFlowReferencesDoNotIntroduceAbsentDirection(int otherOwners)
    {
        var root = XElement.Parse("""
            <process xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL">
              <task id="task"><incoming>stale</incoming></task>
              <task id="other" />
            </process>
            """);
        var document = new BpmnDocument
        {
            Definitions = new("definitions", "urn:test"),
            SequenceFlows = [new("in", "other", "task", "process"), new("out", "task", "other", "process")],
        };

        new BpmnFidelityWriter(document, []).SynchroniseFlowReferences(WrapWithOtherOwners(root, otherOwners));

        Assert.Equal("in", Assert.Single(root.Descendants(Model + "incoming")).Value);
        Assert.Empty(root.Descendants(Model + "outgoing"));
        Assert.False(root.Elements(Model + "task").Last().HasElements);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void DuplicateFlowReferenceOwnersDoNotShareXmlNodes(int otherOwners)
    {
        var root = XElement.Parse("""
            <process xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL">
              <task id="task"><outgoing>stale</outgoing></task>
              <task id="task"><outgoing>stale</outgoing></task>
            </process>
            """);
        var document = new BpmnDocument
        {
            Definitions = new("definitions", "urn:test"),
            SequenceFlows = [new("out", "task", "other", "process")],
        };

        new BpmnFidelityWriter(document, []).SynchroniseFlowReferences(WrapWithOtherOwners(root, otherOwners));

        Assert.Equal(["out", "out"], root.Descendants(Model + "outgoing").Select(item => item.Value));
        Assert.All(root.Elements(), owner => Assert.Single(owner.Elements()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void ReusedWriterObservesChangedHostFlowList(int otherOwners)
    {
        var root = XElement.Parse("""
            <process xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL">
              <task id="task"><outgoing>before</outgoing></task>
            </process>
            """);
        var flows = new List<BpmnSequenceFlow> { new("before", "task", "other", "process") };
        flows.AddRange(Enumerable.Range(0, otherOwners)
            .Select(index => new BpmnSequenceFlow("stale", $"other_{index}", "sink", "process")));
        var writer = new BpmnFidelityWriter(new BpmnDocument
        {
            Definitions = new("definitions", "urn:test"),
            SequenceFlows = flows,
        }, []);
        var wrapped = WrapWithOtherOwners(root, otherOwners);
        writer.SynchroniseFlowReferences(wrapped);
        flows[0] = new("after", "task", "other", "process");

        writer.SynchroniseFlowReferences(wrapped);

        Assert.Equal("after", Assert.Single(root.Descendants(Model + "outgoing")).Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void DetachedNestedOwnersStillReceiveReferenceUpdates(int otherOwners)
    {
        var root = XElement.Parse("""
            <process xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL">
              <task id="outer"><outgoing><task id="nested"><outgoing>stale</outgoing></task></outgoing></task>
            </process>
            """);
        var nested = root.Descendants(Model + "task").Last();
        var document = new BpmnDocument
        {
            Definitions = new("definitions", "urn:test"),
            SequenceFlows = [new("outer_flow", "outer", "sink", "process"), new("nested_flow", "nested", "sink", "process")],
        };

        new BpmnFidelityWriter(document, []).SynchroniseFlowReferences(WrapWithOtherOwners(root, otherOwners));

        Assert.Equal("outer_flow", Assert.Single(root.Descendants(Model + "outgoing")).Value);
        Assert.DoesNotContain(nested, root.Descendants());
        Assert.Equal("nested_flow", Assert.Single(nested.Elements(Model + "outgoing")).Value);
    }

    [Fact]
    public void ChangedFlowReferenceExportPreservesRetainedSource()
    {
        var document = BpmnXmlReader.Read("""
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="definitions" targetNamespace="urn:test">
              <process id="process">
                <task id="first"><outgoing>flow</outgoing></task>
                <task id="second"><incoming>flow</incoming></task>
                <sequenceFlow id="flow" sourceRef="first" targetRef="second" />
              </process>
            </definitions>
            """).Document;
        var before = BpmnXmlWriter.Write(document, BpmnWriteOptions.Fidelity);
        var changed = document with { SequenceFlows = [] };

        var exported = XDocument.Parse(BpmnXmlWriter.Write(changed, BpmnWriteOptions.Fidelity).Xml);

        Assert.Empty(exported.Descendants(Model + "incoming"));
        Assert.Empty(exported.Descendants(Model + "outgoing"));
        var unchanged = BpmnXmlWriter.Write(document, BpmnWriteOptions.Fidelity);
        Assert.Equal(before.Xml, unchanged.Xml);
        Assert.Equal(before.Diagnostics, unchanged.Diagnostics);
    }

    private static XElement WrapWithOtherOwners(XElement root, int count) =>
        new("envelope", root, Enumerable.Range(0, count).Select(index =>
            new XElement(Model + "task", new XAttribute("id", $"other_{index}"),
                new XElement(Model + "outgoing", "stale"))));
}
