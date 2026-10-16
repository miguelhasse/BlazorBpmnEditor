using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Bpmn.E2E.Tests;

public sealed class DemoLocalizationTests
{
    private static readonly string[] SupportedCultures =
    [
        "cs", "de", "es", "fr", "it", "ja", "ko", "pl", "pt-BR", "ru", "tr", "zh-Hans", "zh-Hant",
    ];

    [Theory]
    [InlineData("Bpmn.Demo.Client", "DemoClientStrings")]
    [InlineData("Bpmn.Demo", "DemoStrings")]
    public void LocalizerAllLiteralLookupsHaveNeutralResources(string project, string marker)
    {
        var directory = ProjectDirectory(project);
        var resources = ReadResources(directory, marker);
        var sources = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => path.EndsWith(".razor", StringComparison.Ordinal) || path.EndsWith(".razor.cs", StringComparison.Ordinal))
            .Select(File.ReadAllText);

        var keys = sources.SelectMany(source => Regex.Matches(source, @"Localizer\[""([^""]+)"""))
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(keys);
        Assert.All(keys, key => Assert.True(resources.ContainsKey(key), $"Missing {marker} resource: {key}"));
    }

    [Theory]
    [InlineData("Bpmn.Demo.Client", "DemoClientStrings")]
    [InlineData("Bpmn.Demo", "DemoStrings")]
    public void XliffAllSupportedCulturesPreserveSourcesAndPlaceholders(string project, string marker)
    {
        var directory = ProjectDirectory(project);
        var resources = ReadResources(directory, marker);
        var xlfDirectory = Path.Combine(directory, "Resources", "xlf");
        XNamespace ns = "urn:oasis:names:tc:xliff:document:1.2";

        Assert.Equal(SupportedCultures.Length, Directory.GetFiles(xlfDirectory, "*.xlf").Length);
        Assert.All(SupportedCultures, culture =>
        {
            var document = XDocument.Load(Path.Combine(xlfDirectory, $"{marker}.{culture}.xlf"));
            var units = document.Descendants(ns + "trans-unit")
                .ToDictionary(unit => (string)unit.Attribute("id")!, StringComparer.Ordinal);

            Assert.Equal(culture, (string?)document.Descendants(ns + "file").Single().Attribute("target-language"));
            Assert.Equal(resources.Keys.Order(StringComparer.Ordinal), units.Keys.Order(StringComparer.Ordinal));
            Assert.All(resources, resource =>
            {
                var unit = units[resource.Key];
                var target = unit.Element(ns + "target")!;
                Assert.Equal(resource.Value, unit.Element(ns + "source")!.Value);
                Assert.Equal(Placeholders(resource.Value), Placeholders(target.Value));
                if ((string?)target.Attribute("state") == "new")
                {
                    Assert.Equal(resource.Value, target.Value);
                }
            });
        });
    }

    [Fact]
    public void ReconnectCountdownResourcePreservesLiveElementPlaceholder()
    {
        var resources = ReadResources(ProjectDirectory("Bpmn.Demo"), "DemoStrings");

        var placeholders = Placeholders(resources["Reconnect.RetryIn"]);

        Assert.Equal(["{0}"], placeholders);
    }

    [Theory]
    [InlineData("ImportSeverity.Info")]
    [InlineData("ImportSeverity.Degraded")]
    [InlineData("ImportSeverity.Dropped")]
    [InlineData("ImportSeverity.SchemaError")]
    [InlineData("ExportSeverity.Info")]
    [InlineData("ExportSeverity.Normalized")]
    [InlineData("ExportSeverity.Dropped")]
    [InlineData("Gallery.Events")]
    [InlineData("Gallery.Gateways")]
    [InlineData("Gallery.Activities")]
    [InlineData("Gallery.Connectors")]
    [InlineData("Gallery.Swimlanes")]
    public void LocalizerComputedKeysHaveNeutralResources(string key)
    {
        var resources = ReadResources(ProjectDirectory("Bpmn.Demo.Client"), "DemoClientStrings");

        Assert.True(resources.ContainsKey(key), $"Missing computed resource: {key}");
    }

    private static string[] Placeholders(string value) =>
        Regex.Matches(value, @"\{\d+(?:[^{}]*)\}")
            .Select(match => match.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static Dictionary<string, string> ReadResources(string directory, string marker) =>
        XDocument.Load(Path.Combine(directory, "Resources", $"{marker}.resx"))
            .Root!.Elements("data")
            .ToDictionary(data => (string)data.Attribute("name")!, data => data.Element("value")!.Value, StringComparer.Ordinal);

    private static string ProjectDirectory(string project)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "playground", project);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the demo project from the test output folder.");
    }
}
