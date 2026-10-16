namespace Hasseware.AspNetCore.Components.Bpmn.Configuration;

/// <summary>
/// Application-wide editor defaults, registered through
/// <see cref="BpmnServiceCollectionExtensions.AddBpmnComponents"/>.
/// </summary>
/// <remarks>
/// Resolution order is library defaults, then these options, then component parameters. That
/// gives an application one place to set house style without repeating it on every editor
/// instance, while still allowing a single instance to differ.
/// </remarks>
public sealed class BpmnOptions
{
    /// <summary>Creates default options including the template property binding factory.</summary>
    public BpmnOptions()
    {
        Properties.BindingCommandFactories.Add(new Editing.BpmnTemplateBindingCommandFactory(Templates.Registry));
        BpmnCoreDefaults.Register(this);
    }

    /// <summary>The components that render the editor's parts, app-wide.</summary>
    public BpmnEditorComponentOptions Components { get; } = new();

    /// <summary>Property panel providers and editor overrides.</summary>
    public BpmnPropertyPanelOptions Properties { get; } = new();

    /// <summary>Canvas surface options applied to every editor unless overridden.</summary>
    public CanvasOptions Canvas { get; set; } = CanvasOptions.Default;

    /// <summary>Grid options applied to every editor unless overridden.</summary>
    public GridOptions Grid { get; set; } = GridOptions.Default;

    /// <summary>Viewport options applied to every editor unless overridden.</summary>
    public ViewportOptions Viewport { get; set; } = ViewportOptions.Default;

    /// <summary>Keyboard shortcuts applied to every editor unless overridden.</summary>
    public KeyboardOptions Keyboard { get; set; } = KeyboardOptions.Default;

    /// <summary>Alignment guide options applied to every editor unless overridden.</summary>
    public SnappingOptions Snapping { get; set; } = SnappingOptions.Default;

    /// <summary>Editing options applied to every editor unless overridden.</summary>
    public EditingOptions Editing { get; set; } = EditingOptions.Default;

    /// <summary>Routing options applied to every editor unless overridden.</summary>
    public RoutingOptions Routing { get; set; } = RoutingOptions.Default;

    /// <summary>Execution overlay options applied to every editor unless overridden.</summary>
    public ExecutionOptions Execution { get; set; } = ExecutionOptions.Default;

    /// <summary>Document storage behaviour, such as the response to external changes.</summary>
    public StorageOptions Storage { get; set; } = StorageOptions.Default;

    /// <summary>Element-template registry and loading options.</summary>
    public BpmnTemplateOptions Templates { get; } = new();
    /// <summary>Opt-in extension schemas used by extension editors.</summary>
    public BpmnExtensionOptions Extensions { get; } = new();
}



