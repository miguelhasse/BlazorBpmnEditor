using System.Collections;
using System.Text.Json;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Validation;

public sealed class TemplateValidationPerformanceTests
{
    [Theory]
    [InlineData(false, "upgrade")]
    [InlineData(true, "upgrade")]
    [InlineData(true, "missing")]
    [InlineData(true, "flavor")]
    [InlineData(false, "deprecated")]
    [InlineData(true, "engine")]
    public void CompleteOrderedDiagnosticsMatchFullInspection(bool scoped, string scenario)
    {
        var template = Template(scoped);
        if (scenario == "deprecated") template = template with { Deprecated = new("Host deprecation") };
        var options = new BpmnTemplateOptions();
        if (scenario != "missing")
            options.Registry.Add(scenario == "flavor" ? template with { Flavor = BpmnTemplateFlavor.Zeebe } : template);
        options.Registry.Add(template with
        {
            Version = 2,
            Deprecated = null,
            Engines = new Dictionary<string, string> { ["camunda"] = ">=8.5" },
        });
        options.Engines["camunda"] = scenario == "engine" ? "7.0.0" : "8.6.0";
        var document = Document(template);
        AssertReportsMatch(document, options);
        var changed = document with
        {
            FlowNodes = document.FlowNodes.Select((node, index) => node with { Name = index % 2 == 0 ? "" : "=value" }).ToArray(),
        };
        AssertReportsMatch(changed, options);
        AssertReportsMatch(document, options);
    }

    [Fact]
    public void EveryFlowNodeAndEventIdentityMatchesFullInspection()
    {
        foreach (var type in Enum.GetValues<BpmnFlowNodeType>())
        {
            foreach (var definition in Enum.GetValues<BpmnEventDefinitionType>())
            {
                var template = Template(false) with { AppliesTo = ["bpmn:BaseElement"] };
                var options = new BpmnTemplateOptions();
                options.Registry.Add(template with
                {
                    Version = 2,
                    ElementType = new("bpmn:ServiceTask", "bpmn:" + definition + "EventDefinition"),
                });
                var node = new BpmnFlowNode("Node", type, "Process")
                {
                    EventDefinitions = definition == BpmnEventDefinitionType.None ? [] : [new(definition)],
                };
                var document = new BpmnDocument { Definitions = new("Definitions", "urn:test"), Processes = [new("Process")], FlowNodes = [node] };
                var snapshot = BpmnElementInspector.Describe(document, node.Id)!;

                Assert.Same(BpmnTemplateSelection.Upgrade(snapshot, template, options),
                    BpmnTemplateSelection.Upgrade(document, node.Id, template, options));
            }
        }
    }

    [Fact]
    public void RegistryAndEngineChangesRemainVisibleBetweenValidations()
    {
        var template = Template(true);
        var document = Document(template);
        var options = new BpmnTemplateOptions();
        var profile = new BpmnElementTemplateValidator(options);
        Assert.All(profile.Validate(document), diagnostic => Assert.Equal("PROF-103", diagnostic.RuleId));
        options.Registry.Add(template);
        Assert.DoesNotContain(profile.Validate(document), diagnostic => diagnostic.RuleId == "PROF-105");
        options.Registry.Add(template with { Version = 2, Engines = new Dictionary<string, string> { ["camunda"] = ">=8.5" } });
        Assert.Contains(profile.Validate(document), diagnostic => diagnostic.RuleId == "PROF-105");
        options.Engines["camunda"] = "7.0.0";
        Assert.DoesNotContain(profile.Validate(document), diagnostic => diagnostic.RuleId == "PROF-105");
        AssertReportsMatch(document, options);
    }

    [Fact]
    public void UpgradeSelectionMatchesFullInspectionForEveryShippedElement()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "playground")))
            directory = directory.Parent;
        var samples = Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."),
            "playground", "Bpmn.Demo", "wwwroot", "samples");
        var files = Directory.GetFiles(samples, "*.bpmn");
        Assert.NotEmpty(files);
        var template = Template(false) with { AppliesTo = ["bpmn:BaseElement"] };
        var options = new BpmnTemplateOptions();
        options.Registry.Add(template with { Version = 2 });
        foreach (var file in files)
        {
            var document = BpmnXmlReader.Read(File.ReadAllText(file)).Document;
            foreach (var id in BpmnValidator.Extended(document).Select(item => item.Id).Distinct(StringComparer.Ordinal))
            {
                var snapshot = BpmnElementInspector.Describe(document, id);
                var expected = snapshot is null ? null : BpmnTemplateSelection.Upgrade(snapshot, template, options);
                Assert.Same(expected, BpmnTemplateSelection.Upgrade(document, id, template, options));
            }
        }
    }

    [Fact]
    public void UnknownBindingStillFailsExplicitly()
    {
        var template = Template(false) with { Properties = [new() { Binding = new("unknown", new Dictionary<string, string>()) }] };
        var document = Document(template);
        var options = new BpmnTemplateOptions();
        options.Registry.Add(template);

        var expected = Assert.Throws<InvalidOperationException>(() => new FullInspectionProfile(options).Validate(document).ToArray());
        var actual = Assert.Throws<InvalidOperationException>(() => new BpmnElementTemplateValidator(options).Validate(document).ToArray());

        Assert.Equal(expected.Message, actual.Message);
        AssertReportsMatch(document, options);
    }

    [Fact]
    public void ProfileVisitsEachShapeOnlyOnce()
    {
        var template = Template(false);
        var document = Document(template);
        var diagram = document.Diagrams[0];
        var shapes = new CountingShapes(diagram.Plane.Shapes);
        document = document with { Diagrams = [diagram with { Plane = diagram.Plane with { Shapes = shapes } }] };
        var options = new BpmnTemplateOptions();
        options.Registry.Add(template);
        _ = document.Find("Task_0");
        shapes.Reads = 0;

        Assert.NotEmpty(new BpmnElementTemplateValidator(options).Validate(document).ToArray());

        Assert.Equal(shapes.Count, shapes.Reads);
        _ = new FullInspectionProfile(options).Validate(document).ToArray();
        Assert.True(shapes.Reads >= document.FlowNodes.Count * shapes.Count);
    }

    private static void AssertReportsMatch(BpmnDocument document, BpmnTemplateOptions options)
    {
        var expected = BpmnValidator.Validate(document, [new FullInspectionProfile(options)]);
        var actual = BpmnValidator.Validate(document, [new BpmnElementTemplateValidator(options)]);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    private static BpmnElementTemplate Template(bool scoped) => new()
    {
        Id = "performance",
        Name = "Performance",
        Version = 1,
        Flavor = BpmnTemplateFlavor.Camunda7,
        AppliesTo = ["bpmn:Task"],
        Properties =
        [
            new() { Id = "name", Binding = new("property", new Dictionary<string, string> { ["name"] = "name" }) },
            new() { Id = "required", Feel = BpmnTemplateFeel.Required, Constraints = new(NotEmpty: true) },
            new() { Id = "conditional", Condition = new PropertyCondition("name", BpmnTemplateConditionOperator.Equals, "Task"), Constraints = new(NotEmpty: true) },
            new() { Id = "hidden", Type = BpmnTemplatePropertyType.Hidden, Constraints = new(NotEmpty: true) },
        ],
        Scopes = scoped ? [new("camunda:Connector",
            [new() { Id = "input", Binding = new("camunda:inputParameter", new Dictionary<string, string> { ["name"] = "input" }), Constraints = new(NotEmpty: true) }])] : [],
    };

    private static BpmnDocument Document(BpmnElementTemplate template)
    {
        var builder = new BpmnDocumentBuilder();
        for (var index = 0; index < 12; index++)
            builder.Node($"Task_{index}", BpmnFlowNodeType.Task, new Bounds(index * 150, 0, 100, 80), "Task");
        var document = builder.Build();
        var extras = new BpmnElementExtras
        {
            ForeignAttributes = new Dictionary<string, string>
            {
                ["{" + BpmnTemplateMarkers.CamundaNamespaceUri + "}modelerTemplate"] = template.Id,
                ["{" + BpmnTemplateMarkers.CamundaNamespaceUri + "}modelerTemplateVersion"] = "1",
            },
        };
        return document with { FlowNodes = document.FlowNodes.Select(node => node with { Extras = extras }).ToArray() };
    }

    private sealed class CountingShapes(IReadOnlyList<BpmnShape> shapes) : IReadOnlyList<BpmnShape>
    {
        public int Reads { get; set; }
        public int Count => shapes.Count;
        public BpmnShape this[int index] { get { Reads++; return shapes[index]; } }
        public IEnumerator<BpmnShape> GetEnumerator()
        {
            foreach (var shape in shapes) { Reads++; yield return shape; }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // Original validation path is the oracle for complete diagnostic order and presentation metadata.
    private sealed class FullInspectionProfile(BpmnTemplateOptions options) : IBpmnProfileValidator
    {
        public string Name => "Element templates";

        public IEnumerable<BpmnProfileDiagnostic> Validate(BpmnDocument document)
        {
            foreach (var (id, extras) in BpmnValidator.Extended(document).DistinctBy(item => item.Id))
            {
                if (!BpmnTemplateMarkers.TryReadApplied(extras, out var templateId, out var version, out _)) continue;
                if (BpmnAppliedTemplate.Resolve(document, id, options.Registry).Template is not { } template)
                {
                    yield return FromText("PROF-103", BpmnSeverity.Info,
                        BpmnDiagnosticText.Create("PROF-103", "Template not found: '{0}' version {1}.", templateId, version), id);
                    continue;
                }
                if (template.Deprecated is { } deprecated)
                    yield return FromText("PROF-104", BpmnSeverity.Warning,
                        deprecated.Message is { } custom
                            ? BpmnDiagnosticText.Create("PROF-104.Detail.Custom", "{0}", custom)
                            : BpmnDiagnosticText.Create("PROF-104", "Template '{0}' is deprecated.", template.Name), id);
                var snapshot = BpmnElementInspector.Describe(document, id);
                if (snapshot is not null && BpmnTemplateSelection.Upgrade(snapshot, template, options) is { } newer)
                    yield return FromText("PROF-105", BpmnSeverity.Info,
                        BpmnDiagnosticText.Create("PROF-105", "Template '{0}' version {1} is available.", template.Name, newer.Version), id);
                var values = BpmnTemplateValues.Read(document, id, template);
                var active = BpmnTemplateValues.ActiveProperties(template, values);
                foreach (var (property, index) in BpmnTemplateScopes.Flatten(template).Properties.Select((property, index) => (property, index)))
                {
                    var key = BpmnTemplateValues.PropertyKey(property, index);
                    if (!active.Contains(key) || property.Type == BpmnTemplatePropertyType.Hidden) continue;
                    foreach (var error in BpmnTemplatePropertyValidation.LocalizedErrors(property, values[key]))
                        yield return FromText(property.Feel == BpmnTemplateFeel.Required && error.Message.Contains("FEEL", StringComparison.Ordinal)
                            ? "PROF-102" : "PROF-101", BpmnSeverity.Error, error, id);
                }
            }
        }

        private static BpmnProfileDiagnostic FromText(string ruleId, BpmnSeverity severity, BpmnDiagnosticText text, string id) =>
            new(ruleId, severity, text.Message, id) { MessageResourceKey = text.ResourceKey, MessageArguments = text.Arguments };
    }
}
