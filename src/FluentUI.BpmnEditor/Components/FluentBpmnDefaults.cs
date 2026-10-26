using Hasseware.AspNetCore.Components.Bpmn;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn;

/// <summary>
/// The concrete chrome and property entry components registered as defaults.
/// </summary>
public static class FluentBpmnDefaults
{
    /// <summary>The defaults <see cref="FluentBpmnEditor"/> renders when the host chose no other component, whatever kit the app registered.</summary>
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
            .UseDefault<FluentBpmnToolbar>(BpmnEditorPart.Toolbar)
            .UseDefault<FluentBpmnPropertyPanel>(BpmnEditorPart.PropertyPanel)
            .UseDefault<FluentBpmnStencilPanel>(BpmnEditorPart.Stencil)
            .UseDefault<FluentBpmnValidationPanel>(BpmnEditorPart.ValidationPanel)
            .UseDefault<FluentBpmnDiagramNavigator>(BpmnEditorPart.DiagramNavigator)
            .UseDefault<FluentBpmnContextMenu>(BpmnEditorPart.ContextMenu)
            .UseDefault<FluentBpmnContextualActions>(BpmnEditorPart.ContextualActions)
            .UseDefault<FluentBpmnImportOffer>(BpmnEditorPart.ImportOffer)
            .UseDefault<FluentBpmnExternalChangeOffer>(BpmnEditorPart.ExternalChangeOffer)
            .UseDefault<FluentBpmnAgentConfirmation>(BpmnEditorPart.AgentConfirmation)
            .UseDefault<FluentBpmnCalledElementPicker>(BpmnEditorPart.CalledElementPicker)
            .UseDefault<FluentBpmnMessageCatalog>(BpmnEditorPart.MessageCatalog)
            .UseDefault<FluentBpmnShortcutHelp>(BpmnEditorPart.ShortcutHelp)
            .UseDefault<BpmnLabelEditor>(BpmnEditorPart.LabelEditor)
            .UseDefault<BpmnAccessibleTree>(BpmnEditorPart.AccessibleTree);
    }

    /// <summary>Registers the property entry editors that render each kind.</summary>
    /// <param name="registry">The registry to register into.</param>
    /// <returns>The same registry.</returns>
    public static BpmnPropertyEditorRegistry Register(BpmnPropertyEditorRegistry registry)
    {
        return registry
            .UseDefaultEditor<FluentBpmnTextEntry>(BpmnPropertyEntryKind.Text)
            .UseDefaultEditor<FluentBpmnTextAreaEntry>(BpmnPropertyEntryKind.TextArea)
            .UseDefaultEditor<FluentBpmnCheckboxEntry>(BpmnPropertyEntryKind.Checkbox)
            .UseDefaultEditor<FluentBpmnNumberEntry>(BpmnPropertyEntryKind.Number)
            .UseDefaultEditor<FluentBpmnSelectEntry>(BpmnPropertyEntryKind.Select)
            .UseDefaultEditor<FluentBpmnMessageReferenceEntry>("bpmn:messageReference")
            .UseDefaultEditor<FluentBpmnListEntry>(BpmnPropertyEntryKind.List)
            .UseDefaultFeelEditor<FluentBpmnFeelEntry>();
    }
}
