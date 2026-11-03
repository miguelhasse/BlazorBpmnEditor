using Hasseware.AspNetCore.Components.Bpmn;
using Hasseware.AspNetCore.Components.Bpmn.FeelSharp;
using Microsoft.AspNetCore.Components;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Standalone Fluent rendering of the shared managed FEEL field.</summary>
public partial class FluentBpmnFeelSharpField
{
    private BpmnFeelSharpField? _field;
    /// <summary>Expression text, prefix-free by default.</summary>
    [Parameter] public string? Value { get; set; }
    /// <summary>Committed value callback.</summary>
    [Parameter] public EventCallback<string?> ValueChanged { get; set; }
    /// <summary>Finalized draft callback.</summary>
    [Parameter] public EventCallback<BpmnFeelSharpDraft> DraftChanged { get; set; }
    /// <summary>Advisory diagnostic callback.</summary>
    [Parameter] public EventCallback<BpmnFeelSharpDiagnosticsUpdate> DiagnosticsChanged { get; set; }
    /// <summary>Host variable hints.</summary>
    [Parameter] public IReadOnlyList<BpmnFeelVariable> Variables { get; set; } = [];
    /// <summary>Accessible field label.</summary>
    [Parameter] public string Label { get; set; } = "FEEL";
    /// <summary>Disable editing.</summary>
    [Parameter] public bool ReadOnly { get; set; }
    /// <summary>Use one BPMN persistence prefix rather than prefix-free expression values.</summary>
    [Parameter] public bool PersistencePrefix { get; set; }
    /// <summary>Explicit host reset revision.</summary>
    [Parameter] public int Revision { get; set; }
    /// <summary>Textarea rows.</summary>
    [Parameter] public int Rows { get; set; } = 6;
    /// <summary>Read current native draft.</summary>
    public Task<BpmnFeelSharpDraft> CaptureAsync() => (_field ??
        throw new InvalidOperationException("The Fluent field has not rendered.")).CaptureAsync();
}
