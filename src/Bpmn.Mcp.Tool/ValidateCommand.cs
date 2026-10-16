using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Tools;

using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Hasseware.AspNetCore.Components.Bpmn.Mcp.Tool;

/// <summary><c>bpmn-mcp validate</c>: the editor's validation, for pipelines that have no MCP client.</summary>
/// <remarks>
/// It runs the same handler an agent's <c>bpmn_validate</c> does, so a build fails on exactly what an agent was
/// told. Only the editor's own rules run; profile validators belong to hosts that register them.
/// </remarks>
internal static class ValidateCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    // CI wants every finding, not a page of them.
    private static readonly BpmnToolLimits Limits = BpmnToolLimits.Default with { MaxElements = 100_000 };

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var json = false;
        var warningsAsErrors = false;
        var inputs = new List<string>();

        foreach (var argument in args)
        {
            switch (argument)
            {
                case "--json":
                    json = true;
                    break;

                case "--warnings-as-errors":
                    warningsAsErrors = true;
                    break;

                default:
                    if (argument.StartsWith('-')) return await BpmnMcpCli.UsageErrorAsync(error, $"Unknown option '{argument}'.").ConfigureAwait(false);
                    inputs.Add(argument);
                    break;
            }
        }

        if (inputs.Count == 0) return await BpmnMcpCli.UsageErrorAsync(error, "validate needs at least one file, folder or pattern.").ConfigureAwait(false);

        var files = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            List<string> matched;
            try
            {
                matched = Expand(input).ToList();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // A folder that cannot be read is a problem with the invocation, which exits 2 as the help says, not an unhandled exception.
                return await BpmnMcpCli.UsageErrorAsync(error, $"'{input}' could not be read: {exception.Message}").ConfigureAwait(false);
            }

            if (matched.Count == 0) return await BpmnMcpCli.UsageErrorAsync(error, $"'{input}' matches no file.").ConfigureAwait(false);
            files.UnionWith(matched);
        }

        var reports = new List<FileReport>();
        var problem = false;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var report = await ValidateFileAsync(file, cancellationToken).ConfigureAwait(false);
            problem |= report.Unreadable;
            reports.Add(report);
        }

        var errors = reports.Sum(report => report.Errors);
        var warnings = reports.Sum(report => report.Warnings);

        if (json) await WriteJsonAsync(output, reports, errors, warnings).ConfigureAwait(false);
        else await WriteTextAsync(output, reports, errors, warnings).ConfigureAwait(false);

        if (problem) return BpmnMcpCli.Usage;
        return errors > 0 || (warningsAsErrors && warnings > 0) ? BpmnMcpCli.Findings : BpmnMcpCli.Success;
    }

    private static async Task<FileReport> ValidateFileAsync(string file, CancellationToken cancellationToken)
    {
        string text;
        try
        {
            text = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return FileReport.Failure(file, "READ-FAILED", "The file could not be read.", unreadable: true);
        }

        BpmnImportResult import;
        try
        {
            import = BpmnFileFormats.Read(text, file);
        }
        catch (BpmnImportException)
        {
            return FileReport.Failure(file, BpmnToolCodes.InvalidDocument, "The file cannot be read as BPMN.", unreadable: false);
        }

        var findings = import.Diagnostics
            .Where(finding => finding.Severity != BpmnImportSeverity.Info)
            .Select(finding => new Finding(
                finding.Severity switch
                {
                    BpmnImportSeverity.Degraded => "IMPORT-DEGRADED",
                    BpmnImportSeverity.Dropped => "IMPORT-DROPPED",
                    _ => "IMPORT-SCHEMA-ERROR",
                },
                finding.Severity == BpmnImportSeverity.SchemaError ? "error" : "warning",
                finding.Message,
                finding.ElementId))
            .ToList();

        var host = new BpmnDocumentToolHost(import.Document, BpmnEditOrigin.FromAgent("bpmn-mcp"), limits: Limits) { IsReadOnly = true };
        var result = await BpmnTools.ValidateAsync(host, cancellationToken).ConfigureAwait(false);
        findings.AddRange(result.Diagnostics.Select(diagnostic => new Finding(diagnostic.Code, diagnostic.Severity, diagnostic.Message?.UntrustedText ?? string.Empty, diagnostic.ElementId)));

        return new FileReport(
            file,
            Unreadable: false,
            findings.Count(finding => finding.Severity == "error"),
            findings.Count(finding => finding.Severity == "warning"),
            findings);
    }

    // A folder means every .bpmn beneath it; a pattern is a glob; anything else is a file.
    private static IEnumerable<string> Expand(string input)
    {
        if (Directory.Exists(input))
        {
            return Directory.EnumerateFiles(input, "*.bpmn", SearchOption.AllDirectories).Select(Path.GetFullPath);
        }

        if (input.Contains('*', StringComparison.Ordinal) || input.Contains('?', StringComparison.Ordinal))
        {
            var (baseDirectory, pattern) = SplitPattern(input);
            if (!Directory.Exists(baseDirectory)) return [];

            var matcher = new Matcher(StringComparison.Ordinal);
            matcher.AddInclude(pattern);
            return matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(baseDirectory))).Files.Select(match => Path.GetFullPath(Path.Combine(baseDirectory, match.Path)));
        }

        return File.Exists(input) ? [Path.GetFullPath(input)] : [];
    }

    private static (string BaseDirectory, string Pattern) SplitPattern(string input)
    {
        var normalized = input.Replace('\\', '/');
        var segments = normalized.Split('/');
        var fixedSegments = segments.TakeWhile(segment => !segment.Contains('*', StringComparison.Ordinal) && !segment.Contains('?', StringComparison.Ordinal)).ToList();
        var baseDirectory = fixedSegments.Count == 0 ? "." : string.Join('/', fixedSegments);
        if (normalized.StartsWith('/') && fixedSegments.Count <= 1) baseDirectory = "/";

        return (baseDirectory, string.Join('/', segments.Skip(fixedSegments.Count)));
    }

    private static async Task WriteTextAsync(TextWriter output, List<FileReport> reports, int errors, int warnings)
    {
        foreach (var report in reports)
        {
            foreach (var finding in report.Findings)
            {
                var element = finding.ElementId is null ? string.Empty : $" [{finding.ElementId}]";
                await output.WriteLineAsync($"{report.Path}: {finding.Severity} {finding.Code} {OneLine(finding.Message)}{element}").ConfigureAwait(false);
            }
        }

        await output.WriteLineAsync($"{reports.Count} file(s), {errors} error(s), {warnings} warning(s)").ConfigureAwait(false);
    }

    private static async Task WriteJsonAsync(TextWriter output, List<FileReport> reports, int errors, int warnings)
    {
        var document = new
        {
            files = reports.Select(report => new { path = report.Path, errors = report.Errors, warnings = report.Warnings, findings = report.Findings }),
            errors,
            warnings,
        };

        await output.WriteLineAsync(JsonSerializer.Serialize(document, JsonOptions)).ConfigureAwait(false);
    }

    private static string OneLine(string text) => text.ReplaceLineEndings(" ");

    private sealed record Finding(string Code, string Severity, string Message, string? ElementId);

    private sealed record FileReport(string Path, bool Unreadable, int Errors, int Warnings, List<Finding> Findings)
    {
        // A file that cannot be read at all is a problem with the invocation; one that is not BPMN is a finding.
        public static FileReport Failure(string path, string code, string message, bool unreadable) =>
            new(path, unreadable, Errors: 1, Warnings: 0, [new Finding(code, "error", message, null)]);
    }
}
