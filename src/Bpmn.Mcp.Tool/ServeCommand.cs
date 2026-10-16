using Hasseware.AspNetCore.Components.Bpmn.Storage;
using Hasseware.AspNetCore.Components.Bpmn.Storage.FileSystem;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hasseware.AspNetCore.Components.Bpmn.Mcp.Tool;

/// <summary><c>bpmn-mcp serve</c>: the MCP server over stdio, backed by a folder.</summary>
internal static class ServeCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter error, CancellationToken cancellationToken)
    {
        string? root = null;
        string? tenant = null;
        var allowWrites = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--root" when index + 1 < args.Length:
                    root = args[++index];
                    break;

                case "--tenant" when index + 1 < args.Length:
                    tenant = args[++index];
                    break;

                // Writing is the operator's decision, made out loud: nothing turns it on by default.
                case "--allow-writes":
                    allowWrites = true;
                    break;

                default:
                    return await BpmnMcpCli.UsageErrorAsync(error, $"Unexpected argument '{args[index]}'.").ConfigureAwait(false);
            }
        }

        if (string.IsNullOrWhiteSpace(root)) return await BpmnMcpCli.UsageErrorAsync(error, "serve needs --root <folder>.").ConfigureAwait(false);
        if (!Directory.Exists(root)) return await BpmnMcpCli.UsageErrorAsync(error, $"The folder '{root}' does not exist.").ConfigureAwait(false);
        if (tenant is not null && !BpmnDocumentKey.IsValidTenant(tenant))
        {
            return await BpmnMcpCli.UsageErrorAsync(error, "--tenant must be a single valid name (letters, digits, '-', '_', '.'; up to 128 characters).").ConfigureAwait(false);
        }

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });

        // Stdout carries the protocol and nothing else: every log line goes to stderr.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        var server = builder.Services
            .AddBpmnFileDocumentStore(root)
            .AddBpmnMcp(options =>
            {
                options.Tenant = tenant;
                options.AllowWrites = allowWrites;
            })
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithBpmnTools();
        if (allowWrites) server.WithBpmnWriteTools();

        using var host = builder.Build();
        await host.RunAsync(cancellationToken).ConfigureAwait(false);
        return BpmnMcpCli.Success;
    }
}
