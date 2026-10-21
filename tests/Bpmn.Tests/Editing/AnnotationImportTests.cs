using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class AnnotationImportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviewReportsMultipleTargetsAndBendsWithoutMutatingDocument(bool json)
    {
        var preview = BpmnImportPreview.Of(json ? "notes.json" : "notes.bpmn", Content(json));

        Assert.True(preview.CanAccept);
        Assert.True(preview.AnnotationNormalization.RequiresConfirmation);
        Assert.Equal(["Note"], preview.AnnotationNormalization.AnnotationIds);
        Assert.Equal(["Extra"], preview.AnnotationNormalization.RemovedAssociationIds);
        Assert.Equal(["First_di"], preview.AnnotationNormalization.StraightenedEdgeIds);
        Assert.Equal(2, preview.Document!.Associations.Count);
        Assert.Equal(3, preview.Document.Diagrams[0].Plane.Edges.Single(edge => edge.BpmnElement == "First").Waypoints.Count);
    }

    [Fact]
    public async Task MissingConsentRejectsImportWithoutChangingHistoryOrPreview()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var session = Session(services);
        var original = session.EditedDocument;
        var preview = session.PreviewImport("notes.bpmn", Content(false));

        await Assert.ThrowsAsync<InvalidOperationException>(session.AcceptImportAsync);

        Assert.Same(original, session.EditedDocument);
        Assert.Same(preview, session.PendingImport);
        Assert.False(session.CanUndo);
        session.CancelImport();
        Assert.Same(original, session.EditedDocument);
        Assert.Null(session.PendingImport);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmedImportNormalizesBothFormatsAndFidelityCannotResurrectLosses(bool json)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var session = Session(services);
        session.PreviewImport(json ? "notes.json" : "notes.bpmn", Content(json));

        await session.AcceptImportAsync(true);

        var normalized = session.EditedDocument!;
        Assert.Equal("First", Assert.Single(normalized.Associations).Id);
        Assert.Equal(BpmnAssociationDirection.One, normalized.Associations[0].Direction);
        Assert.Equal(2, normalized.Diagrams[0].Plane.Edges.Single(edge => edge.BpmnElement == "First").Waypoints.Count);
        Assert.DoesNotContain(normalized.Diagrams[0].Plane.Edges, edge => edge.BpmnElement == "Extra");
        Assert.Null(session.PendingImport);
        Assert.False(session.CanUndo);
        var fidelity = BpmnXmlWriter.Write(normalized, BpmnWriteOptions.Fidelity).Xml;
        var reloaded = BpmnXmlReader.Read(fidelity).Document;
        Assert.Equal("First", Assert.Single(reloaded.Associations).Id);
        Assert.Equal(2, reloaded.Diagrams[0].Plane.Edges.Single(edge => edge.BpmnElement == "First").Waypoints.Count);
        Assert.False(BpmnImportPreview.Of("again.bpmn", fidelity).AnnotationNormalization.RequiresConfirmation);
        Assert.Equal(fidelity, BpmnXmlWriter.Write(normalized, BpmnWriteOptions.Fidelity).Xml);
    }

    [Fact]
    public void OriginalSourceOrderWinsOverModelCollectionOrder()
    {
        var document = BpmnXmlReader.Read(Content(false)).Document;
        document = document with { Associations = [.. document.Associations.Reverse()] };

        var normalization = BpmnAnnotationNormalization.Inspect(document);

        Assert.Equal(["Extra"], normalization.RemovedAssociationIds);
        Assert.Equal("First", Assert.Single(normalization.Apply(document).Associations).Id);
    }

    [Fact]
    public void RawReaderWriterPreservesNonconformingAnnotations()
    {
        var document = BpmnXmlReader.Read(Content(false)).Document;
        var roundTrip = BpmnXmlReader.Read(BpmnXmlWriter.Write(document, BpmnWriteOptions.Fidelity).Xml).Document;

        Assert.Equal(2, roundTrip.Associations.Count);
        Assert.Equal(3, roundTrip.Diagrams[0].Plane.Edges.Single(edge => edge.BpmnElement == "First").Waypoints.Count);
    }

    private static BpmnEditorSession Session(IServiceProvider services)
    {
        var session = new BpmnEditorSession(new BpmnOptions(), services) { Document = Document() };
        session.UpdateParameters(EditorConfig.CreateDefault());
        return session;
    }

    private static string Content(bool json) =>
        json ? BpmnJsonWriter.Write(Document()) : BpmnXmlWriter.Write(Document()).Xml;

    private static BpmnDocument Document()
    {
        var document = new BpmnDocumentBuilder()
            .Node("A", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Node("B", BpmnFlowNodeType.Task, new Bounds(500, 100, 100, 80))
            .TextAnnotation("Note", "Note", new Bounds(280, 300, 100, 50))
            .Build();
        return document with
        {
            Associations =
            [
                new BpmnAssociation("First", "Note", "A", "Process_1", BpmnAssociationDirection.One),
                new BpmnAssociation("Extra", "B", "Note", "Process_1"),
            ],
            Diagrams = [document.Diagrams[0] with
            {
                Plane = document.Diagrams[0].Plane with
                {
                    Edges =
                    [
                        new BpmnEdge("First_di", "First", [new DiPoint(280, 300), new DiPoint(220, 240), new DiPoint(200, 180)]),
                        new BpmnEdge("Extra_di", "Extra", [new DiPoint(500, 180), new DiPoint(380, 300)]),
                    ],
                },
            }],
        };
    }
}
