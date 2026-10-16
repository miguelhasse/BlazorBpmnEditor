using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// The concrete chrome and property entry components registered as defaults.
/// </summary>
public static class BpmnStandardDefaults
{
    /// <summary>The defaults <see cref="BpmnEditor"/> renders when the host chose no other component, whatever kit the app registered.</summary>
    public static BpmnKitDefaults Kit { get; } = CreateKit();

    private static BpmnKitDefaults CreateKit()
    {
        var kit = new BpmnKitDefaults();
        Register(kit.Components);
        Register(kit.Editors);
        return kit;
    }

    /// <summary>Registers the part and property entry defaults, never overriding what the host configured.</summary>
    /// <param name="options">The options to register into.</param>
    public static void Register(BpmnOptions options)
    {
        Register(options.Components);
        Register(options.Properties.Editors);
    }

    private static void Register(BpmnEditorComponentOptions components)
    {
        components
            .UseDefault<BpmnToolbar>(BpmnEditorPart.Toolbar)
            .UseDefault<BpmnPropertyPanel>(BpmnEditorPart.PropertyPanel)
            .UseDefault<BpmnStencilPanel>(BpmnEditorPart.Stencil)
            .UseDefault<BpmnValidationPanel>(BpmnEditorPart.ValidationPanel)
            .UseDefault<BpmnDiagramNavigator>(BpmnEditorPart.DiagramNavigator)
            .UseDefault<BpmnContextMenu>(BpmnEditorPart.ContextMenu)
            .UseDefault<BpmnContextualActions>(BpmnEditorPart.ContextualActions)
            .UseDefault<BpmnImportOffer>(BpmnEditorPart.ImportOffer)
            .UseDefault<BpmnExternalChangeOffer>(BpmnEditorPart.ExternalChangeOffer)
            .UseDefault<BpmnAgentConfirmation>(BpmnEditorPart.AgentConfirmation)
            .UseDefault<BpmnCalledElementPicker>(BpmnEditorPart.CalledElementPicker)
            .UseDefault<BpmnShortcutHelp>(BpmnEditorPart.ShortcutHelp);
    }

    /// <summary>Registers the property entry editors that render each kind.</summary>
    /// <param name="registry">The registry to register into.</param>
    /// <returns>The same registry.</returns>
    public static BpmnPropertyEditorRegistry Register(BpmnPropertyEditorRegistry registry)
    {
        return registry
            .UseDefaultEditor<BpmnTextEntry>(BpmnPropertyEntryKind.Text)
            .UseDefaultEditor<BpmnTextAreaEntry>(BpmnPropertyEntryKind.TextArea)
            .UseDefaultEditor<BpmnCheckboxEntry>(BpmnPropertyEntryKind.Checkbox)
            .UseDefaultEditor<BpmnNumberEntry>(BpmnPropertyEntryKind.Number)
            .UseDefaultEditor<BpmnSelectEntry>(BpmnPropertyEntryKind.Select)
            .UseDefaultEditor<BpmnListEntry>(BpmnPropertyEntryKind.List)
            .UseDefaultFeelEditor<BpmnFeelEntry>();
    }
}
