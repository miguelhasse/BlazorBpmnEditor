using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class AnnotationInspectionPerformanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnnotationFreeInspectionPreservesSourceAndNeedsNoConsent(bool json)
    {
        var document = new BpmnDocumentBuilder()
            .Node("a", BpmnFlowNodeType.Task, new Bounds(0, 0, 100, 80))
            .Node("b", BpmnFlowNodeType.Task, new Bounds(200, 0, 100, 80))
            .Association("association", "a", "b", [new(100, 40), new(150, 100), new(200, 40)])
            .Build();
        var content = json ? BpmnJsonWriter.Write(document) : BpmnXmlWriter.Write(document).Xml;
        var preview = BpmnImportPreview.Of(json ? "test.json" : "test.bpmn", content);
        var before = BpmnXmlWriter.Write(preview.Document!, BpmnWriteOptions.Fidelity).Xml;

        var normalization = BpmnAnnotationNormalization.Inspect(preview.Document!);

        Assert.True(preview.CanAccept);
        Assert.False(normalization.RequiresConfirmation);
        Assert.Empty(normalization.AnnotationIds);
        Assert.Empty(normalization.RemovedAssociationIds);
        Assert.Empty(normalization.StraightenedEdgeIds);
        Assert.Equal(before, BpmnXmlWriter.Write(preview.Document!, BpmnWriteOptions.Fidelity).Xml);
    }

    [Fact]
    public void AnnotationFreeInspectionAllocatesIndependentlyOfSourceSize()
    {
        var builder = new BpmnDocumentBuilder();
        for (var index = 0; index < 600; index++)
            builder.Node($"task{index}", BpmnFlowNodeType.Task, new Bounds(index * 200, 0, 100, 80));
        var document = BpmnXmlReader.Read(BpmnXmlWriter.Write(builder.Build()).Xml).Document;
        for (var warm = 0; warm < 3; warm++) BpmnAnnotationNormalization.Inspect(document);
        var before = GC.GetAllocatedBytesForCurrentThread();

        var result = BpmnAnnotationNormalization.Inspect(document);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.False(result.RequiresConfirmation);
        Assert.InRange(allocated, 0, 256);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InspectionPreservesOrderingAndDuplicateSemantics(bool retainedSource)
    {
        var builder = new BpmnDocumentBuilder()
            .Node("task", BpmnFlowNodeType.Task, new Bounds(0, 0, 100, 80));
        for (var index = 0; index < 8; index++)
        {
            builder.TextAnnotation($"note{index}", "Note", new Bounds(index * 120, 160, 80, 40))
                .Association($"valid{index}", $"note{index}", "task", [new(0, 160), new(0, 120), new(0, 80)])
                .Association($"extra{index}", "task", $"note{index}", [new(0, 80), new(0, 160)]);
        }
        var document = builder.Build();
        if (retainedSource)
        {
            document = BpmnXmlReader.Read(BpmnXmlWriter.Write(document).Xml).Document;
            document = document with { Associations = [.. document.Associations.Reverse()] };
        }
        var associations = new List<BpmnAssociation>(document.Associations)
        {
            new("self", "note0", "note0", "Process_1"),
            new("missing", "note1", "missing", "Process_1"),
            new("duplicate", "note2", "task", "Process_1"),
            new("duplicate", "task", "note2", "Process_1"),
        };
        document = document with { Associations = associations };
        var expected = BpmnAnnotationNormalization.Inspect(document);
        var indexedDocument = document with
        { TextAnnotations = [.. document.TextAnnotations, new("unused", "Unused", "Process_1")] };
        var originalSource = document.Source?.Clone().ToString();
        var originalJson = BpmnJsonWriter.Write(document);

        var actual = BpmnAnnotationNormalization.Inspect(indexedDocument);

        Assert.Equal(expected.AnnotationIds, actual.AnnotationIds);
        Assert.Equal(expected.RemovedAssociationIds, actual.RemovedAssociationIds);
        Assert.Equal(expected.StraightenedEdgeIds, actual.StraightenedEdgeIds);
        Assert.Equal(originalSource, document.Source?.Clone().ToString());
        Assert.Equal(originalJson, BpmnJsonWriter.Write(document));
    }

    [Fact]
    public void ConnectionIndexMatchesScansIncludingSelfAndDuplicateConnections()
    {
        var document = new BpmnDocumentBuilder()
            .Node("task", BpmnFlowNodeType.Task, new Bounds(0, 0, 100, 80))
            .TextAnnotation("note", "Note", new Bounds(0, 160, 80, 40)).Build();
        document = document with
        {
            Associations =
            [
                new("self", "note", "note", "Process_1"),
                new("same", "note", "task", "Process_1"),
                new("same", "task", "note", "Process_1"),
                new("missing", "missing", "note", "Process_1"),
            ],
        };

        var indexed = BpmnAnnotationAttachments.ConnectionsByElement(document);

        foreach (var id in new[] { "note", "task", "missing", "absent" })
        {
            Assert.Equal(BpmnAnnotationAttachments.Connections(document, id),
                indexed.GetValueOrDefault(id) ?? []);
            Assert.Equal(BpmnAnnotationAttachments.UniqueConnection(document, id),
                BpmnAnnotationAttachments.UniqueConnection(document, id, indexed));
        }
        Assert.Equal(4, indexed["note"].Count);
    }
}
