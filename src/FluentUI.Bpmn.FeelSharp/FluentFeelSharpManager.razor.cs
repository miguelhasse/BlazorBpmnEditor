using Hasseware.AspNetCore.Components.Bpmn.FeelSharp;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>FEEL authoring hosted by the solution's standard Fluent dialog shell.</summary>
public partial class FluentFeelSharpManager
{
    private BpmnFeelSharpWorkspace? _workspace;
    /// <summary>Whether the manager is open; close before releasing the standard shell.</summary>
    [Parameter] public bool Visible { get; set; } = true;
    /// <summary>Raised after the modal's focus trap is released.</summary>
    [Parameter] public EventCallback Closed { get; set; }
    [Inject] private IStringLocalizer<Hasseware.AspNetCore.Components.Bpmn.Resources.BpmnStrings> Localizer { get; set; } = default!;
    /// <summary>Prefix-free draft source.</summary>
    [Parameter] public string? Value { get; set; }
    /// <summary>Guarded apply callback.</summary>
    [Parameter] public EventCallback<string?> Applied { get; set; }
    /// <summary>Non-persisting dismissal callback.</summary>
    [Parameter] public EventCallback Cancelled { get; set; }
    /// <summary>Source identity.</summary>
    [Parameter] public string? ContextKey { get; set; }
    /// <summary>Source revision.</summary>
    [Parameter] public int Revision { get; set; }
    /// <summary>Accessible property caption.</summary>
    [Parameter] public string Label { get; set; } = "FEEL";
    /// <summary>Prevent changes.</summary>
    [Parameter] public bool ReadOnly { get; set; }
    /// <summary>Host variable hints.</summary>
    [Parameter] public IReadOnlyList<Hasseware.AspNetCore.Components.Bpmn.BpmnFeelVariable> Variables { get; set; } = [];
    /// <summary>Explicit evaluator authorization.</summary>
    [Parameter] public IBpmnFeelSharpEvaluation? Evaluator { get; set; }
    /// <summary>Optional explicit host actions.</summary>
    [Parameter] public BpmnFeelSharpHostActions? Actions { get; set; }
    private string Text(string key) => Localizer[key];
    private Task ApplyAsync() => _workspace?.ApplyAsync() ?? Task.CompletedTask;
    private Task CancelAsync() => Cancelled.InvokeAsync();
}
