using System.Globalization;
using System.Resources;
using System.Text.Json;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;
using Hasseware.AspNetCore.Components.Bpmn.Resources;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Localization;

public sealed class BpmnDiagnosticLocalizationTests
{
    private static readonly ResourceManager Resources = new(typeof(BpmnRuleMessages));

    [Fact]
    public void EveryCatalogRuleHasExactlyItsInvariantEnglishMessage()
    {
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "rules", "bpmn-rules.json")));
        foreach (var rule in catalog.RootElement.GetProperty("rules").EnumerateArray())
        {
            Assert.Equal(rule.GetProperty("message").GetString(),
                Resources.GetString(rule.GetProperty("id").GetString()!, CultureInfo.InvariantCulture));
        }
    }

    [Theory]
    [InlineData("simple-process.bpmn")]
    [InlineData("collaboration.bpmn")]
    [InlineData("nested-subprocesses.bpmn")]
    [InlineData("linked-processes.bpmn")]
    [InlineData("vendor-extensions.bpmn")]
    [InlineData("zeebe-process.bpmn")]
    [InlineData("zeebe-extensions.bpmn")]
    [InlineData("global-catalog.bpmn")]
    [InlineData("large-virtualization-stress.bpmn")]
    [InlineData("order-process.bpmn")]
    [InlineData("pizza-store.bpmn")]
    [InlineData("self-serve-restaurant.bpmn")]
    [InlineData("recruitment-and-selection.bpmn")]
    [InlineData("smart-parking.bpmn")]
    public void EveryFindingInSampleCorpusResolvesIncludingDetails(string fileName)
    {
        var path = Path.Combine(RepositoryRoot(), "playground", "Bpmn.Demo", "wwwroot", "samples", fileName);
        var document = BpmnXmlReader.Read(File.ReadAllText(path)).Document;
        AssertResolvable(BpmnValidator.Validate(document, [new BpmnElementTemplateValidator(new BpmnTemplateOptions())]));
    }

    [Fact]
    public void NativeAndProfileFindingsResolveWithoutChangingEnglish()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Start", BpmnFlowNodeType.StartEvent, new Bounds(80, 100, 36, 36))
            .Node("Task", BpmnFlowNodeType.Task, new Bounds(200, 90, 100, 80))
            .Flow("Back", "Task", "Start", [new DiPoint(200, 130), new DiPoint(116, 118)])
            .Build();
        document = document with
        {
            FlowNodes = [.. document.FlowNodes,
                new BpmnFlowNode("Boundary", BpmnFlowNodeType.BoundaryEvent, "Missing", AttachedToRef: "MissingHost"),
                new BpmnFlowNode("Call", BpmnFlowNodeType.CallActivity, "Missing", CalledElementRef: "Missing", FlowElementsOwned: true)],
            Diagrams = [document.Diagrams[0] with
            {
                Plane = document.Diagrams[0].Plane with
                {
                    Shapes = [document.Diagrams[0].Plane.Shapes[0] with { Bounds = new Bounds(0, 0, 0, 0), IsHorizontal = true }],
                    Edges = [document.Diagrams[0].Plane.Edges[0] with { Waypoints = [] }],
                },
            }],
        };
        var report = BpmnValidator.Validate(document, [new InvalidProfile(), new ThrowingProfile()]);
        Assert.Contains(report.Diagnostics, d => d.RuleId == "CT-001");
        Assert.Contains(report.Diagnostics, d => d.RuleId == "CT-005");
        Assert.Contains(report.Diagnostics, d => d.RuleId == "DI-001");
        Assert.Contains(report.Diagnostics, d => d.RuleId == "DI-002");
        Assert.Contains(report.Diagnostics, d => d.RuleId == "DI-007");
        Assert.Contains(report.Diagnostics, d => d.RuleId == "PROF-000");
        AssertResolvable(report);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TemplateProfileLifecycleAndConstraintFindingsResolve(bool customMessage)
    {
        var options = new BpmnTemplateOptions();
        var binding = new BpmnTemplateBinding("property", new Dictionary<string, string> { ["name"] = "name" });
        var template = new BpmnElementTemplate
        {
            Id = "mail",
            Name = "Mail",
            Version = 1,
            AppliesTo = ["bpmn:Task"],
            Deprecated = new(customMessage ? "Host deprecation wording." : null),
            Properties =
            [
                new() { Id = "required", Label = "Recipient", Binding = binding, Constraints = new(NotEmpty: true) },
                new() { Id = "length", Label = "Recipient", Binding = binding, Constraints = new(MinLength: 3, MaxLength: 1) },
                new() { Id = "feel", Label = "Recipient", Binding = binding, Feel = BpmnTemplateFeel.Required },
                new() { Id = "pattern", Label = "Recipient", Binding = binding,
                    Constraints = new(Pattern: new("^Z", customMessage ? "Host pattern wording." : null)) },
                new() { Id = "invalid", Label = "Recipient", Binding = binding, Constraints = new(Pattern: new("[")) },
            ],
        };
        Assert.True(options.Registry.Add(template).Added);
        Assert.True(options.Registry.Add(template with { Version = 2, Deprecated = null }).Added);
        var document = new BpmnDocumentBuilder()
            .Node("Task", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80), "ab")
            .Build();
        document = document with
        {
            FlowNodes = [document.FlowNodes[0] with
            {
                Extras = new BpmnElementExtras
                {
                    ForeignAttributes = new Dictionary<string, string>
                    {
                        [$"{{{BpmnTemplateMarkers.ZeebeNamespaceUri}}}modelerTemplate"] = "mail",
                        [$"{{{BpmnTemplateMarkers.ZeebeNamespaceUri}}}modelerTemplateVersion"] = "1",
                    },
                },
            }],
        };
        var report = BpmnValidator.Validate(document, [new BpmnElementTemplateValidator(options)]);
        foreach (var code in new[] { "PROF-101", "PROF-102", "PROF-104", "PROF-105" })
            Assert.Contains(report.Diagnostics, d => d.RuleId == code);
        AssertResolvable(report);
        var emptyName = document with { FlowNodes = [document.FlowNodes[0] with { Name = null }] };
        AssertResolvable(BpmnValidator.Validate(emptyName, [new BpmnElementTemplateValidator(options)]));
        AssertResolvable(BpmnValidator.Validate(document, [new BpmnElementTemplateValidator(new BpmnTemplateOptions())]));
    }

    [Fact]
    public void UnsafeExtensionReasonsAreTranslatedAlongWithCatalogDetails()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Build();
        document = document with
        {
            FlowNodes = [document.FlowNodes[0] with
            {
                Extras = new BpmnElementExtras
                {
                    ForeignAttributes = new Dictionary<string, string> { ["url"] = "javascript:example" },
                    Extensions = [new("http://www.w3.org/1999/xhtml", "script",
                        new Dictionary<string, string> { ["onclick"] = "example", ["href"] = "javascript:example" }, [])],
                },
            }],
        };
        var report = BpmnValidator.Validate(document);
        AssertResolvable(report);
        var url = Assert.Single(report.Diagnostics, d => d.DetailResourceKey == "EXT-010.Detail.Url" && d.MessageArguments?[0]?.ToString() == "url");
        Assert.Equal("Unsicher. url: URL mit Schema 'javascript:'.",
            Translated(("EXT-010", "Unsicher."), ("EXT-010.Detail.Url", "{0}: URL mit Schema '{1}'.")).Localize(url));
    }

    [Fact]
    public void UnknownRuleFallsBackToOriginalMessageIncludingProfileSuffix()
    {
        using var provider = new ServiceCollection().AddBpmnComponents().BuildServiceProvider();
        var diagnostic = new BpmnDiagnostic("PROF-099", BpmnSeverity.Warning, "Host wording (host)") { ProfileName = "host" };
        Assert.Equal(diagnostic.Message, provider.GetRequiredService<BpmnDiagnosticLocalizer>().Localize(diagnostic));
    }

    [Fact]
    public void ArgumentsUseCurrentUiCultureNotCurrentFormattingCulture()
    {
        using var culture = new CultureScope();
        var diagnostic = new BpmnDiagnostic("PROF-103", BpmnSeverity.Info, "Template not found: 'Mail' version 2.")
        {
            MessageArguments = ["Mail", 2.5],
        };
        var localizer = Translated(("PROF-103", "Vorlage '{0}' Version {1:N1} fehlt."));
        Assert.Equal("Vorlage 'Mail' Version 2,5 fehlt.", localizer.Localize(diagnostic));
        Assert.Equal("Template not found: 'Mail' version 2.", diagnostic.Message);
    }

    [Fact]
    public void CatalogDetailsAndProfileSuffixAreRetainedAndTranslated()
    {
        using var culture = new CultureScope();
        var diagnostic = BpmnDiagnostic.From(BpmnRules.D001, "Start", "Flow", "'Start' has 2.",
            ["'Start'", 2], "D001.Detail") with
        { ProfileName = "engine" };
        var localizer = Translated(("D001", "Startereignis darf keinen Eingang haben."),
            ("D001.Detail", "{0} hat {1} Eingänge."));
        Assert.Equal("Startereignis darf keinen Eingang haben. 'Start' hat 2 Eingänge. (engine)", localizer.Localize(diagnostic));
        Assert.Equal($"{BpmnRules.D001.Message} 'Start' has 2.", diagnostic.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Missing.Detail")]
    public void MissingDetailTranslationRetainsSuppliedDetail(string? detailKey)
    {
        var diagnostic = BpmnDiagnostic.From(BpmnRules.D001, detail: "Host detail.", detailResourceKey: detailKey);
        Assert.Equal("Translated. Host detail.", Translated(("D001", "Translated.")).Localize(diagnostic));
    }

    [Fact]
    public void NativeWordingUsesItsSpecificResourceRatherThanGenericRule()
    {
        var diagnostic = new BpmnDiagnostic("CT-005", BpmnSeverity.Error, "Boundary Event host 'Host' does not resolve to a flow node.")
        {
            MessageResourceKey = "CT-005.Native.3",
            MessageArguments = ["Host"],
        };
        Assert.Equal("Host fehlt.", Translated(("CT-005", "Generic."), ("CT-005.Native.3", "{0} fehlt.")).Localize(diagnostic));
    }

    [Fact]
    public void ProfileRunnerPreservesArgumentsAndSuffix()
    {
        var diagnostic = Assert.Single(BpmnProfileRunner.Run(new BpmnDocumentBuilder().Build(), [new TranslatedProfile()]));
        Assert.Equal("Template 'Mail' is deprecated. (templates)", diagnostic.Message);
        Assert.Equal("Vorlage 'Mail' ist veraltet. (templates)",
            Translated(("PROF-104", "Vorlage '{0}' ist veraltet.")).Localize(diagnostic));
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("PROF-104")]
    public void MissingOrMalformedSpecificResourceFallsBack(string key)
    {
        var diagnostic = new BpmnDiagnostic("PROF-104", BpmnSeverity.Warning, "Custom message.")
        {
            MessageResourceKey = key,
            MessageArguments = [],
        };
        Assert.Equal(diagnostic.Message, Translated(("PROF-104", "Invalid {1}")).Localize(diagnostic));
    }

    [Fact]
    public void ParameterizedResourceWithoutCapturedArgumentsFallsBack()
    {
        var diagnostic = new BpmnDiagnostic("PROF-103", BpmnSeverity.Info, "Host diagnostic without metadata.");
        Assert.Equal(diagnostic.Message, Translated(("PROF-103", "Template '{0}' missing.")).Localize(diagnostic));
    }

    [Fact]
    public void PresentationMetadataDoesNotChangeEqualityOrPositionalShape()
    {
        var original = new BpmnDiagnostic("D001", BpmnSeverity.Error, "Message", "Start", "Flow");
        var localized = original with { MessageArguments = [1], MessageResourceKey = "Format", ProfileName = "Profile" };
        Assert.Equal(original, localized);
        Assert.Equal(original.GetHashCode(), localized.GetHashCode());
        var (rule, severity, message, element, related) = localized;
        Assert.Equal(("D001", BpmnSeverity.Error, "Message", "Start", "Flow"), (rule, severity, message, element, related));
    }

    private static void AssertResolvable(BpmnValidationReport report)
    {
        using var provider = new ServiceCollection().AddBpmnComponents().BuildServiceProvider();
        var localizer = provider.GetRequiredService<BpmnDiagnosticLocalizer>();
        foreach (var diagnostic in report.Diagnostics)
        {
            Assert.NotNull(Resources.GetString(diagnostic.RuleId, CultureInfo.InvariantCulture));
            if (diagnostic.MessageResourceKey is { } messageKey)
                Assert.NotNull(Resources.GetString(messageKey, CultureInfo.InvariantCulture));
            if (diagnostic.DetailResourceKey is { } detailKey)
                Assert.NotNull(Resources.GetString(detailKey, CultureInfo.InvariantCulture));
            Assert.Equal(diagnostic.Message, localizer.Localize(diagnostic));
        }
    }

    private static BpmnDiagnosticLocalizer Translated(params (string Key, string Value)[] entries) =>
        new(new TestLocalizer(entries.ToDictionary(entry => entry.Key, entry => entry.Value)));

    private sealed class TestLocalizer(Dictionary<string, string> entries) : IStringLocalizer<BpmnRuleMessages>
    {
        public LocalizedString this[string name] => entries.TryGetValue(name, out var value)
            ? new(name, value) : new(name, name, resourceNotFound: true);
        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(CultureInfo.CurrentCulture, this[name].Value, arguments), this[name].ResourceNotFound);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
            entries.Select(entry => new LocalizedString(entry.Key, entry.Value));
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
        public CultureScope()
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        }
        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }

    private sealed class InvalidProfile : IBpmnProfileValidator
    {
        public string Name => "invalid";
        public IEnumerable<BpmnProfileDiagnostic> Validate(BpmnDocument document) => [new("D001", BpmnSeverity.Error, "Invalid.")];
    }

    private sealed class ThrowingProfile : IBpmnProfileValidator
    {
        public string Name => "throwing";
        public IEnumerable<BpmnProfileDiagnostic> Validate(BpmnDocument document) => throw new InvalidOperationException("Failure.");
    }

    private sealed class TranslatedProfile : IBpmnProfileValidator
    {
        public string Name => "templates";
        public IEnumerable<BpmnProfileDiagnostic> Validate(BpmnDocument document) =>
            [new("PROF-104", BpmnSeverity.Warning, "Template 'Mail' is deprecated.") { MessageArguments = ["Mail"] }];
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "rules", "bpmn-rules.json"))) return directory.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
}
