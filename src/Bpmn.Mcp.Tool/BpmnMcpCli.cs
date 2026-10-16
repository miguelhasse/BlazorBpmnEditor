namespace Hasseware.AspNetCore.Components.Bpmn.Mcp.Tool;

/// <summary>The <c>bpmn-mcp</c> command line: <c>serve</c> and <c>validate</c>.</summary>
/// <remarks>
/// Exit codes are the contract pipelines rely on: 0 for success, 1 for findings at or above the threshold, 2 for
/// a usage or I/O problem, so a broken invocation can never be mistaken for a clean diagram.
/// </remarks>
public static class BpmnMcpCli
{
    /// <summary>Exit code: everything is fine.</summary>
    public const int Success = 0;

    /// <summary>Exit code: validation found something at or above the threshold.</summary>
    public const int Findings = 1;

    /// <summary>Exit code: the command line or a file could not be used.</summary>
    public const int Usage = 2;

    /// <summary>Runs a command.</summary>
    /// <param name="args">The command line, without the program name.</param>
    /// <param name="output">Where results go; for <c>serve</c> this is never used, because stdout belongs to the protocol.</param>
    /// <param name="error">Where problems and logs go.</param>
    /// <param name="cancellationToken">Stops the command.</param>
    /// <returns>The exit code.</returns>
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            await output.WriteAsync(HelpText).ConfigureAwait(false);
            return args.Length == 0 ? Usage : Success;
        }

        return args[0] switch
        {
            "validate" => await ValidateCommand.RunAsync(args[1..], output, error, cancellationToken).ConfigureAwait(false),
            "serve" => await ServeCommand.RunAsync(args[1..], error, cancellationToken).ConfigureAwait(false),
            _ => await UnknownAsync(args[0], error).ConfigureAwait(false),
        };
    }

    internal static async Task<int> UsageErrorAsync(TextWriter error, string message)
    {
        await error.WriteLineAsync(message).ConfigureAwait(false);
        await error.WriteLineAsync("Run 'bpmn-mcp --help' for usage.").ConfigureAwait(false);
        return Usage;
    }

    private static Task<int> UnknownAsync(string command, TextWriter error) =>
        UsageErrorAsync(error, $"Unknown command '{command}'.");

    private const string HelpText = """
        bpmn-mcp (preview)

        Usage:
          bpmn-mcp serve --root <folder> [--tenant <name>] [--allow-writes]
              Serves the BPMN documents under <folder> to an AI agent over MCP on stdin/stdout.
              The agent can list, open, validate, describe, export and compare. With --allow-writes it can also
              create documents and edit them (edits are saved at the revision they were made against, so a
              concurrent change is refused). Without it nothing can change. Logs go to stderr.

          bpmn-mcp validate <path|folder|glob>... [--json] [--warnings-as-errors]
              Validates .bpmn, .xml and .json files with the editor's rules, for CI. A folder means every
              .bpmn file beneath it. Prints one line per finding: file: severity CODE message [element].
              Exits 0 when clean, 1 when there are errors (or warnings with --warnings-as-errors),
              2 when an argument or a file cannot be used.

        """;
}
