using System.Text;

using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Storage;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Storage;

/// <summary>Parsed documents go through any store with the same reader and writer as files.</summary>
public sealed class BpmnDocumentStoreExtensionsTests
{
    public static TheoryData<string> Samples =>
    [
        "simple-process.bpmn",
        "collaboration.bpmn",
        "nested-subprocesses.bpmn",
        "zeebe-extensions.bpmn",
        "vendor-extensions.bpmn",
    ];

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task RoundTripsEverySampleAsXml(string sample)
    {
        var store = new InMemoryBpmnDocumentStore();
        var original = BpmnXmlReader.Read(ReadSample(sample)).Document;
        var key = Key($"samples/{sample}");

        var saved = await store.SaveDocumentAsync(key, original, ifMatchRevision: null, Token);
        var loaded = await store.LoadDocumentAsync(key, Token);

        Assert.True(saved.Succeeded);
        Assert.NotNull(loaded);
        Assert.Equal(saved.Revision, loaded.Revision);
        Assert.Equal(original.FlowNodes.Count, loaded.Document.FlowNodes.Count);
        Assert.Equal(original.SequenceFlows.Count, loaded.Document.SequenceFlows.Count);
        Assert.Equal(BpmnFileFormat.Xml, (await store.OpenAsync(key, Token))!.Content.Format);
    }

    [Fact]
    public async Task WritesJsonWhenTheNameSaysJson()
    {
        var store = new InMemoryBpmnDocumentStore();
        var original = BpmnXmlReader.Read(ReadSample("simple-process.bpmn")).Document;
        var key = Key("simple.json");

        await store.SaveDocumentAsync(key, original, ifMatchRevision: null, Token);
        var stored = await store.OpenAsync(key, Token);
        var loaded = await store.LoadDocumentAsync(key, Token);

        Assert.Equal(BpmnFileFormat.Json, stored!.Content.Format);
        Assert.StartsWith("{", Encoding.UTF8.GetString(stored.Content.Bytes.Span).TrimStart(), StringComparison.Ordinal);
        Assert.Equal(original.FlowNodes.Count, loaded!.Document.FlowNodes.Count);
    }

    [Fact]
    public async Task ReadsContentThatStartsWithAByteOrderMark()
    {
        var store = new InMemoryBpmnDocumentStore();
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(ReadSample("simple-process.bpmn"))).ToArray();
        await store.SaveAsync(Key("bom.bpmn"), new BpmnDocumentContent(bytes, BpmnFileFormat.Xml), ifMatchRevision: null, Token);

        var loaded = await store.LoadDocumentAsync(Key("bom.bpmn"), Token);

        Assert.NotNull(loaded);
        Assert.NotEmpty(loaded.Document.FlowNodes);
    }

    [Fact]
    public async Task RefusesContentThatIsNotUtf8()
    {
        var store = new InMemoryBpmnDocumentStore();
        await store.SaveAsync(Key("bad.bpmn"), new BpmnDocumentContent(new byte[] { 0x3C, 0xFF, 0xFE }, BpmnFileFormat.Xml), ifMatchRevision: null, Token);

        await Assert.ThrowsAsync<DecoderFallbackException>(async () => await store.LoadDocumentAsync(Key("bad.bpmn"), Token));
    }

    [Fact]
    public async Task ReturnsNullForAMissingDocument()
    {
        var store = new InMemoryBpmnDocumentStore();

        Assert.Null(await store.LoadDocumentAsync(Key("missing.bpmn"), Token));
    }

    [Fact]
    public async Task PassesConflictsThrough()
    {
        var store = new InMemoryBpmnDocumentStore();
        var document = BpmnXmlReader.Read(ReadSample("simple-process.bpmn")).Document;
        await store.SaveDocumentAsync(Key("a.bpmn"), document, ifMatchRevision: null, Token);

        var again = await store.SaveDocumentAsync(Key("a.bpmn"), document, ifMatchRevision: null, Token);

        Assert.Equal(BpmnStoreStatus.Conflict, again.Status);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static BpmnDocumentKey Key(string path) => new(null, BpmnDocumentPath.Parse(path));

    private static string ReadSample(string fileName) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "playground", "Bpmn.Demo", "wwwroot", "samples", fileName));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "playground"))
                && Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output folder.");
    }
}
