using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Native modal container with focus/selection restoration for the transactional workspace.</summary>
public partial class BpmnFeelSharpDialog
{
    private BpmnFeelSharpWorkspace? _workspace;
    [Inject] private IStringLocalizer<Resources.BpmnStrings> Localizer { get; set; } = default!;
    private string Text(string key) => Localizer[key];
    /// <summary>Whether the manager is open; closing retains the shell until focus is released.</summary>
    [Parameter] public bool Visible { get; set; } = true;
    /// <summary>Raised after the standard shell has closed.</summary>
    [Parameter] public EventCallback Closed { get; set; }
    /// <summary>Initial prefix-free expression.</summary>
    [Parameter] public string? Value { get; set; }
    /// <summary>Apply callback.</summary>
    [Parameter] public EventCallback<string?> Applied { get; set; }
    /// <summary>Cancel callback.</summary>
    [Parameter] public EventCallback Cancelled { get; set; }
    /// <summary>Context identity.</summary>
    [Parameter] public string? ContextKey { get; set; }
    /// <summary>Host reset revision.</summary>
    [Parameter] public int Revision { get; set; }
    /// <summary>Accessible title.</summary>
    [Parameter] public string Label { get; set; } = "FEEL";
    /// <summary>Prevent changes.</summary>
    [Parameter] public bool ReadOnly { get; set; }
    /// <summary>Host hints.</summary>
    [Parameter] public IReadOnlyList<BpmnFeelVariable> Variables { get; set; } = [];
    /// <summary>Workspace field renderer.</summary>
    [Parameter] public Type EditorType { get; set; } = typeof(BpmnFeelSharpCodeEditor);
    /// <summary>Explicit evaluator; no implicit DI authorization.</summary>
    [Parameter] public IBpmnFeelSharpEvaluation? Evaluator { get; set; }
    /// <summary>Explicit host actions.</summary>
    [Parameter] public BpmnFeelSharpHostActions? Actions { get; set; }

    private Task ApplyAsync() => _workspace?.ApplyAsync() ?? Task.CompletedTask;
    private Task CancelAsync() => Cancelled.InvokeAsync();

}
