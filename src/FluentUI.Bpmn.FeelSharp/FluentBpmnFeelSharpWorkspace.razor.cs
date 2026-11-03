using Hasseware.AspNetCore.Components.Bpmn;
using Hasseware.AspNetCore.Components.Bpmn.FeelSharp;
using Microsoft.AspNetCore.Components;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Fluent field presentation of the shared transactional workspace.</summary>
public partial class FluentBpmnFeelSharpWorkspace
{
    /// <summary>Initial prefix-free expression.</summary>
    [Parameter] public string? Value { get; set; }
    /// <summary>Explicit Apply callback.</summary>
    [Parameter] public EventCallback<string?> Applied { get; set; }
    /// <summary>Cancel callback.</summary>
    [Parameter] public EventCallback Cancelled { get; set; }
    /// <summary>Element/property identity.</summary>
    [Parameter] public string? ContextKey { get; set; }
    /// <summary>Host reset revision.</summary>
    [Parameter] public int Revision { get; set; }
    /// <summary>Accessible label.</summary>
    [Parameter] public string Label { get; set; } = "FEEL";
    /// <summary>Prevent edits and Apply.</summary>
    [Parameter] public bool ReadOnly { get; set; }
    /// <summary>Host variable declarations.</summary>
    [Parameter] public IReadOnlyList<BpmnFeelVariable> Variables { get; set; } = [];
    /// <summary>Explicitly authorized evaluator.</summary>
    [Parameter] public IBpmnFeelSharpEvaluation? Evaluator { get; set; }
    /// <summary>Explicit host actions.</summary>
    [Parameter] public BpmnFeelSharpHostActions? Actions { get; set; }
}
