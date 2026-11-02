using BenchmarkDotNet.Attributes;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

/// <summary>Measures real applied-template validation, including scoped properties and upgrade selection.</summary>
[MemoryDiagnoser]
public class TemplateValidationBenchmarks
{
    private BpmnDocument _document = null!;
    private BpmnElementTemplateValidator _profile = null!;
    private IBpmnProfileValidator[] _profiles = [];

    [Params(602, 2000, 5000)]
    public int TaskCount { get; set; }

    [Params("None", "Plain", "Scoped")]
    public string Templates { get; set; } = "None";

    [GlobalSetup]
    public void Setup()
    {
        var template = new BpmnElementTemplate
        {
            Id = "benchmark",
            Name = "Benchmark",
            Version = 1,
            Flavor = BpmnTemplateFlavor.Camunda7,
            AppliesTo = ["bpmn:Task"],
            Properties =
            [
                new() { Id = "name", Binding = new("property", new Dictionary<string, string> { ["name"] = "name" }) },
                new() { Id = "required", Constraints = new(NotEmpty: true), Feel = BpmnTemplateFeel.Required },
                new() { Id = "conditional", Condition = new PropertyCondition("name", BpmnTemplateConditionOperator.Equals, "Task"), Constraints = new(NotEmpty: true) },
            ],
            Scopes = Templates == "Scoped"
                ? [new("camunda:Connector",
                    [new() { Id = "input", Binding = new("camunda:inputParameter", new Dictionary<string, string> { ["name"] = "input" }), Constraints = new(NotEmpty: true) }])]
                : [],
        };
        var options = new BpmnTemplateOptions();
        options.Registry.Add(template);
        options.Registry.Add(template with { Version = 2 });
        var extras = new BpmnElementExtras
        {
            ForeignAttributes = new Dictionary<string, string>
            {
                ["{" + BpmnTemplateMarkers.CamundaNamespaceUri + "}modelerTemplate"] = template.Id,
                ["{" + BpmnTemplateMarkers.CamundaNamespaceUri + "}modelerTemplateVersion"] = "1",
            },
        };
        var builder = new BpmnDocumentBuilder();
        for (var index = 0; index < TaskCount; index++)
        {
            builder.Node($"Task_{index}", BpmnFlowNodeType.Task, new Bounds(index * 200, 0, 100, 80), "Task");
            if (index > 0)
                builder.Flow($"Flow_{index}", $"Task_{index - 1}", $"Task_{index}",
                    [new DiPoint(((index - 1) * 200) + 100, 40), new DiPoint(index * 200, 40)]);
        }
        _document = builder.Build();
        if (Templates != "None")
            _document = _document with { FlowNodes = _document.FlowNodes.Select(node => node with { Extras = extras }).ToArray() };
        _profile = new(options);
        _profiles = [_profile];
        _ = _document.Find("Task_0");
    }

    [Benchmark]
    public BpmnProfileDiagnostic[] ProfileOnly() => _profile.Validate(_document).ToArray();

    [Benchmark]
    public BpmnValidationReport Combined() => BpmnValidator.Validate(_document, _profiles);

    [Benchmark]
    public BpmnValidationReport CoreOnly() => BpmnValidator.Validate(_document);

    [Benchmark]
    public int InspectTargets()
    {
        var count = 0;
        foreach (var node in _document.FlowNodes)
            if (BpmnElementInspector.Describe(_document, node.Id) is not null) count++;
        return count;
    }
}
