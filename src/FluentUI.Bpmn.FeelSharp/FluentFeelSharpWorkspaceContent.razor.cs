using Hasseware.AspNetCore.Components.Bpmn.FeelSharp;
using Microsoft.AspNetCore.Components;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Fluent presentation of the shared manager workspace state and commands.</summary>
public partial class FluentFeelSharpWorkspaceContent
{
    /// <summary>Owning transactional workspace.</summary>
    [Parameter, EditorRequired] public BpmnFeelSharpWorkspace Workspace { get; set; } = default!;
}
