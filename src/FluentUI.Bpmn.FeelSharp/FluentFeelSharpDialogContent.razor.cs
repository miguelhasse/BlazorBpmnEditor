using Hasseware.AspNetCore.Components.Bpmn;
using Hasseware.AspNetCore.Components.Bpmn.FeelSharp;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn.FeelSharp;

internal sealed record FluentFeelSharpApplied(string? Value);

/// <summary>Fluent v5 service-dialog content; requires a FluentDialogProvider in the host layout.</summary>
public partial class FluentFeelSharpDialogContent
{
    [CascadingParameter] private IDialogInstance Dialog { get; set; } = default!;
    /// <summary>Initial prefix-free expression.</summary>
    [Parameter] public string? Value { get; set; }
    /// <summary>Accessible label.</summary>
    [Parameter] public string Label { get; set; } = "FEEL";
    /// <summary>Host variable declarations.</summary>
    [Parameter] public IReadOnlyList<BpmnFeelVariable> Variables { get; set; } = [];
    /// <summary>Explicitly authorized evaluator.</summary>
    [Parameter] public IBpmnFeelSharpEvaluation? Evaluator { get; set; }
    /// <summary>Explicit host actions.</summary>
    [Parameter] public BpmnFeelSharpHostActions? Actions { get; set; }

    private Task ApplyAsync(string? value) => Dialog.CloseAsync(new FluentFeelSharpApplied(value));
    private Task CancelAsync() => Dialog.CancelAsync();
}
