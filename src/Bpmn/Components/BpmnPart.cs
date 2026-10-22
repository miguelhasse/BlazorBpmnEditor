using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>Renders the component configured for an editor part, or nothing when there is none.</summary>
/// <remarks>
/// Resolution: <see cref="ChildContent"/> (a per-instance override) first, then the host's
/// <see cref="BpmnOptions.Components"/>, then the enclosing composition's kit defaults, then the app-wide defaults.
/// The resolved component reads editor state from the cascade. Toolbar visibility is forwarded explicitly.
/// </remarks>
public sealed class BpmnPart : ComponentBase
{
    private Type? _componentType;
    private readonly Dictionary<string, object> _toolbarParameters = new()
    {
        [nameof(BpmnToolbarBase.ShowColorPicker)] = false,
    };
    private (BpmnEditorPart Part, BpmnKitDefaults? Kit, BpmnEditorComponentOptions? Options,
        int Revision, int KitRevision) _resolution;

    [CascadingParameter]
    private BpmnEditorSession? Session { get; set; }

    [Inject]
    private IOptions<BpmnOptions> Options { get; set; } = default!;

    /// <summary>The part to render.</summary>
    [Parameter, EditorRequired]
    public BpmnEditorPart Part { get; set; }

    /// <summary>Content that replaces the configured component for this instance.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>Whether the resolved toolbar shows color controls. Ignored for other parts and custom content.</summary>
    [Parameter]
    public bool ShowColorPicker { get; set; }

    /// <summary>Content rendered when neither <see cref="ChildContent"/> nor a component is available.</summary>
    [Parameter]
    public RenderFragment? Fallback { get; set; }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (Session is null) throw new InvalidOperationException("BpmnPart must be rendered inside a BpmnEditorScope (or a BpmnEditor).");
        _toolbarParameters[nameof(BpmnToolbarBase.ShowColorPicker)] = ShowColorPicker;
        var options = Options.Value.Components;
        var key = (Part, Session.KitDefaults, options, options.Revision, Session.KitDefaults?.Components.Revision ?? 0);
        if (_resolution == key) return;
        _resolution = key;
        _componentType = options.Get(Part, Session.KitDefaults);
    }

    /// <inheritdoc />
    protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
    {
        if (ChildContent is not null)
        {
            builder.AddContent(0, ChildContent);
        }
        else if (_componentType is { } type)
        {
            builder.OpenComponent<DynamicComponent>(1);
            builder.AddComponentParameter(2, nameof(DynamicComponent.Type), type);
            if (Part == BpmnEditorPart.Toolbar)
                builder.AddComponentParameter(3, nameof(DynamicComponent.Parameters), _toolbarParameters);
            builder.CloseComponent();
        }
        else if (Fallback is not null)
        {
            builder.AddContent(4, Fallback);
        }
    }
}
