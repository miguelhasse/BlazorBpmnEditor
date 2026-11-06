using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Localization;

public sealed class XliffCoverageTests
{
    private static readonly string[] Languages = ["cs", "de", "es", "fr", "it", "ja", "ko", "pl", "pt-BR", "ru", "tr", "zh-Hans", "zh-Hant"];
    private static readonly HashSet<string> IntentionallyInvariantMessages =
        ["EXT-010.Detail", "PROF-101.Detail.Custom", "PROF-104.Detail.Custom"];
    public static TheoryData<string, string> Resources
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var project in new[]
            {
                Path.Combine("src", "Bpmn"),
                Path.Combine("src", "BpmnEditor"),
                Path.Combine("src", "FluentUI.BpmnEditor"),
                Path.Combine("playground", "Bpmn.Demo"),
                Path.Combine("playground", "Bpmn.Demo.Client"),
            })
                foreach (var resx in Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), project), "*.resx", SearchOption.AllDirectories)
                    .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
                    foreach (var language in Languages) data.Add(Path.GetRelativePath(RepositoryRoot(), resx), language);
            return data;
        }
    }

    [Theory, MemberData(nameof(Resources))]
    public void EveryResourceHasCurrentTranslationsForAllSupportedLanguages(string relativePath, string language)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath);
        var source = XDocument.Load(path).Descendants("data").ToDictionary(entry => (string)entry.Attribute("name")!, entry => entry.Element("value")!.Value);
        var xlfPath = Path.Combine(Path.GetDirectoryName(path)!, "xlf", $"{Path.GetFileNameWithoutExtension(path)}.{language}.xlf");
        Assert.True(File.Exists(xlfPath), $"Missing translation file: {xlfPath}");
        var xlf = XDocument.Load(xlfPath);
        XNamespace ns = "urn:oasis:names:tc:xliff:document:1.2";
        Assert.Equal(language, (string?)xlf.Descendants(ns + "file").Single().Attribute("target-language"));
        var units = xlf.Descendants(ns + "trans-unit").ToArray();
        var translated = units.ToDictionary(unit => (string)unit.Attribute("id")!, unit => unit.Element(ns + "source")!.Value);
        Assert.Equal(source.Keys.Order(), translated.Keys.Order());
        foreach (var (key, value) in source) Assert.Equal(value, translated[key]);

        if (relativePath == Path.Combine("src", "Bpmn", "Resources", "BpmnRuleMessages.resx"))
            foreach (var unit in units)
            {
                var target = unit.Element(ns + "target");
                Assert.NotNull(target);
                Assert.NotEqual("new", (string?)target.Attribute("state"));
                Assert.False(string.IsNullOrWhiteSpace(target.Value));
                if (!IntentionallyInvariantMessages.Contains((string)unit.Attribute("id")!))
                    Assert.NotEqual(unit.Element(ns + "source")!.Value, target.Value);
                Assert.Equal(
                    Placeholders(unit.Element(ns + "source")!.Value),
                    Placeholders(target.Value));
            }
    }

    private static string[] Placeholders(string value) =>
        Regex.Matches(value, @"\{(?<index>\d+)(?:,[^}:]+)?(?::[^}]+)?\}")
            .Select(match => match.Groups["index"].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Bpmn"))) return directory.FullName;
        throw new InvalidOperationException("Cannot locate repository root.");
    }
}
