using Bunit;

using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Storage;

using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>
/// The notice shown when another party changes the stored document the editor has open (design doc,
/// "External change notification").
/// </summary>
public sealed class ExternalChangeOfferTests : BunitContext
{
    private const string Xml =
        """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="d" targetNamespace="t">
          <process id="p"><startEvent id="s" name="Start" /></process>
        </definitions>
        """;

    private static readonly BpmnDocumentKey Key = new(null, BpmnDocumentPath.Parse("claims/intake.bpmn"));

    public ExternalChangeOfferTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddBpmnEditor().AddBpmnInMemoryDocumentStore().AddBpmnDocumentChanges();
        var canvas = JSInterop.SetupModule(BpmnInteropContract.ModulePath).SetupModule("initialize", _ => true);
        canvas.Setup<CanvasInitResult>("getInitResult", _ => true)
            .SetResult(new CanvasInitResult("instance-1", BpmnInteropContract.Version, 1, true, null));
    }

    [Fact]
    public async Task ShowsNothingWhileTheStoredDocumentIsUnchanged()
    {
        var (editor, _) = await RenderBoundAsync();

        Assert.Empty(editor.FindAll(".bpmn-change"));
    }

    [Fact]
    public async Task OffersReloadSaveACopyAndDismissAfterAnUpdate()
    {
        var (editor, _) = await RenderBoundAsync();

        await WriteAsTheOtherPartyAsync();

        editor.WaitForAssertion(() =>
        {
            var notice = editor.Find(".bpmn-change");
            Assert.Equal("alert", notice.GetAttribute("role"));
            Assert.Contains("intake.bpmn", editor.Find(".bpmn-change__message").TextContent, StringComparison.Ordinal);
            Assert.Equal(3, editor.FindAll(".bpmn-change__action").Count);
        });
    }

    [Fact]
    public async Task ReloadTakesTheOtherVersion()
    {
        var (editor, binding) = await RenderBoundAsync();
        var theirs = await WriteAsTheOtherPartyAsync();
        editor.WaitForAssertion(() => editor.Find(".bpmn-change__action--primary"));

        await editor.Find(".bpmn-change__action--primary").ClickAsync(new());

        editor.WaitForAssertion(() =>
        {
            Assert.Equal(theirs, binding.StoredRevision);
            Assert.Empty(editor.FindAll(".bpmn-change"));
            Assert.Equal("Theirs", editor.Instance.EditedDocument!.FindFlowNode("s")!.Name);
        });
    }

    [Fact]
    public async Task AsksBeforeDiscardingUnsavedEdits()
    {
        var (editor, binding) = await RenderBoundAsync();
        var held = binding.StoredRevision;
        await editor.InvokeAsync(() => editor.Instance.ApplyAsync(new UpdatePropertiesCommand("s", "name", "Mine")));
        await WriteAsTheOtherPartyAsync();
        editor.WaitForAssertion(() => editor.Find(".bpmn-change__action--primary"));

        await editor.Find(".bpmn-change__action--primary").ClickAsync(new());

        Assert.Equal(held, binding.StoredRevision);
        Assert.Equal(2, editor.FindAll(".bpmn-change__action").Count);
        Assert.Equal("Mine", editor.Instance.EditedDocument!.FindFlowNode("s")!.Name);
    }

    [Fact]
    public async Task ANewerChangeShowsItsOwnMessageBeforeAnyConfirmation()
    {
        var (editor, binding) = await RenderBoundAsync();
        await editor.InvokeAsync(() => editor.Instance.ApplyAsync(new UpdatePropertiesCommand("s", "name", "Mine")));
        await WriteAsTheOtherPartyAsync();
        editor.WaitForAssertion(() => editor.Find(".bpmn-change__action--primary"));
        await editor.Find(".bpmn-change__action--primary").ClickAsync(new());
        Assert.Equal(2, editor.FindAll(".bpmn-change__action").Count);

        var newer = await WriteAsTheOtherPartyAsync();

        editor.WaitForAssertion(() =>
        {
            Assert.Equal(newer, binding.ExternalChange?.Revision);
            Assert.Equal(3, editor.FindAll(".bpmn-change__action").Count);
        });
        Assert.Equal("Mine", editor.Instance.EditedDocument!.FindFlowNode("s")!.Name);
    }

    [Fact]
    public async Task SavingACopyKeepsBothVersions()
    {
        var (editor, binding) = await RenderBoundAsync();
        await editor.InvokeAsync(() => editor.Instance.ApplyAsync(new UpdatePropertiesCommand("s", "name", "Mine")));
        await WriteAsTheOtherPartyAsync();
        editor.WaitForAssertion(() => editor.Find(".bpmn-change__action--primary"));

        var saveCopy = editor.FindAll(".bpmn-change__action").Single(button => button.TextContent.Contains("copy", StringComparison.OrdinalIgnoreCase));
        await saveCopy.ClickAsync(new());

        var copy = new BpmnDocumentKey(null, BpmnDocumentPath.Parse("claims/intake (copy).bpmn"));
        editor.WaitForAssertion(() =>
        {
            Assert.Equal(copy, binding.Key);

            // The editor now follows the copy, so the notice says so rather than silently disappearing.
            Assert.Contains("intake (copy).bpmn", editor.Find(".bpmn-change__message").TextContent, StringComparison.Ordinal);
            Assert.Single(editor.FindAll(".bpmn-change__action"));
        });
        var store = Services.GetRequiredService<IBpmnDocumentStore>();
        Assert.Equal("Mine", (await store.LoadDocumentAsync(copy, Token))!.Document.FindFlowNode("s")!.Name);
        Assert.Equal("Theirs", (await store.LoadDocumentAsync(Key, Token))!.Document.FindFlowNode("s")!.Name);
    }

    [Fact]
    public async Task DismissingTheCopyNoticeRemovesIt()
    {
        var (editor, _) = await RenderBoundAsync();
        await WriteAsTheOtherPartyAsync();
        editor.WaitForAssertion(() => editor.Find(".bpmn-change__action--primary"));
        var saveCopy = editor.FindAll(".bpmn-change__action").Single(button => button.TextContent.Contains("copy", StringComparison.OrdinalIgnoreCase));
        await saveCopy.ClickAsync(new());
        editor.WaitForAssertion(() => Assert.Single(editor.FindAll(".bpmn-change__action")));

        await editor.Find(".bpmn-change__action").ClickAsync(new());

        editor.WaitForAssertion(() => Assert.Empty(editor.FindAll(".bpmn-change")));
    }

    [Fact]
    public async Task ReportsAReloadOfUnreadableContentInsteadOfFailingTheClick()
    {
        var (editor, binding) = await RenderBoundAsync();
        var mine = binding.Document;
        var store = Services.GetRequiredService<IBpmnDocumentStore>();
        var current = await store.StatAsync(Key, Token);
        await store.SaveAsync(Key, new BpmnDocumentContent("not bpmn"u8.ToArray(), BpmnFileFormat.Xml), current!.Revision, Token);
        editor.WaitForAssertion(() => editor.Find(".bpmn-change__action--primary"));

        // The click handler must not throw: on Blazor Server that would end the user's circuit.
        await editor.Find(".bpmn-change__action--primary").ClickAsync(new());

        editor.WaitForAssertion(() => Assert.Equal(2, editor.FindAll(".bpmn-change__message").Count));
        Assert.Same(mine, binding.Document);
        Assert.Equal(3, editor.FindAll(".bpmn-change__action").Count);
    }

    [Fact]
    public async Task DismissingHidesTheNoticeButNotTheConflict()
    {
        var (editor, binding) = await RenderBoundAsync();
        await WriteAsTheOtherPartyAsync();
        editor.WaitForAssertion(() => editor.Find(".bpmn-change"));

        await editor.FindAll(".bpmn-change__action")[0].ClickAsync(new());

        editor.WaitForAssertion(() => Assert.Empty(editor.FindAll(".bpmn-change")));
        Assert.Equal(BpmnStoreStatus.Conflict, (await editor.InvokeAsync(() => binding.SaveAsync(Token))).Status);
    }

    [Fact]
    public async Task OffersToSaveAgainAfterADelete()
    {
        var (editor, binding) = await RenderBoundAsync();
        await Services.GetRequiredService<IBpmnDocumentStore>().DeleteAsync(Key, ifMatchRevision: null, Token);
        editor.WaitForAssertion(() => editor.Find(".bpmn-change__action--primary"));

        Assert.Equal(2, editor.FindAll(".bpmn-change__action").Count);
        await editor.Find(".bpmn-change__action--primary").ClickAsync(new());

        editor.WaitForAssertion(() => Assert.Empty(editor.FindAll(".bpmn-change")));
        Assert.NotNull(await Services.GetRequiredService<IBpmnDocumentStore>().StatAsync(Key, Token));
        Assert.Equal(Key, binding.Key);
    }

    private static CancellationToken Token => Xunit.TestContext.Current.CancellationToken;

    private async Task<(IRenderedComponent<BpmnEditor> Editor, BpmnDocumentBinding Binding)> RenderBoundAsync()
    {
        var store = Services.GetRequiredService<IBpmnDocumentStore>();
        await store.SaveDocumentAsync(Key, BpmnXmlReader.Read(Xml).Document, ifMatchRevision: null, Token);

        var binding = Services.GetRequiredService<IBpmnDocumentBindingFactory>().Create();
        await binding.LoadAsync(Key, Token);

        return (Render<BpmnEditor>(parameters => parameters.Add(p => p.DocumentBinding, binding)), binding);
    }

    private async Task<string?> WriteAsTheOtherPartyAsync()
    {
        var store = Services.GetRequiredService<IBpmnDocumentStore>();
        var current = await store.StatAsync(Key, Token);
        var theirs = BpmnXmlReader.Read(Xml.Replace("Start", "Theirs", StringComparison.Ordinal)).Document;

        return (await store.SaveDocumentAsync(Key, theirs, current!.Revision, Token)).Revision;
    }
}
