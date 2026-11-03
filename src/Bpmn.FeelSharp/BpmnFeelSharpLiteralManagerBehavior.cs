namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Manager availability for optional properties currently holding literal text.</summary>
public enum BpmnFeelSharpLiteralManagerBehavior
{
    /// <summary>Require the property to be in FEEL mode before opening the manager.</summary>
    RequireFeelMode,
    /// <summary>Allow authoring a draft; convert the property to FEEL only on Apply.</summary>
    AllowConversionOnApply,
}
