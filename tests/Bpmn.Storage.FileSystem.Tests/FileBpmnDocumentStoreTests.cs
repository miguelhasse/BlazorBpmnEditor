using System.Text;

using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Storage.Testing;

using IoPath = System.IO.Path;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.FileSystem.Tests;

/// <summary>The file store meets the store contract, plus what is particular to files on a disk.</summary>
public sealed class FileBpmnDocumentStoreTests : BpmnDocumentStoreContractTests, IDisposable
{
    private readonly string _root = IoPath.Combine(IoPath.GetTempPath(), "bpmn-file-store-" + Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    protected override IBpmnDocumentStore CreateStore() => new FileBpmnDocumentStore(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            DropLinks(_root);
            Directory.Delete(_root, recursive: true);
        }
    }

    // Removes a link without touching what it points at, which a recursive delete would not do on every platform.
    private static void DropLinks(string directory)
    {
        foreach (var child in new DirectoryInfo(directory).EnumerateDirectories())
        {
            if (child.Attributes.HasFlag(FileAttributes.ReparsePoint)) child.Delete(recursive: false);
            else DropLinks(child.FullName);
        }
    }

    private string DiskPath(params string[] parts) => IoPath.Combine([_root, .. parts]);

    // --- what ends up on the disk ----------------------------------------------------------------

    [Fact]
    public async Task KeepsADocumentAsARealFileAtItsLogicalPath()
    {
        var store = CreateStore();

        await store.SaveAsync(Key("claims/2026/intake.bpmn"), Content("<x/>"), ifMatchRevision: null, Token);

        Assert.Equal("<x/>", await File.ReadAllTextAsync(DiskPath("claims", "2026", "intake.bpmn"), Token));
    }

    [Fact]
    public async Task KeepsATenantInItsOwnFolderThatNoPathCanReach()
    {
        var store = CreateStore();
        var acme = new BpmnDocumentKey("acme", BpmnDocumentPath.Parse("a.bpmn"));

        await store.SaveAsync(acme, Content("acme"), ifMatchRevision: null, Token);
        await store.SaveAsync(Key("a.bpmn"), Content("default"), ifMatchRevision: null, Token);

        Assert.True(File.Exists(DiskPath("~tenants", "acme", "a.bpmn")));
        var listed = await store.ListAsync(new BpmnDocumentQuery(null, Recursive: true), Token);
        Assert.Equal(["a.bpmn"], listed.Entries.Select(entry => entry.Path.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData("a\\b")]
    public async Task RefusesATenantThatIsNotOneValidSegment(string tenant)
    {
        var store = CreateStore();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.SaveAsync(new BpmnDocumentKey(tenant, BpmnDocumentPath.Parse("a.bpmn")), Content("x"), ifMatchRevision: null, Token));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.ListAsync(new BpmnDocumentQuery(tenant), Token));
    }

    [Fact]
    public async Task OpensAFileAnotherProgramWroteAndNamesItsFormatFromTheExtension()
    {
        var store = CreateStore();
        Directory.CreateDirectory(DiskPath("ext"));
        await File.WriteAllTextAsync(DiskPath("ext", "made.json"), "{}", Token);
        await File.WriteAllTextAsync(DiskPath("ext", "made.xml"), "<x/>", Token);

        var json = await store.OpenAsync(Key("ext/made.json"), Token);
        var xml = await store.OpenAsync(Key("ext/made.xml"), Token);

        Assert.Equal(BpmnFileFormat.Json, json!.Content.Format);
        Assert.Equal(BpmnFileFormat.Xml, xml!.Content.Format);
    }

    [Fact]
    public async Task ListsOnlyDocumentsAndFoldersThatHoldOne()
    {
        var store = CreateStore();
        await store.SaveAsync(Key("real/a.bpmn"), Content("a"), ifMatchRevision: null, Token);
        Directory.CreateDirectory(DiskPath("empty"));
        Directory.CreateDirectory(DiskPath("only-notes"));
        await File.WriteAllTextAsync(DiskPath("only-notes", "readme.txt"), "x", Token);
        await File.WriteAllTextAsync(DiskPath("real", ".leftover.tmp"), "x", Token);

        var page = await store.ListAsync(new BpmnDocumentQuery(null), Token);
        var inside = await store.ListAsync(new BpmnDocumentQuery(null, Folder: BpmnDocumentPath.Parse("real")), Token);

        Assert.Equal(["real"], page.Entries.Select(entry => entry.Path.ToString()));
        Assert.Equal(["real/a.bpmn"], inside.Entries.Select(entry => entry.Path.ToString()));
    }

    [Fact]
    public async Task LeavesNoWorkingFileBehindAfterASaveOrARefusedSave()
    {
        var store = CreateStore();
        var saved = await store.SaveAsync(Key("a.bpmn"), Content("one"), ifMatchRevision: null, Token);
        await store.SaveAsync(Key("a.bpmn"), Content("two"), ifMatchRevision: null, Token);
        await store.SaveAsync(Key("a.bpmn"), Content("three"), saved.Revision, Token);

        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    // --- revisions and concurrency ---------------------------------------------------------------

    [Fact]
    public async Task MovesTheRevisionOnEverySaveEvenWhenSavesAreImmediate()
    {
        var store = CreateStore();
        var revisions = new HashSet<string>();
        var current = (await store.SaveAsync(Key("a.bpmn"), Content("0"), ifMatchRevision: null, Token)).Revision!;
        revisions.Add(current);

        for (var index = 1; index <= 25; index++)
        {
            // Same length every time, so only the file's time can tell the revisions apart.
            current = (await store.SaveAsync(Key("a.bpmn"), Content((index % 10).ToString(System.Globalization.CultureInfo.InvariantCulture)), current, Token)).Revision!;
            Assert.True(revisions.Add(current), $"revision repeated at save {index}");
        }
    }

    [Fact]
    public async Task LetsExactlyOneCreatorWinEvenAcrossTwoStoresOverOneFolder()
    {
        var first = new FileBpmnDocumentStore(_root);
        var second = new FileBpmnDocumentStore(_root);

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(async index =>
            await (index % 2 == 0 ? first : second).SaveAsync(Key("race.bpmn"), Content("writer " + index), ifMatchRevision: null, Token)));

        Assert.Equal(1, results.Count(result => result.Succeeded));
        Assert.Equal(15, results.Count(result => result.Status == BpmnStoreStatus.Conflict));
    }

    [Fact]
    public async Task LetsExactlyOneUpdaterWinWhenManyHoldTheSameRevision()
    {
        var store = CreateStore();
        var revision = (await store.SaveAsync(Key("race.bpmn"), Content("base"), ifMatchRevision: null, Token)).Revision;
        var stores = new[] { store, new FileBpmnDocumentStore(_root) };

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async index =>
            await stores[index % 2].SaveAsync(Key("race.bpmn"), Content("update " + index), revision, Token)));

        Assert.Equal(1, results.Count(result => result.Succeeded));
    }

    // --- staying inside the root -----------------------------------------------------------------

    [Fact]
    public async Task DoesNotFollowALinkedFolderOutOfTheRoot()
    {
        var store = CreateStore();
        var outside = IoPath.Combine(IoPath.GetTempPath(), "bpmn-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            await File.WriteAllTextAsync(IoPath.Combine(outside, "secret.bpmn"), "secret", Token);
            Directory.CreateDirectory(_root);
            if (!TryLinkFolder(DiskPath("linked"), outside)) Assert.Skip("This account cannot create symbolic links here.");

            Assert.Null(await store.OpenAsync(Key("linked/secret.bpmn"), Token));
            Assert.Null(await store.StatAsync(Key("linked/secret.bpmn"), Token));
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await store.SaveAsync(Key("linked/new.bpmn"), Content("x"), ifMatchRevision: null, Token));
            Assert.False(File.Exists(IoPath.Combine(outside, "new.bpmn")));
            Assert.Empty((await store.ListAsync(new BpmnDocumentQuery(null, Recursive: true), Token)).Entries);
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task DoesNotFollowALinkedFile()
    {
        var store = CreateStore();
        var outside = IoPath.Combine(IoPath.GetTempPath(), "bpmn-outside-" + Guid.NewGuid().ToString("N") + ".bpmn");
        await File.WriteAllTextAsync(outside, "secret", Token);
        try
        {
            Directory.CreateDirectory(_root);
            try
            {
                File.CreateSymbolicLink(DiskPath("linked.bpmn"), outside);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                Assert.Skip("This account cannot create symbolic links here.");
            }

            Assert.Null(await store.OpenAsync(Key("linked.bpmn"), Token));
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await store.DeleteAsync(Key("linked.bpmn"), ifMatchRevision: null, Token));
            Assert.True(File.Exists(outside));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task ReportsADifferentlyCasedNameAsMissingWhereTheFileSystemIgnoresCase()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) Assert.Skip("This file system is case-sensitive.");

        var store = CreateStore();
        await store.SaveAsync(Key("Order.bpmn"), Content("upper"), ifMatchRevision: null, Token);

        Assert.Null(await store.OpenAsync(Key("order.bpmn"), Token));
        Assert.Null(await store.StatAsync(Key("order.bpmn"), Token));
        Assert.Equal(BpmnStoreStatus.NotFound, (await store.DeleteAsync(Key("order.bpmn"), ifMatchRevision: null, Token)).Status);
        Assert.Equal("upper", Encoding.UTF8.GetString((await store.OpenAsync(Key("Order.bpmn"), Token))!.Content.Bytes.Span));
    }

    [Fact]
    public async Task AtMostOneOfTwoCreateOnlySavesWinsWhenTheirNamesDifferOnlyByCaseOnAFileSystemThatIgnoresIt()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) Assert.Skip("This file system is case-sensitive.");

        var store = CreateStore();

        // Both names are the same file here. Locks picked by the exact spelling let the two run side by side, and the second replaced the first.
        for (var trial = 0; trial < 40; trial++)
        {
            var results = await Task.WhenAll(
                Task.Run(async () => await store.SaveAsync(Key($"race{trial}/Order.bpmn"), Content("upper"), ifMatchRevision: null, Token), Token),
                Task.Run(async () => await store.SaveAsync(Key($"race{trial}/order.bpmn"), Content("lower"), ifMatchRevision: null, Token), Token));

            Assert.Equal(1, results.Count(result => result.Succeeded));
        }
    }

    [Fact]
    public async Task AnswersAConflictWhenADocumentStandsWhereAFolderIsNeeded()
    {
        var store = CreateStore();
        await store.SaveAsync(Key("a.bpmn"), Content("a document"), ifMatchRevision: null, Token);

        // The folder "a.bpmn" would have to be made where a file of that name is: a refusal, not an IOException after retries.
        var create = await store.SaveAsync(Key("a.bpmn/b.bpmn"), Content("x"), ifMatchRevision: null, Token);
        var update = await store.SaveAsync(Key("a.bpmn/b.bpmn"), Content("x"), ifMatchRevision: "anything", Token);

        Assert.Equal(BpmnStoreStatus.Conflict, create.Status);
        Assert.Equal(BpmnStoreStatus.NotFound, update.Status);
        Assert.Null(await store.OpenAsync(Key("a.bpmn/b.bpmn"), Token));
        Assert.Equal("a document", Encoding.UTF8.GetString((await store.OpenAsync(Key("a.bpmn"), Token))!.Content.Bytes.Span));
    }

    [Fact]
    public async Task AnswersAConflictWhenAFolderStandsWhereADocumentIsNeeded()
    {
        var store = CreateStore();
        await store.SaveAsync(Key("keep.bpmn"), Content("x"), ifMatchRevision: null, Token);
        Directory.CreateDirectory(DiskPath("x.bpmn"));

        var create = await store.SaveAsync(Key("x.bpmn"), Content("x"), ifMatchRevision: null, Token);

        Assert.Equal(BpmnStoreStatus.Conflict, create.Status);
        Assert.Null(await store.StatAsync(Key("x.bpmn"), Token));
        Assert.Equal(BpmnStoreStatus.NotFound, (await store.DeleteAsync(Key("x.bpmn"), ifMatchRevision: null, Token)).Status);
        Assert.True(Directory.Exists(DiskPath("x.bpmn")));
    }

    [Fact]
    public async Task KeepsTenantsApartEvenWhenTheirNamesDifferOnlyByCase()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) Assert.Skip("This file system is case-sensitive.");

        var store = CreateStore();
        await store.SaveAsync(new BpmnDocumentKey("Acme", BpmnDocumentPath.Parse("a.bpmn")), Content("acme"), ifMatchRevision: null, Token);

        Assert.Null(await store.OpenAsync(new BpmnDocumentKey("acme", BpmnDocumentPath.Parse("a.bpmn")), Token));
    }

    private static bool TryLinkFolder(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // A junction needs no privilege on Windows, and is a reparse point like a symbolic link.
            return OperatingSystem.IsWindows() && TryJunction(link, target);
        }
    }

    private static bool TryJunction(string link, string target)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        process!.WaitForExit();
        return process.ExitCode == 0;
    }
}
