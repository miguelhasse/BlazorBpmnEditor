using Hasseware.AspNetCore.Components.Bpmn;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Microsoft.AspNetCore.Components;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn;

/// <summary>The Fluent UI rendering of the property entry view property editor.</summary>
public partial class FluentBpmnPropertyEntryView
{
    /// <summary>The property metadata and write callbacks.</summary>
    [Parameter, EditorRequired] public BpmnPropertyEntryContext Context { get; set; } = default!;

    /// <summary>The editor overrides; the enclosing <see cref="BpmnPropertyScope"/>'s, or the built-in editors, when omitted.</summary>
    [Parameter] public BpmnPropertyEditorRegistry? Registry { get; set; }

    [CascadingParameter]
    private BpmnPropertyScope? Scope { get; set; }

    private BpmnPropertyEditorRegistry EffectiveRegistry => Registry ?? Scope?.Editors ?? Options.Value.Properties.Editors;
    private readonly Dictionary<string, object> _editorParameters = [];
    private IDictionary<string, object> EditorParameters => _editorParameters;

    /// <inheritdoc />

    protected override void OnParametersSet() => _editorParameters["Context"] = Context;
}
