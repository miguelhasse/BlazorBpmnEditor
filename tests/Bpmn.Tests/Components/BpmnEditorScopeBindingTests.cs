using Bunit;

using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Storage;

using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>
/// An editor bound to a stored document shows it, reports every edit to the binding without losing
/// history, and surfaces other writers' changes in its state.
/// </summary>
public sealed class BpmnEditorScopeBindingTests : BunitContext
{
    private const string Xml =
        """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="d" targetNamespace="t">
          <process id="p"><startEvent id="s" name="Start" /></process>
        </definitions>
        """;

    private static readonly BpmnDocumentKey Key = new(null, BpmnDocumentPath.Parse("claims/intake.bpmn"));

    public BpmnEditorScopeBindingTests() => Services
        .AddBpmnComponents()
        .AddBpmnInMemoryDocumentStore()
        .AddBpmnDocumentChanges();

    [Fact]
    public async Task ShowsTheBindingsDocument()
    {
        var binding = await LoadedBindingAsync();

        var scope = Render<BpmnEditorScope>(parameters => parameters.Add(p => p.DocumentBinding, binding));

        Assert.Same(binding.Document, scope.Instance.Session.EditedDocument);
        Assert.Same(binding, scope.Instance.Session.DocumentBinding);
        Assert.True(scope.Instance.Session.State.HasDocumentBinding);
        Assert.False(scope.Instance.Session.State.IsDirty);
    }

    [Fact]
    public async Task AnEditMarksTheDocumentDirtyAndKeepsItsHistory()
    {
        var binding = await LoadedBindingAsync();
        var scope = Render<BpmnEditorScope>(parameters => parameters.Add(p => p.DocumentBinding, binding));
        var session = scope.Instance.Session;

        await scope.InvokeAsync(() => session.ApplyAsync(Rename("Renamed")));
        scope.Render(parameters => parameters.Add(p => p.DocumentBinding, binding));

        Assert.True(binding.IsDirty);
        Assert.True(session.State.IsDirty);
        Assert.True(session.CanUndo);
        Assert.Same(binding.Document, session.EditedDocument);
    }

    [Fact]
    public async Task UndoingBackToTheSavedDocumentIsClean()
    {
        var binding = await LoadedBindingAsync();
        var scope = Render<BpmnEditorScope>(parameters => parameters.Add(p => p.DocumentBinding, binding));
        var session = scope.Instance.Session;

        await scope.InvokeAsync(() => session.ApplyAsync(Rename("Renamed")));
        await scope.InvokeAsync(session.UndoAsync);

        Assert.False(binding.IsDirty);
        scope.WaitForAssertion(() => Assert.False(session.State.IsDirty));
    }

    [Fact]
    public async Task SavingMakesTheEditorClean()
    {
        var binding = await LoadedBindingAsync();
        var scope = Render<BpmnEditorScope>(parameters => parameters.Add(p => p.DocumentBinding, binding));
        var session = scope.Instance.Session;
        await scope.InvokeAsync(() => session.ApplyAsync(Rename("Renamed")));

        var result = await scope.InvokeAsync(() => binding.SaveAsync(Token));

        Assert.True(result.Succeeded);
        scope.WaitForAssertion(() => Assert.False(session.State.IsDirty));
        Assert.Null(session.State.ExternalChange);
    }

    [Fact]
    public async Task AnotherWritersChangeReachesTheEditorState()
    {
        var binding = await LoadedBindingAsync();
        var scope = Render<BpmnEditorScope>(parameters => parameters.Add(p => p.DocumentBinding, binding));

        // From a thread pool thread, as a write by another circuit or an agent would arrive.
        await Task.Run(WriteAsTheOtherPartyAsync, Token);

        scope.WaitForAssertion(() => Assert.Equal(BpmnDocumentChangeKind.Updated, scope.Instance.Session.State.ExternalChange?.Kind));
    }

    [Fact]
    public async Task ReloadingShowsTheirDocumentAndStartsAFreshHistory()
    {
        var binding = await LoadedBindingAsync();
        var scope = Render<BpmnEditorScope>(parameters => parameters.Add(p => p.DocumentBinding, binding));
        var session = scope.Instance.Session;
        await scope.InvokeAsync(() => session.ApplyAsync(Rename("Mine")));
        await WriteAsTheOtherPartyAsync();

        await scope.InvokeAsync(() => binding.ReloadAsync(Token));

        scope.WaitForAssertion(() =>
        {
            Assert.Same(binding.Document, session.EditedDocument);
            Assert.False(session.CanUndo);
            Assert.Null(session.State.ExternalChange);
            Assert.False(session.State.IsDirty);
        });
    }

    [Fact]
    public async Task AcceptingAnImportReplacesTheContentsAndKeepsTheKey()
    {
        var binding = await LoadedBindingAsync();
        var scope = Render<BpmnEditorScope>(parameters => parameters.Add(p => p.DocumentBinding, binding));
        var session = scope.Instance.Session;

        await scope.InvokeAsync(() => session.PreviewImport("other.bpmn", Xml.Replace("Start", "Imported", StringComparison.Ordinal)));
        await scope.InvokeAsync(session.AcceptImportAsync);

        Assert.Equal(Key, binding.Key);
        Assert.True(binding.IsDirty);
        Assert.Same(binding.Document, session.EditedDocument);
    }

    [Fact]
    public async Task RefusesADocumentAndABindingTogether()
    {
        var binding = await LoadedBindingAsync();

        var error = Assert.Throws<InvalidOperationException>(() => Render<BpmnEditorScope>(parameters => parameters
            .Add(p => p.DocumentBinding, binding)
            .Add(p => p.Document, binding.Document)));

        Assert.Contains(nameof(BpmnEditorScope.DocumentBinding), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandsTheBindingToTheEditorThatTakesItOver()
    {
        // Blazor gives a replacement editor its parameters before it disposes the one it replaces, as when a
        // host swaps one kit's editor for another; the newcomer must take the binding, not fail.
        var binding = await LoadedBindingAsync();
        var first = Render<BpmnEditorScope>(parameters => parameters.Add(p => p.DocumentBinding, binding));

        var second = Render<BpmnEditorScope>(parameters => parameters.Add(p => p.DocumentBinding, binding));
        ((IDisposable)first.Instance).Dispose();
        await second.InvokeAsync(() => second.Instance.Session.ApplyAsync(Rename("Renamed")));

        Assert.Null(first.Instance.Session.DocumentBinding);
        Assert.Same(binding, second.Instance.Session.DocumentBinding);
        Assert.Same(second.Instance, binding.Owner);
        Assert.True(binding.IsDirty);
    }

    [Fact]
    public async Task ReleasesTheBindingWhenTheEditorGoesAway()
    {
        var binding = await LoadedBindingAsync();
        Render<BpmnEditorScope>(parameters => parameters.Add(p => p.DocumentBinding, binding));

        await DisposeComponentsAsync();
        var second = Render<BpmnEditorScope>(parameters => parameters.Add(p => p.DocumentBinding, binding));

        Assert.Same(binding, second.Instance.Session.DocumentBinding);
    }

    private static CancellationToken Token => Xunit.TestContext.Current.CancellationToken;

    private async Task<BpmnDocumentBinding> LoadedBindingAsync()
    {
        var store = Services.GetRequiredService<IBpmnDocumentStore>();
        if (await store.StatAsync(Key, Token) is null)
        {
            await store.SaveDocumentAsync(Key, BpmnXmlReader.Read(Xml).Document, ifMatchRevision: null, Token);
        }

        var binding = Services.GetRequiredService<IBpmnDocumentBindingFactory>().Create();
        await binding.LoadAsync(Key, Token);
        return binding;
    }

    private async Task WriteAsTheOtherPartyAsync()
    {
        var store = Services.GetRequiredService<IBpmnDocumentStore>();
        var current = await store.StatAsync(Key, Token);
        var theirs = BpmnXmlReader.Read(Xml.Replace("Start", "Theirs", StringComparison.Ordinal)).Document;

        await store.SaveDocumentAsync(Key, theirs, current!.Revision, Token);
    }

    private static UpdatePropertiesCommand Rename(string name) => new("s", "name", name);
}
