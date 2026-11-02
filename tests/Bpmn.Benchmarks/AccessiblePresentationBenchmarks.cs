using System.Reflection;
using BenchmarkDotNet.Attributes;
using Hasseware.AspNetCore.Components.Bpmn.Accessibility;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

/// <summary>Separates localized tree text from semantic graph and validation derivation.</summary>
[MemoryDiagnoser]
public class AccessiblePresentationBenchmarks
{
    private BpmnDocument _document = null!;
    private IReadOnlyList<BpmnGraphNode> _nodes = [];
    private BpmnDisplayLocalizer _display = null!;
    private ServiceProvider _services = null!;
    private IBpmnProfileValidator[] _profiles = [];
    private Func<BpmnDisplayLocalizer> _renderDisplay = null!;

    [Params(602, 2000, 5000)]
    public int TaskCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var builder = new BpmnDocumentBuilder();
        for (var index = 0; index < TaskCount; index++)
        {
            builder.Node($"Task_{index}", BpmnFlowNodeType.Task,
                new Bounds(index * 200, 0, 100, 80), $"Task {index}");
            if (index > 0)
                builder.Flow($"Flow_{index}", $"Task_{index - 1}", $"Task_{index}",
                    [new DiPoint(((index - 1) * 200) + 100, 40), new DiPoint(index * 200, 40)]);
        }
        _document = builder.Build();
        _nodes = BpmnAccessibleGraph.For(_document, null);
        _services = new ServiceCollection().AddBpmnComponents().BuildServiceProvider();
        _display = _services.GetRequiredService<BpmnDisplayLocalizer>();
        _renderDisplay = typeof(BpmnDisplayLocalizer).GetMethod("ForAccessibleRender", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.CreateDelegate<Func<BpmnDisplayLocalizer>>(_display) ?? (() => _display);
        _profiles = [new BpmnElementTemplateValidator(new BpmnTemplateOptions())];
    }

    [Benchmark]
    public int RenderNames()
    {
        var display = _renderDisplay();
        var length = 0;
        foreach (var node in _nodes) length += display.AccessibleName(node).Length;
        return length;
    }

    [Benchmark]
    public int LocalizedNames()
    {
        var length = 0;
        foreach (var node in _nodes) length += _display.AccessibleName(node).Length;
        return length;
    }

    [Benchmark]
    public int TypeNames()
    {
        var length = 0;
        foreach (var node in _nodes) length += _display.ElementType(node.TypeName).Length;
        return length;
    }

    [Benchmark]
    public IReadOnlyList<BpmnGraphNode> Graph() => BpmnAccessibleGraph.For(_document, null);

    [Benchmark]
    public BpmnValidationReport CoreValidation() => BpmnValidator.Validate(_document);

    [Benchmark]
    public BpmnValidationReport ProfileValidation() => BpmnValidator.Validate(_document, _profiles);

    [GlobalCleanup]
    public void Cleanup() => _services.Dispose();
}
