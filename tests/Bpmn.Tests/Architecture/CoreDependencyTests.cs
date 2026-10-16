using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Architecture;

/// <summary>
/// Core stays free of cloud, database, transport and agent-protocol dependencies (design doc, "Keeping Core
/// clean").
/// </summary>
/// <remarks>
/// <see cref="Storage.IBpmnDocumentStore"/> lives in Core so the editor, agent tools and hosts share one
/// persistence seam. That only stays affordable while Core references none of the things a store, bus or
/// transport is built on: those belong in optional packages. Asserted mechanically, because "just one
/// helper from the Azure SDK" is exactly the change that slips through review.
/// </remarks>
public sealed partial class CoreDependencyTests
{
    private static readonly Assembly Core = typeof(BpmnCanvas).Assembly;

    private static readonly string[] Forbidden =
    [
        "Azure.",
        "AWSSDK.",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Data.",
        "System.Data.",
        "Npgsql",
        "MySql",
        "Microsoft.Extensions.Http",
        "Microsoft.AspNetCore.SignalR",
        "Microsoft.AspNetCore.Http.Connections",
        "StackExchange.Redis",
        "RabbitMQ",
        "Confluent.Kafka",
        "ModelContextProtocol",
    ];

    private static readonly string SourceRoot = FindSourceRoot();

    [Fact]
    public void CoreReferencesNoCloudDatabaseTransportOrProtocolAssembly()
    {
        var offenders = Core.GetReferencedAssemblies()
            .Select(name => name.Name!)
            .Where(IsForbidden)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void CoreDeclaresNoCloudDatabaseTransportOrProtocolPackage()
    {
        var project = XDocument.Load(Path.Combine(SourceRoot, "Bpmn", "Hasseware.AspNetCore.Components.Bpmn.csproj"));

        var offenders = project.Descendants("PackageReference")
            .Select(reference => (string?)reference.Attribute("Include") ?? string.Empty)
            .Where(IsForbidden)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void StorageNeverTouchesTheFileSystem()
    {
        // Core targets the browser, where a file store is meaningless; it lives in its own package.
        var offenders = Directory.EnumerateFiles(Path.Combine(SourceRoot, "Bpmn", "Storage"), "*.cs")
            .Where(file => FileSystemUse().IsMatch(File.ReadAllText(file)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }

    private static bool IsForbidden(string name) =>
        Forbidden.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"\b(File|Directory|FileInfo|DirectoryInfo|FileStream|FileSystemWatcher)\s*[\.(]")]
    private static partial Regex FileSystemUse();

    private static string FindSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src");
            if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(candidate, "Bpmn")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository 'src' directory from the test output folder.");
    }
}
