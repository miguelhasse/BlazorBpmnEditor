using System.Text;

using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Storage;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Storage;

/// <summary>
/// A binding loads, saves with a revision check, tracks unsaved edits and notices other writers
/// (design doc, "External change notification").
/// </summary>
public sealed class BpmnDocumentBindingTests
{
    private static readonly BpmnDocumentKey Intake = new(null, BpmnDocumentPath.Parse("claims/intake.bpmn"));

    // --- loading and dirty tracking --------------------------------------------------------

    [Fact]
    public async Task LoadsADocumentAndStartsClean()
    {
        var world = await World.CreateAsync();
        var changes = 0;
        world.Binding.Changed += (_, _) => changes++;

        var result = await world.Binding.LoadAsync(Intake, Token);

        Assert.True(result.Succeeded);
        Assert.Equal(Intake, world.Binding.Key);
        Assert.NotNull(world.Binding.Document);
        Assert.Same(world.Binding.Document, world.Binding.SavedDocument);
        Assert.Equal(result.Revision, world.Binding.StoredRevision);
        Assert.False(world.Binding.IsDirty);
        Assert.Null(world.Binding.ExternalChange);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task ReportsAMissingDocumentWithoutChangingState()
    {
        var world = await World.CreateAsync();

        var result = await world.Binding.LoadAsync(new BpmnDocumentKey(null, BpmnDocumentPath.Parse("missing.bpmn")), Token);

        Assert.Equal(BpmnStoreStatus.NotFound, result.Status);
        Assert.Null(world.Binding.Key);
        Assert.Null(world.Binding.Document);
    }

    [Fact]
    public async Task SavesAFileInTheFormatItWasLoadedInWhateverItsNameSays()
    {
        var world = await World.CreateAsync();
        var key = new BpmnDocumentKey(null, BpmnDocumentPath.Parse("misnamed.json"));
        var xml = Encoding.UTF8.GetBytes("<definitions xmlns=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" id=\"d\" targetNamespace=\"t\"><process id=\"p\" /></definitions>");
        await world.Inner.SaveAsync(key, new BpmnDocumentContent(xml, BpmnFileFormat.Xml), null, Token);
        await world.Binding.LoadAsync(key, Token);

        world.Binding.Track(Edited(world.Binding.SavedDocument!));
        var saved = await world.Binding.SaveAsync(Token);

        Assert.True(saved.Succeeded);
        var written = Encoding.UTF8.GetString((await world.Inner.OpenAsync(key, Token))!.Content.Bytes.Span).TrimStart('﻿', ' ');
        Assert.StartsWith("<", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TurnsDirtyOnAnEditAndCleanWhenUndoReturnsTheSavedSnapshot()
    {
        var world = await World.LoadedAsync();
        var saved = world.Binding.SavedDocument!;
        var changes = 0;
        world.Binding.Changed += (_, _) => changes++;

        world.Binding.Track(Edited(saved));
        var dirtyAfterEdit = world.Binding.IsDirty;
        world.Binding.Track(Edited(saved));
        world.Binding.Track(saved);

        Assert.True(dirtyAfterEdit);
        Assert.False(world.Binding.IsDirty);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task ReportsTheFirstEditAfterLoadingAnotherDocument()
    {
        var world = await World.LoadedAsync();
        world.Binding.Track(Edited(world.Binding.SavedDocument!));
        var other = new BpmnDocumentKey(null, BpmnDocumentPath.Parse("claims/other.bpmn"));
        await world.Inner.SaveDocumentAsync(other, world.Binding.SavedDocument!, ifMatchRevision: null, Token);
        await world.Binding.LoadAsync(other, Token);
        var changes = 0;
        world.Binding.Changed += (_, _) => changes++;

        world.Binding.Track(Edited(world.Binding.SavedDocument!));

        Assert.True(world.Binding.IsDirty);
        Assert.Equal(1, changes);
    }

    // --- review regressions: overlapping and failing writes -----------------------------------

    [Fact]
    public async Task KeepsEditsMadeWhileASaveIsInFlight()
    {
        var world = await World.LoadedAsync(gated: true);
        var first = Edited(world.Binding.SavedDocument!);
        var later = Edited(first);
        world.Binding.Track(first);

        var saving = world.Binding.SaveAsync(Token);
        world.Binding.Track(later);
        world.Gate.Open();
        var result = await saving;

        Assert.True(result.Succeeded);
        Assert.Same(later, world.Binding.Document);
        Assert.Same(first, world.Binding.SavedDocument);
        Assert.True(world.Binding.IsDirty);
    }

    [Fact]
    public async Task ASecondSaveWaitsForTheFirstInsteadOfConflicting()
    {
        var world = await World.LoadedAsync(gated: true);
        world.Binding.Track(Edited(world.Binding.SavedDocument!));

        var first = world.Binding.SaveAsync(Token);
        world.Binding.Track(Edited(world.Binding.Document!));
        var second = world.Binding.SaveAsync(Token);
        world.Gate.Open();

        Assert.True((await first).Succeeded);
        Assert.True((await second).Succeeded);
        Assert.Null(world.Binding.ExternalChange);
        Assert.False(world.Binding.IsDirty);
        Assert.Equal((await world.Inner.StatAsync(Intake, Token))!.Revision, world.Binding.StoredRevision);
    }

    [Fact]
    public async Task StillReportsWhatWasHeardDuringASaveThatFailed()
    {
        var world = await World.LoadedAsync(gated: true);
        world.Binding.Track(Edited(world.Binding.SavedDocument!));

        var saving = world.Binding.SaveAsync(Token);
        var theirs = await world.WriteAsTheOtherPartyAsync();
        world.Gate.Fail(new IOException("The store went away."));

        await Assert.ThrowsAsync<IOException>(() => saving);
        Assert.Equal(theirs, world.Binding.ExternalChange?.Revision);
    }

    [Fact]
    public async Task SaveAsIgnoresChangesToTheDocumentItLeaves()
    {
        var world = await World.LoadedAsync(BpmnExternalChangePolicy.AutoReloadWhenClean, gated: true);
        var copy = new BpmnDocumentKey(null, BpmnDocumentPath.Parse("claims/copy.bpmn"));
        var mine = world.Binding.Document;
        var loads = 0;
        world.Binding.Changed += (_, _) => loads += ReferenceEquals(world.Binding.Document, mine) ? 0 : 1;

        var saving = world.Binding.SaveAsAsync(copy, Token);
        await world.WriteAsTheOtherPartyAsync();
        world.Gate.Open();
        await saving;

        // The editor keeps the document it saved: nothing was reloaded over it, so its undo history stands.
        Assert.Same(mine, world.Binding.Document);
        Assert.Equal(0, loads);
        Assert.Equal(copy, world.Binding.Key);
        Assert.Equal((await world.Inner.StatAsync(copy, Token))!.Revision, world.Binding.StoredRevision);
        Assert.Null(world.Binding.ExternalChange);
    }


    // --- review regressions: races and failures around saves, reloads and subscriptions ------------

    [Fact]
    public async Task NeverWritesTheDiscardedDocumentOverWhatAReloadJustBrought()
    {
        var world = await World.LoadedAsync();
        world.Binding.Track(Edited(world.Binding.SavedDocument!));
        var held = world.Counting.HoldNextStat();

        var saving = world.Binding.SaveAsync(Token);
        var theirs = await world.WriteAsTheOtherPartyAsync();
        await world.Binding.ReloadAsync(Token);
        held.SetResult();
        var result = await saving;

        // The save was for the document the user had when they pressed it, based on the revision they had. The reload replaced both; writing the old
        // document with the new revision would have put discarded work over the other party's.
        Assert.Equal(BpmnStoreStatus.Conflict, result.Status);
        Assert.Equal(theirs, (await world.Inner.StatAsync(Intake, Token))!.Revision);
        Assert.Equal(theirs, world.Binding.StoredRevision);
        Assert.False(world.Binding.IsDirty);
    }

    [Fact]
    public async Task StillReportsWhatWasHeardDuringASaveAsThatFailed()
    {
        var world = await World.LoadedAsync(gated: true);
        var copy = new BpmnDocumentKey(null, BpmnDocumentPath.Parse("claims/copy.bpmn"));

        var saving = world.Binding.SaveAsAsync(copy, Token);
        var theirs = await world.WriteAsTheOtherPartyAsync();
        world.Gate.Fail(new IOException("The store went away."));

        // The change was to the document still bound, which a failed save-as leaves bound.
        await Assert.ThrowsAsync<IOException>(() => saving);
        Assert.Equal(Intake, world.Binding.Key);
        Assert.Equal(theirs, world.Binding.ExternalChange?.Revision);
    }

    [Fact]
    public async Task ALoadStandsEvenWhenTheLiveUpdateSubscriptionCannotBeMade()
    {
        var world = await World.CreateAsync();
        world.Subscriber.FailNextSubscribe = new InvalidOperationException("The feed could not be opened.");
        var changes = 0;
        world.Binding.Changed += (_, _) => changes++;

        var result = await world.Binding.LoadAsync(Intake, Token);

        // Live updates are an improvement on the save-time revision check, which stays the authority: not having them must not undo a load.
        Assert.True(result.Succeeded);
        Assert.Equal(Intake, world.Binding.Key);
        Assert.NotNull(world.Binding.Document);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task ASaveAsThatWasWrittenSucceedsEvenWhenTheSubscriptionToTheNewKeyFails()
    {
        var world = await World.LoadedAsync();
        var copy = new BpmnDocumentKey(null, BpmnDocumentPath.Parse("claims/copy.bpmn"));
        world.Subscriber.FailNextSubscribe = new InvalidOperationException("The feed could not be opened.");

        var result = await world.Binding.SaveAsAsync(copy, Token);

        Assert.True(result.Succeeded);
        Assert.Equal(copy, world.Binding.Key);
        Assert.Equal(result.Revision, world.Binding.StoredRevision);
    }

    [Fact]
    public async Task ReportsTheChangeWhenAnAutomaticReloadCannotReadIt()
    {
        var world = await World.LoadedAsync(BpmnExternalChangePolicy.AutoReloadWhenClean);
        var current = await world.Store.StatAsync(Intake, Token);

        var unreadable = await world.Store.SaveAsync(Intake, new BpmnDocumentContent("not bpmn"u8.ToArray(), BpmnFileFormat.Xml), current!.Revision, Token);

        Assert.Equal(unreadable.Revision, world.Binding.ExternalChange?.Revision);
    }

    // --- saving --------------------------------------------------------------------------------

    [Fact]
    public async Task SavesAtTheHeldRevisionAndIsCleanAfterwards()
    {
        var world = await World.LoadedAsync();
        var before = world.Binding.StoredRevision;
        world.Binding.Track(Edited(world.Binding.SavedDocument!));

        var result = await world.Binding.SaveAsync(Token);

        Assert.True(result.Succeeded);
        Assert.NotEqual(before, world.Binding.StoredRevision);
        Assert.Equal(result.Revision, (await world.Store.StatAsync(Intake, Token))!.Revision);
        Assert.False(world.Binding.IsDirty);
    }

    [Fact]
    public async Task NeverReportsItsOwnSaveAsSomebodyElses()
    {
        var world = await World.LoadedAsync();
        world.Binding.Track(Edited(world.Binding.SavedDocument!));

        await world.Binding.SaveAsync(Token);

        Assert.Null(world.Binding.ExternalChange);
    }

    [Fact]
    public async Task RefusesToSaveOverAnotherWritersChange()
    {
        var world = await World.LoadedAsync();
        var theirs = await world.WriteAsTheOtherPartyAsync();
        world.Binding.Track(Edited(world.Binding.SavedDocument!));

        var result = await world.Binding.SaveAsync(Token);

        Assert.Equal(BpmnStoreStatus.Conflict, result.Status);
        Assert.Equal(theirs, (await world.Store.StatAsync(Intake, Token))!.Revision);
        Assert.True(world.Binding.IsDirty);
    }

    [Fact]
    public async Task CatchesAMissedChangeBeforeSaving()
    {
        // No notifications at all: the change is only discovered by the check every save makes first.
        var world = await World.CreateAsync(withNotifications: false);
        await world.Binding.LoadAsync(Intake, Token);
        var theirs = await world.WriteAsTheOtherPartyAsync();
        world.Binding.Track(Edited(world.Binding.SavedDocument!));

        var result = await world.Binding.SaveAsync(Token);

        Assert.Equal(BpmnStoreStatus.Conflict, result.Status);
        Assert.Equal(theirs, world.Binding.ExternalChange?.Revision);
    }

    [Fact]
    public async Task SaveAsMovesTheBindingToANewDocument()
    {
        var world = await World.LoadedAsync();
        var copy = new BpmnDocumentKey(null, BpmnDocumentPath.Parse("claims/copy.bpmn"));
        world.Binding.Track(Edited(world.Binding.SavedDocument!));

        var result = await world.Binding.SaveAsAsync(copy, Token);

        Assert.True(result.Succeeded);
        Assert.Equal(copy, world.Binding.Key);
        Assert.False(world.Binding.IsDirty);
        Assert.NotNull(await world.Store.StatAsync(copy, Token));
    }

    [Fact]
    public async Task SaveAsNeverOverwritesAnExistingDocument()
    {
        var world = await World.LoadedAsync();
        var existing = new BpmnDocumentKey(null, BpmnDocumentPath.Parse("claims/other.bpmn"));
        await world.Store.SaveDocumentAsync(existing, world.Binding.SavedDocument!, ifMatchRevision: null, Token);

        var result = await world.Binding.SaveAsAsync(existing, Token);

        Assert.Equal(BpmnStoreStatus.Conflict, result.Status);
        Assert.Equal(Intake, world.Binding.Key);
    }

    // --- external changes ----------------------------------------------------------------------

    [Fact]
    public async Task ReportsAnUpdateByAnotherWriter()
    {
        var world = await World.LoadedAsync();
        var changes = 0;
        world.Binding.Changed += (_, _) => changes++;

        var theirs = await world.WriteAsTheOtherPartyAsync();

        Assert.Equal(BpmnDocumentChangeKind.Updated, world.Binding.ExternalChange?.Kind);
        Assert.Equal(theirs, world.Binding.ExternalChange?.Revision);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task ReportsADeleteByAnotherWriter()
    {
        var world = await World.LoadedAsync();

        await world.Store.DeleteAsync(Intake, ifMatchRevision: null, Token);

        Assert.Equal(BpmnDocumentChangeKind.Deleted, world.Binding.ExternalChange?.Kind);
        Assert.Equal(BpmnStoreStatus.NotFound, (await world.Binding.SaveAsync(Token)).Status);
    }

    [Fact]
    public async Task IgnoresARepeatedOrOwnRevisionNotification()
    {
        var world = await World.LoadedAsync();
        var theirs = await world.WriteAsTheOtherPartyAsync();
        var changes = 0;
        world.Binding.Changed += (_, _) => changes++;

        await world.Bus.PublishAsync(new BpmnDocumentChange(Intake, BpmnDocumentChangeKind.Updated, theirs, null, DateTimeOffset.UnixEpoch), Token);
        await world.Bus.PublishAsync(new BpmnDocumentChange(Intake, BpmnDocumentChangeKind.Updated, world.Binding.StoredRevision, null, DateTimeOffset.UnixEpoch), Token);

        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task DismissingHidesTheChangeButKeepsTheConflict()
    {
        var world = await World.LoadedAsync();
        await world.WriteAsTheOtherPartyAsync();

        world.Binding.DismissExternalChange();
        var dismissed = world.Binding.ExternalChange;
        var result = await world.Binding.SaveAsync(Token);

        Assert.Null(dismissed);
        Assert.Equal(BpmnStoreStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task ReloadingTakesTheirVersionAndDropsLocalEdits()
    {
        var world = await World.LoadedAsync();
        var theirs = await world.WriteAsTheOtherPartyAsync();
        world.Binding.Track(Edited(world.Binding.SavedDocument!));

        var result = await world.Binding.ReloadAsync(Token);

        Assert.True(result.Succeeded);
        Assert.Equal(theirs, world.Binding.StoredRevision);
        Assert.Null(world.Binding.ExternalChange);
        Assert.False(world.Binding.IsDirty);
    }

    [Fact]
    public async Task ReloadsSilentlyWhenCleanUnderTheAutoReloadPolicy()
    {
        var world = await World.LoadedAsync(BpmnExternalChangePolicy.AutoReloadWhenClean);

        var theirs = await world.WriteAsTheOtherPartyAsync();

        Assert.Equal(theirs, world.Binding.StoredRevision);
        Assert.Null(world.Binding.ExternalChange);
    }

    [Fact]
    public async Task OffersInsteadOfReloadingWhenDirtyOrBusy()
    {
        var dirty = await World.LoadedAsync(BpmnExternalChangePolicy.AutoReloadWhenClean);
        var busy = await World.LoadedAsync(BpmnExternalChangePolicy.AutoReloadWhenClean);
        var held = dirty.Binding.StoredRevision;
        dirty.Binding.Track(Edited(dirty.Binding.SavedDocument!));
        busy.Binding.IsBusy = () => true;

        await dirty.WriteAsTheOtherPartyAsync();
        await busy.WriteAsTheOtherPartyAsync();

        Assert.Equal(held, dirty.Binding.StoredRevision);
        Assert.NotNull(dirty.Binding.ExternalChange);
        Assert.NotNull(busy.Binding.ExternalChange);
    }

    [Fact]
    public async Task SaysNothingUntilTheSaveUnderTheIgnorePolicy()
    {
        var world = await World.LoadedAsync(BpmnExternalChangePolicy.Ignore);
        await world.WriteAsTheOtherPartyAsync();
        world.Binding.Track(Edited(world.Binding.SavedDocument!));

        var before = world.Binding.ExternalChange;
        var result = await world.Binding.SaveAsync(Token);

        Assert.Null(before);
        Assert.Equal(BpmnStoreStatus.Conflict, result.Status);
        Assert.Null(world.Binding.ExternalChange);
    }

    // --- the feed ----------------------------------------------------------------------------

    [Fact]
    public async Task ChecksOnceAfterEachGapInTheFeed()
    {
        var world = await World.LoadedAsync();
        var statsBefore = world.Counting.Stats;

        await world.Subscriber.RaiseResubscribedAsync();

        Assert.Equal(statsBefore + 1, world.Counting.Stats);
    }

    [Fact]
    public async Task NoticesAChangeMadeDuringAGap()
    {
        var world = await World.LoadedAsync();
        var theirs = (await world.Inner.SaveDocumentAsync(Intake, Edited(world.Binding.SavedDocument!), world.Binding.StoredRevision, Token)).Revision;

        await world.Subscriber.RaiseResubscribedAsync();

        Assert.Equal(theirs, world.Binding.ExternalChange?.Revision);
    }

    [Fact]
    public async Task HandsNotificationsToTheDispatcher()
    {
        var world = await World.LoadedAsync();
        var dispatched = 0;
        world.Binding.Dispatcher = work =>
        {
            dispatched++;
            return work();
        };

        await world.WriteAsTheOtherPartyAsync();

        Assert.Equal(1, dispatched);
        Assert.NotNull(world.Binding.ExternalChange);
    }

    [Fact]
    public async Task IgnoresNotificationsOnceDisposed()
    {
        var world = await World.LoadedAsync();

        await world.Binding.DisposeAsync();
        await world.WriteAsTheOtherPartyAsync();

        Assert.Null(world.Binding.ExternalChange);
    }

    // --- review regressions: lifetime and threading ---------------------------------------------

    [Fact]
    public async Task DisposingWaitsForASaveInFlight()
    {
        var world = await World.LoadedAsync(gated: true);
        world.Binding.Track(Edited(world.Binding.SavedDocument!));
        var saving = world.Binding.SaveAsync(Token);

        var disposing = world.Binding.DisposeAsync().AsTask();
        Assert.False(disposing.IsCompleted);
        world.Gate.Open();

        // The save releases its lock as it ends; disposing underneath it would make that throw.
        Assert.True((await saving).Succeeded);
        await disposing;
    }

    [Fact]
    public async Task HoldsNotificationsUntilAnEditorAttaches()
    {
        var world = await World.LoadedAsync(deferred: true);

        await world.WriteAsTheOtherPartyAsync();

        // Nothing ran on the writer's thread.
        Assert.Null(world.Binding.ExternalChange);

        var dispatched = 0;
        world.Binding.Dispatcher = work =>
        {
            dispatched++;
            return work();
        };

        await WaitForAsync(() => world.Binding.ExternalChange is not null);
        Assert.Equal(1, dispatched);
    }

    [Fact]
    public async Task HoldsNothingForAnEditorThatIsAlreadyAttached()
    {
        var world = await World.LoadedAsync(deferred: true);
        world.Binding.Dispatcher = work => work();

        await world.WriteAsTheOtherPartyAsync();

        Assert.NotNull(world.Binding.ExternalChange);
    }

    [Fact]
    public async Task AttachingWithNothingMissedChecksNothing()
    {
        var world = await World.LoadedAsync(deferred: true);
        var stats = world.Counting.Stats;

        world.Binding.Dispatcher = work => work();

        Assert.Equal(stats, world.Counting.Stats);
    }

    // --- helpers -------------------------------------------------------------------------------

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++) await Task.Delay(10, Token);
        Assert.True(condition(), "The condition did not hold in time.");
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // A different snapshot of the same diagram, as an edit would produce.
    private static BpmnDocument Edited(BpmnDocument document) => document with { };

    private sealed class World
    {
        private World(InMemoryBpmnDocumentStore inner, CountingStore counting, InProcessBpmnDocumentChangeBus bus, ManualSubscriber subscriber, BpmnDocumentBinding binding)
        {
            Inner = inner;
            Counting = counting;
            Bus = bus;
            Subscriber = subscriber;
            Binding = binding;
            Store = new NotifyingBpmnDocumentStore(inner, bus);
        }

        public InMemoryBpmnDocumentStore Inner { get; }

        public CountingStore Counting { get; }

        public InProcessBpmnDocumentChangeBus Bus { get; }

        public ManualSubscriber Subscriber { get; }

        public BpmnDocumentBinding Binding { get; }

        // The other party writes through its own notifying store over the same storage and bus.
        public NotifyingBpmnDocumentStore Store { get; }

        public SaveGate Gate => Counting.Gate ?? throw new InvalidOperationException("This world was not created gated.");

        public static async Task<World> CreateAsync(
            bool withNotifications = true,
            BpmnExternalChangePolicy policy = BpmnExternalChangePolicy.Notify,
            bool gated = false,
            bool deferred = false)
        {
            var inner = new InMemoryBpmnDocumentStore();
            var bus = new InProcessBpmnDocumentChangeBus();
            var counting = new CountingStore(withNotifications ? new NotifyingBpmnDocumentStore(inner, bus) : inner, gated ? new SaveGate() : null);
            var subscriber = new ManualSubscriber(bus);
            var binding = new BpmnDocumentBinding(
                counting,
                withNotifications ? subscriber : null,
                new StorageOptions { ExternalChangePolicy = policy })
            {
                DeferUntilAttached = deferred,
            };

            await inner.SaveDocumentAsync(Intake, Sample(), ifMatchRevision: null, Token);
            return new World(inner, counting, bus, subscriber, binding);
        }

        public static async Task<World> LoadedAsync(BpmnExternalChangePolicy policy = BpmnExternalChangePolicy.Notify, bool gated = false, bool deferred = false)
        {
            var world = await CreateAsync(policy: policy, gated: gated, deferred: deferred);
            await world.Binding.LoadAsync(Intake, Token);
            return world;
        }

        public async Task<string?> WriteAsTheOtherPartyAsync()
        {
            var current = await Store.StatAsync(Intake, Token);
            var result = await Store.SaveDocumentAsync(Intake, Edited(Sample()), current!.Revision, Token);
            return result.Revision;
        }

        private static BpmnDocument Sample() => BpmnXmlReader.Read(
            """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="d" targetNamespace="t">
              <process id="p"><startEvent id="s" /></process>
            </definitions>
            """).Document;
    }

    // Counts metadata reads, which is how catch-up checks are observed.
    // Counts metadata reads, and when gated holds every save until the test opens or fails the gate, so a
    // test can act while a write is in flight.
    private sealed class CountingStore(IBpmnDocumentStore inner, SaveGate? gate = null) : IBpmnDocumentStore
    {
        public int Stats { get; private set; }

        public SaveGate? Gate { get; } = gate;

        public ValueTask<BpmnStoredDocument?> OpenAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default) =>
            inner.OpenAsync(key, cancellationToken);

        private TaskCompletionSource? _heldStat;

        // The next metadata read waits for the test, so the binding can be made to act while a probe is in flight.
        public TaskCompletionSource HoldNextStat() => _heldStat = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<BpmnDocumentStat?> StatAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default)
        {
            Stats++;
            if (Interlocked.Exchange(ref _heldStat, null) is { } held) await held.Task;

            return await inner.StatAsync(key, cancellationToken);
        }

        public async ValueTask<BpmnStoreResult> SaveAsync(BpmnDocumentKey key, BpmnDocumentContent content, string? ifMatchRevision, CancellationToken cancellationToken = default)
        {
            if (Gate is not null) await Gate.PassAsync();
            return await inner.SaveAsync(key, content, ifMatchRevision, cancellationToken);
        }

        public ValueTask<BpmnDocumentPage> ListAsync(BpmnDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.ListAsync(query, cancellationToken);

        public ValueTask<BpmnStoreResult> DeleteAsync(BpmnDocumentKey key, string? ifMatchRevision, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(key, ifMatchRevision, cancellationToken);
    }

    private sealed class SaveGate
    {
        private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task PassAsync() => _opened.Task;

        public void Open() => _opened.TrySetResult();

        public void Fail(Exception exception) => _opened.TrySetException(exception);
    }

    // A subscriber whose gaps a test can cause.
    private sealed class ManualSubscriber(IBpmnDocumentChangeBus bus) : IBpmnDocumentChangeSubscriber
    {
        public event Func<BpmnChangeFeedState, ValueTask>? StateChanged;

        public event Func<ValueTask>? Resubscribed;

        public BpmnChangeFeedState State { get; private set; } = BpmnChangeFeedState.Live;

        public Exception? FailNextSubscribe { get; set; }

        public ValueTask<IAsyncDisposable> SubscribeAsync(BpmnDocumentKey key, Func<BpmnDocumentChange, ValueTask> handler, CancellationToken cancellationToken = default)
        {
            if (FailNextSubscribe is { } failure)
            {
                FailNextSubscribe = null;
                throw failure;
            }

            return bus.SubscribeAsync(key, handler, cancellationToken);
        }

        public async Task RaiseResubscribedAsync()
        {
            State = BpmnChangeFeedState.Live;
            if (StateChanged is { } stateChanged) await stateChanged(State);
            if (Resubscribed is { } resubscribed) await resubscribed();
        }
    }
}
