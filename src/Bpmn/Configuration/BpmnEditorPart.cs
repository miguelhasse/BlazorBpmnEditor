namespace Hasseware.AspNetCore.Components.Bpmn.Configuration;

/// <summary>A replaceable part of the editor, resolved to a component through <see cref="BpmnEditorComponentOptions"/>.</summary>
/// <remarks>
/// Each part has a required base class (see <see cref="BpmnEditorComponentOptions.Use{TComponent}"/>);
/// the component state arrives by cascade, so a part is always rendered without parameters.
/// </remarks>
public enum BpmnEditorPart
{
    /// <summary>The command toolbar; derives from <see cref="BpmnToolbarBase"/>.</summary>
    Toolbar,

    /// <summary>The shape palette; derives from <see cref="BpmnStencilPanelBase"/>.</summary>
    Stencil,

    /// <summary>The properties side panel; derives from <see cref="BpmnPropertyPanelBase"/>.</summary>
    PropertyPanel,

    /// <summary>The validation results list; derives from <see cref="BpmnValidationPanelBase"/>.</summary>
    ValidationPanel,

    /// <summary>The diagram (sub-process) navigator; derives from <see cref="BpmnDiagramNavigatorBase"/>.</summary>
    DiagramNavigator,

    /// <summary>The canvas context menu; derives from <see cref="BpmnContextMenuBase"/>.</summary>
    ContextMenu,

    /// <summary>The contextual actions beside the selection; derives from <see cref="BpmnContextualActionsBase"/>.</summary>
    ContextualActions,

    /// <summary>The in-place label editor; derives from <see cref="BpmnLabelEditorBase"/>.</summary>
    LabelEditor,

    /// <summary>The keyboard-navigable diagram tree; derives from <see cref="BpmnAccessibleTreeBase"/>.</summary>
    AccessibleTree,

    /// <summary>The offer to import a dropped file; derives from <see cref="BpmnImportOfferBase"/>.</summary>
    ImportOffer,

    /// <summary>The called-element picker dialog; derives from <see cref="BpmnCalledElementPickerBase"/>.</summary>
    CalledElementPicker,

    /// <summary>The keyboard shortcut help dialog; derives from <see cref="BpmnShortcutHelpBase"/>.</summary>
    ShortcutHelp,
}
