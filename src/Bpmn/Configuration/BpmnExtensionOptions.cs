using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema;

namespace Hasseware.AspNetCore.Components.Bpmn.Configuration;

/// <summary>Opt-in extension schemas. No vendor schema is registered by default.</summary>
public sealed class BpmnExtensionOptions
{
    /// <summary>Offer all registered schemas even in documents without their namespace. Defaults to true for compatibility; set false in mixed-engine hosts to offer only declared or used namespaces.</summary>
    public bool AlwaysOfferRegisteredSchemas { get; set; } = true;
    /// <summary>Namespace registry shared by schema-driven editors and binding handlers.</summary>
    public BpmnExtensionRegistry Registry { get; } = new();
    /// <summary>Snapshot builders for registered schemas.</summary>
    public IList<IBpmnExtensionGroupBuilder> GroupBuilders { get; } = [new BpmnSchemaPropertiesProvider()];
    /// <summary>Labels for schema-provided property groups and fields.</summary>
    public IBpmnPropertyLabelProvider LabelProvider { get; set; } = new BpmnEnglishPropertyLabelProvider();
    /// <summary>Registers a custom moddle descriptor.</summary>
    public BpmnExtensionOptions AddSchema(BpmnExtensionSchema schema) { Registry.Register(schema); return this; }
    /// <summary>Registers the embedded Camunda 8 / Zeebe descriptor.</summary>
    public BpmnExtensionOptions AddZeebe() => AddSchema(BpmnExtensionSchemas.Zeebe);
    /// <summary>Registers the embedded Camunda 7 descriptor.</summary>
    public BpmnExtensionOptions AddCamunda7() => AddSchema(BpmnExtensionSchemas.Camunda7);
}
