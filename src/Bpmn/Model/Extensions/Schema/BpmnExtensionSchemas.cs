namespace Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema;

/// <summary>Opt-in embedded vendor descriptors; loading never fetches external resources.</summary>
public static class BpmnExtensionSchemas
{
    /// <summary>Camunda 8 / Zeebe namespace URI.</summary>
    public const string ZeebeNamespace = "http://camunda.org/schema/zeebe/1.0";
    /// <summary>Camunda 7 namespace URI.</summary>
    public const string Camunda7Namespace = "http://camunda.org/schema/1.0/bpmn";
    private static readonly Lazy<BpmnExtensionSchema> ZeebeSchema = new(() => Load("zeebe"));
    private static readonly Lazy<BpmnExtensionSchema> CamundaSchema = new(() => Load("camunda"));
    /// <summary>The bundled Zeebe moddle descriptor.</summary>
    public static BpmnExtensionSchema Zeebe => ZeebeSchema.Value;
    /// <summary>The bundled Camunda 7 moddle descriptor.</summary>
    public static BpmnExtensionSchema Camunda7 => CamundaSchema.Value;
    private static BpmnExtensionSchema Load(string name)
    {
        using var stream = typeof(BpmnExtensionSchemas).Assembly.GetManifestResourceStream($"Hasseware.AspNetCore.Components.Bpmn.Extensions.{name}.json")
            ?? throw new BpmnExtensionSchemaException($"Embedded descriptor '{name}' is missing.");
        return BpmnExtensionSchemaLoader.Load(stream);
    }
}
