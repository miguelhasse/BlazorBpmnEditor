namespace Hasseware.AspNetCore.Components.Bpmn.Configuration;

/// <summary>Application-wide property panel composition and editor overrides.</summary>
/// <remarks>
/// Configure through AddBpmnComponents. Editors resolve by explicit key, last matching predicate,
/// kind, then built-in default. A component registered here accepts a parameter named Context of
/// type BpmnPropertyEntryContext. Providers augment the built-ins unless the panel's Providers
/// parameter supplies an explicit replacement chain. EntryTemplate overrides registry dispatch;
/// GroupTemplate can reuse its context's EntryTemplate to retain write routing.
/// </remarks>
public sealed class BpmnPropertyPanelOptions
{
    /// <summary>The editor registry; registered components accept a Context parameter.</summary>
    public BpmnPropertyEditorRegistry Editors { get; } = new();

    /// <summary>Additional providers run alongside the built-ins in descending priority.</summary>
    public IList<IBpmnPropertiesProvider> Providers { get; } = [];

    /// <summary>Factories for host- or feature-specific entry bindings.</summary>
    public IList<IBpmnBindingCommandFactory> BindingCommandFactories { get; } = [];
}
