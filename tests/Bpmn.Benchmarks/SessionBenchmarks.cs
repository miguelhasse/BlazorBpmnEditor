using System.Reflection;
using BenchmarkDotNet.Attributes;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

/// <summary>Headless state-publication costs on the 602-shape stress fixture.</summary>
[MemoryDiagnoser]
public class SessionBenchmarks
{
    private ServiceProvider _services = null!;
    private BpmnDocument _document = null!;
    private BpmnEditorSession _session = null!;
    private EditorConfig _configuration = null!;
    private Action _refresh = null!;
    private string[] _elements = [];
    private IReadOnlyList<string>[] _selections = [];
    private int _selection;
    private double _zoom = 1;

    [GlobalSetup]
    public void Setup()
    {
        _document = BpmnXmlReader.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "large-virtualization-stress.bpmn"))).Document;
        _services = new ServiceCollection().AddBpmnComponents().BuildServiceProvider();
        _configuration = EditorConfig.CreateDefault();
        _elements = [.. _document.FlowNodes.Take(2).Select(node => node.Id)];
        _selections = [new[] { _elements[0] }, new[] { _elements[1] }];
        ResetSession();
    }

    // A fresh history keeps the single edit independent of earlier invocations. BenchmarkDotNet
    // uses one invocation per iteration for this target; session/fixture setup is not timed.
    [IterationSetup(Target = nameof(SingleEdit))]
    public void ResetSession()
    {
        _session = new BpmnEditorSession(new BpmnOptions(), _services)
        {
            Document = _document,
            DiagramId = _document.Diagrams[0].Id,
            ShowAccessibleTree = true,
        };
        _session.UpdateParameters(_configuration);
        _session.SetSelectionAsync(_selections[0]).GetAwaiter().GetResult();
        _selection = 0;
        _refresh = typeof(BpmnEditorSession).GetMethod("RefreshState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action>(_session);
    }

    [Benchmark]
    public Task ViewportChanged()
    {
        _zoom = _zoom == 1 ? 1.1 : 1;
        return _session.HandleViewportChangedAsync(new ViewportChanged(_zoom, 0, 0));
    }

    [Benchmark]
    public Task SelectionChanged()
    {
        _selection = 1 - _selection;
        return _session.SetSelectionAsync(_selections[_selection]);
    }

    [Benchmark]
    public void UnchangedParameters() => _session.UpdateParameters(_configuration);

    [Benchmark]
    public Task<BpmnEditResult> SingleEdit() =>
        _session.ApplyAsync(new UpdatePropertiesCommand(_elements[0], "name", "Baseline edit"));

    [Benchmark]
    public void RefreshState() => _refresh();

    [GlobalCleanup]
    public void Cleanup() => _services.Dispose();
}
