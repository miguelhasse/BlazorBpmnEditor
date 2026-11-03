using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Attaches FEEL assistance to the registered property renderer's own text control.</summary>
public partial class BpmnFeelSharpPropertyInput
{
    /// <summary>Owns the property draft, diagnostics and native attachment.</summary>
    [Parameter, EditorRequired] public BpmnFeelSharpEditorBase Editor { get; set; } = default!;
}
