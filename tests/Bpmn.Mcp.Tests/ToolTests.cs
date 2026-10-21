using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Mcp.Tool;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using static Hasseware.AspNetCore.Components.Bpmn.Mcp.Tests.McpHarness;

namespace Hasseware.AspNetCore.Components.Bpmn.Mcp.Tests;

/// <summary>The <c>bpmn-mcp</c> tool: what a pipeline sees from <c>validate</c>, and a real child process over stdio.</summary>
public sealed class ToolTests : IDisposable
{
    private const string BrokenFlows = """
        <?xml version="1.0" encoding="UTF-8"?>
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="urn:test">
          <process id="p" isExecutable="false">
            <startEvent id="start" name="Start" />
            <endEvent id="end" name="Done" />
            <task id="t" name="Work" />
            <sequenceFlow id="f1" sourceRef="end" targetRef="t" />
            <sequenceFlow id="f2" sourceRef="t" targetRef="start" />
          </process>
        </definitions>
        """;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "bpmn-mcp-tool-" + Guid.NewGuid().ToString("N"));

    public ToolTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static async Task<(int Code, string Output, string Error)> RunAsync(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = await BpmnMcpCli.RunAsync(args, output, error, Token);
        return (code, output.ToString(), error.ToString());
    }

    // --- validate --------------------------------------------------------------------------------

    [Fact]
    public async Task ExitsZeroForACleanDiagram()
    {
        var file = Write("clean.bpmn", Diagram());

        var (code, output, _) = await RunAsync("validate", file);

        Assert.Equal(BpmnMcpCli.Success, code);
        Assert.Contains("1 file(s), 0 error(s)", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExitsOneAndNamesEachFindingForABrokenDiagram()
    {
        var file = Write("broken.bpmn", BrokenFlows);

        var (code, output, _) = await RunAsync("validate", file);

        Assert.Equal(BpmnMcpCli.Findings, code);
        Assert.Contains($"{Path.GetFullPath(file)}: error D001", output, StringComparison.Ordinal);
        Assert.Contains("[start]", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailsOnWarningsOnlyWhenAskedTo()
    {
        var file = Write("warn.bpmn", Diagram().Replace("</process>", "<task id=\"orphan\" name=\"Orphan\" /></process>", StringComparison.Ordinal));

        var (Code, Output, _) = await RunAsync("validate", file);
        var strict = await RunAsync("validate", file, "--warnings-as-errors");

        Assert.Contains("warning", Output, StringComparison.Ordinal);
        Assert.Equal(BpmnMcpCli.Success, Code);
        Assert.Equal(BpmnMcpCli.Findings, strict.Code);
    }

    [Fact]
    public async Task TreatsAFileThatIsNotBpmnAsAFindingWithoutQuotingIt()
    {
        var file = Write("junk.bpmn", "<<< SECRET-CONTENT >>>");

        var (code, output, _) = await RunAsync("validate", file);

        Assert.Equal(BpmnMcpCli.Findings, code);
        Assert.Contains("INVALID-DOCUMENT", output, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-CONTENT", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExpandsAFolderAndAPattern()
    {
        Write("a/one.bpmn", Diagram());
        Write("a/b/two.bpmn", Diagram());
        Write("a/ignored.txt", "not a diagram");

        var (_, Output, _) = await RunAsync("validate", Path.Combine(_folder, "a"));
        var pattern = await RunAsync("validate", Path.Combine(_folder, "a", "*.bpmn"));
        var recursivePattern = await RunAsync("validate", Path.Combine(_folder, "**", "*.bpmn"));

        Assert.Contains("2 file(s)", Output, StringComparison.Ordinal);
        Assert.Contains("1 file(s)", pattern.Output, StringComparison.Ordinal);
        Assert.Contains("2 file(s)", recursivePattern.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WritesParseableJson()
    {
        var file = Write("broken.bpmn", BrokenFlows);

        var (code, output, _) = await RunAsync("validate", file, "--json");

        Assert.Equal(BpmnMcpCli.Findings, code);
        using var json = JsonDocument.Parse(output);
        Assert.Equal(2, json.RootElement.GetProperty("errors").GetInt32());
        Assert.Contains(json.RootElement.GetProperty("files")[0].GetProperty("findings").EnumerateArray(), finding => finding.GetProperty("code").GetString() == "D001");
    }

    [Fact]
    public async Task ExitsTwoForATenantNoStoreCouldHold()
    {
        var (code, output, error) = await RunAsync("serve", "--root", _folder, "--tenant", "alice@example.com");

        Assert.Equal(BpmnMcpCli.Usage, code);
        Assert.Equal(string.Empty, output);
        Assert.Contains("--tenant", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("validate")]
    [InlineData("validate", "no-such-file.bpmn")]
    [InlineData("validate", "--nonsense", "x.bpmn")]
    [InlineData("serve")]
    [InlineData("serve", "--root", "no-such-folder-anywhere")]
    [InlineData("serve", "--root", ".", "--nonsense")]
    [InlineData("explode")]
    public async Task ExitsTwoForAnInvocationItCannotUse(params string[] args)
    {
        var (code, output, error) = await RunAsync(args);

        Assert.Equal(BpmnMcpCli.Usage, code);
        Assert.Equal(string.Empty, output);
        Assert.NotEqual(string.Empty, error);
    }

    [Fact]
    public async Task PrintsHelpAndExitsZeroWhenAsked()
    {
        var (code, output, _) = await RunAsync("--help");

        Assert.Equal(BpmnMcpCli.Success, code);
        Assert.Contains("bpmn-mcp serve", output, StringComparison.Ordinal);
    }

    // --- serve, as a real child process ----------------------------------------------------------

    [Fact]
    public async Task ServesAFolderOverStdioKeepingStdoutForTheProtocolAndLogsOnStderr()
    {
        Write("claims/intake.bpmn", Diagram());
        var dll = typeof(BpmnMcpCli).Assembly.Location;
        var stderr = new List<string>();

        await using var client = await McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "bpmn-mcp",
                Command = "dotnet",
                Arguments = [dll, "serve", "--root", _folder],
                StandardErrorLines = line =>
                {
                    lock (stderr) stderr.Add(line);
                },
            }),
            cancellationToken: Token);

        var tools = await client.ListToolsAsync(cancellationToken: Token);
        var listed = await client.CallToolAsync("bpmn_list_documents", new Dictionary<string, object?> { ["recursive"] = true }, cancellationToken: Token);
        var validated = await client.CallToolAsync("bpmn_validate", new Dictionary<string, object?> { ["documentId"] = "claims/intake.bpmn" }, cancellationToken: Token);

        Assert.Equal(6, tools.Count);
        Assert.Contains("claims/intake.bpmn", ((TextContentBlock)listed.Content[0]).Text, StringComparison.Ordinal);
        Assert.False(validated.IsError);

        // The log of the call is on stderr, which is where a host looks; it had to stay off stdout for the above to work.
        SpinWait.SpinUntil(() =>
        {
            lock (stderr) return stderr.Any(line => line.Contains("bpmn_validate", StringComparison.Ordinal));
        }, TimeSpan.FromSeconds(5));
        lock (stderr) Assert.Contains(stderr, line => line.Contains("MCP tool bpmn_validate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OffersTheWritingToolsOverStdioOnlyWhenAskedAndSavesToTheFolder()
    {
        var dll = typeof(BpmnMcpCli).Assembly.Location;

        await using var readOnly = await McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions { Name = "bpmn-mcp", Command = "dotnet", Arguments = [dll, "serve", "--root", _folder] }),
            cancellationToken: Token);
        var readOnlyNames = (await readOnly.ListToolsAsync(cancellationToken: Token)).Select(tool => tool.Name).ToList();

        await using var writing = await McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions { Name = "bpmn-mcp", Command = "dotnet", Arguments = [dll, "serve", "--root", _folder, "--allow-writes"] }),
            cancellationToken: Token);
        var created = await writing.CallToolAsync("bpmn_create_document", new Dictionary<string, object?> { ["documentId"] = "made/by-agent.bpmn", ["name"] = "Made" }, cancellationToken: Token);
        var edited = await writing.CallToolAsync(
            "bpmn_create_element",
            new Dictionary<string, object?> { ["documentId"] = "made/by-agent.bpmn", ["type"] = "task", ["name"] = "Work" },
            cancellationToken: Token);

        Assert.DoesNotContain("bpmn_create_document", readOnlyNames);
        Assert.False(created.IsError);
        Assert.False(edited.IsError);
        Assert.Contains("Work", await File.ReadAllTextAsync(Path.Combine(_folder, "made", "by-agent.bpmn"), Token), StringComparison.Ordinal);
    }
}
