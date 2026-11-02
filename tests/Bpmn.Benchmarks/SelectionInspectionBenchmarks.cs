using System.Reflection;
using BenchmarkDotNet.Attributes;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

/// <summary>Separates cold selection inspection from warm session reads and invariant checking.</summary>
[MemoryDiagnoser]
public class SelectionInspectionBenchmarks
{
    private BpmnDocument _document = null!;
    private BpmnEditorSession _cold = null!;
    private BpmnEditorSession _warm = null!;
    private ServiceProvider _services = null!;
    private System.Collections.IDictionary _descriptions = null!;

    [Params(602, 2000, 5000)]
    public int TaskCount { get; set; }

    [Params(1, 20, 100)]
    public int SelectedCount { get; set; }

    [Params(false, true)]
    public bool Templated { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var builder = new BpmnDocumentBuilder();
        for (var index = 0; index < TaskCount; index++)
        {
            builder.Node($"Task_{index}", BpmnFlowNodeType.Task,
                new Bounds(index * 200, 0, 100, 80), "Task");
            if (index > 0)
                builder.Flow($"Flow_{index}", $"Task_{index - 1}", $"Task_{index}",
                    [new DiPoint(((index - 1) * 200) + 100, 40), new DiPoint(index * 200, 40)]);
        }
        _document = builder.Build();
        var options = new BpmnOptions();
        if (Templated)
        {
            options.Templates.Registry.Add(new BpmnElementTemplate
            {
                Id = "inspection",
                Name = "Inspection",
                Version = 1,
                Flavor = BpmnTemplateFlavor.Camunda7,
                AppliesTo = ["bpmn:Task"],
                Scopes = [new("camunda:Connector",
                    [new() { Id = "input", Binding = new("camunda:inputParameter",
                        new Dictionary<string, string> { ["name"] = "input" }), Constraints = new(NotEmpty: true) }])],
            });
            var extras = new BpmnElementExtras
            {
                ForeignAttributes = new Dictionary<string, string>
                {
                    ["{" + BpmnTemplateMarkers.CamundaNamespaceUri + "}modelerTemplate"] = "inspection",
                    ["{" + BpmnTemplateMarkers.CamundaNamespaceUri + "}modelerTemplateVersion"] = "1",
                },
            };
            _document = _document with { FlowNodes = _document.FlowNodes.Select(node => node with { Extras = extras }).ToArray() };
        }
        _services = new ServiceCollection().AddBpmnComponents().BuildServiceProvider();
        _cold = Session(options);
        _warm = Session(options);
        _ = _warm.SelectionSnapshots;
        _descriptions = (System.Collections.IDictionary)typeof(BpmnEditorSession)
            .GetField("_descriptions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_cold)!;
    }

    [Benchmark]
    public IReadOnlyList<BpmnElementSnapshot> ColdSnapshots()
    {
        // Clear only the existing description cache: graph, validation and fixture setup are not measured here.
        _descriptions.Clear();
        return _cold.SelectionSnapshots;
    }

    [Benchmark]
    public IReadOnlyList<BpmnElementSnapshot> WarmSnapshots() => _warm.SelectionSnapshots;

    [Benchmark]
    public IReadOnlyList<BpmnInvariantViolation> Invariants() => BpmnDocumentInvariants.Check(_document);

    private BpmnEditorSession Session(BpmnOptions options)
    {
        var session = new BpmnEditorSession(options, _services) { Document = _document };
        session.UpdateParameters(EditorConfig.CreateDefault());
        session.SetSelectionAsync(_document.FlowNodes.Take(SelectedCount).Select(node => node.Id).ToArray()).GetAwaiter().GetResult();
        return session;
    }

    [GlobalCleanup]
    public void Cleanup() => _services.Dispose();
}
