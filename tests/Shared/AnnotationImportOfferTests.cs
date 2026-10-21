using Bunit;

using Hasseware.AspNetCore.Components.Bpmn.Configuration;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class AnnotationImportOfferTests : BunitContext
{
    public AnnotationImportOfferTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        KitFixture.Register(Services);
    }

    [Fact]
    public async Task RequiresExplicitAnnotationConsentAndResetsConsentForNextFile()
    {
        var rendered = this.Render<BpmnPart>(parameters => parameters.Add(part => part.Part, BpmnEditorPart.ImportOffer), canvas: false);
        await rendered.Scope.InvokeAsync(() => rendered.Session.PreviewImport("notes.bpmn", Xml));

        var offer = rendered.Cut;
        Assert.Contains("Note", offer.Find("[role=alert]").TextContent, StringComparison.Ordinal);
        Assert.Contains("Extra", offer.Find("[role=alert]").TextContent, StringComparison.Ordinal);
        Assert.True(offer.Find(".bpmn-import__accept").HasAttribute("disabled"));

        await KitFixture.ConfirmAnnotationNormalizationAsync(offer);
        Assert.False(offer.Find(".bpmn-import__accept").HasAttribute("disabled"));

        await rendered.Scope.InvokeAsync(() => rendered.Session.PreviewImport("next.bpmn", Xml));
        Assert.True(offer.Find(".bpmn-import__accept").HasAttribute("disabled"));
        offer.Find(".bpmn-import__cancel").Click();
        Assert.Null(rendered.Session.PendingImport);
    }

    [Fact]
    public async Task ConfirmedOfferOpensNormalizedDocument()
    {
        var rendered = this.Render<BpmnPart>(parameters => parameters.Add(part => part.Part, BpmnEditorPart.ImportOffer), canvas: false);
        await rendered.Scope.InvokeAsync(() => rendered.Session.PreviewImport("notes.bpmn", Xml));

        await KitFixture.ConfirmAnnotationNormalizationAsync(rendered.Cut);
        rendered.Cut.Find(".bpmn-import__accept").Click();

        Assert.Null(rendered.Session.PendingImport);
        Assert.Equal("First", Assert.Single(rendered.Session.EditedDocument!.Associations).Id);
        Assert.Single(rendered.Session.EditedDocument.TextAnnotations);
    }

    private const string Xml = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="Definitions" targetNamespace="urn:test">
          <process id="Process">
            <task id="A" /><task id="B" /><textAnnotation id="Note"><text>Note</text></textAnnotation>
            <association id="First" sourceRef="Note" targetRef="A" />
            <association id="Extra" sourceRef="B" targetRef="Note" />
          </process>
        </definitions>
        """;
}
