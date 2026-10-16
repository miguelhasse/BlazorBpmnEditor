namespace Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;

/// <summary>Template product family inferred from its schema and bindings.</summary>
public enum BpmnTemplateFlavor
{
    /// <summary>Camunda 8 template flavor.</summary>
    Zeebe,
    /// <summary>Camunda 7 template flavor.</summary>
    Camunda7
}

/// <summary>Supported property editor kinds in Camunda element templates.</summary>
#pragma warning disable CA1720
public enum BpmnTemplatePropertyType
{
    /// <summary>Single-line text input.</summary>
    String,
    /// <summary>Multiline text input.</summary>
    Text,
    /// <summary>Numeric input.</summary>
    Number,
    /// <summary>Boolean checkbox.</summary>
    Boolean,
    /// <summary>Choice list.</summary>
    Dropdown,
    /// <summary>Unrendered value binding.</summary>
    Hidden
}
#pragma warning restore CA1720

/// <summary>FEEL mode requested by a template property.</summary>
public enum BpmnTemplateFeel
{
    /// <summary>No FEEL editor mode configured.</summary>
    None,
    /// <summary>FEEL can be toggled.</summary>
    Optional,
    /// <summary>A FEEL expression is required.</summary>
    Required,
    /// <summary>Only static FEEL value is allowed.</summary>
    Static
}

/// <summary>Template category metadata.</summary>
public sealed record BpmnTemplateCategory(string Id, string Name);
/// <summary>Template element type override metadata.</summary>
public sealed record BpmnTemplateElementType(string Value, string? EventDefinition = null);
/// <summary>Template property group metadata.</summary>
public sealed record BpmnTemplateGroup(string Id, string Label, string? Tooltip = null, bool? OpenByDefault = null);
/// <summary>One dropdown choice.</summary>
public sealed record BpmnTemplateChoice(string Name, string Value);
/// <summary>Generated-value metadata.</summary>
public sealed record BpmnTemplateGeneratedValue(string Type);
/// <summary>Pattern constraint metadata.</summary>
public sealed record BpmnTemplatePattern(string Value, string? Message = null);
/// <summary>Constraints applied to a template property.</summary>
public sealed record BpmnTemplateConstraints(bool? NotEmpty = null, int? MinLength = null, int? MaxLength = null, BpmnTemplatePattern? Pattern = null);
/// <summary>Deprecation metadata.</summary>
public sealed record BpmnTemplateDeprecated(string? Message = null, string? DocumentationRef = null);
/// <summary>Visibility configuration for standard property-panel entries.</summary>
public sealed record BpmnTemplateEntriesVisible(bool? Default = null, IReadOnlyDictionary<string, bool>? Entries = null);

/// <summary>Generic binding with vendor-specific parameters retained as strings.</summary>
public sealed record BpmnTemplateBinding(string Type, IReadOnlyDictionary<string, string> Parameters)
{
    /// <summary>Gets a binding parameter, or null when absent.</summary>
    public string? Get(string name) => Parameters.TryGetValue(name, out var value) ? value : null;
    /// <summary>Gets conventional <c>name</c> binding parameter.</summary>
    public string? Name => Get("name");
    /// <summary>Gets conventional <c>property</c> binding parameter.</summary>
    public string? Property => Get("property");
    /// <summary>Gets conventional <c>key</c> binding parameter.</summary>
    public string? Key => Get("key");
    /// <summary>Returns known binding types for a template flavor.</summary>
    public static IReadOnlySet<string> KnownTypes(BpmnTemplateFlavor flavor) => flavor == BpmnTemplateFlavor.Zeebe ? KnownZeebeTypes : KnownCamunda7Types;

    private static readonly IReadOnlySet<string> KnownZeebeTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "property", "zeebe:input", "zeebe:output", "zeebe:taskHeader", "zeebe:taskDefinition", "zeebe:taskDefinition:type", "zeebe:property",
        "bpmn:Message#property", "bpmn:Message#zeebe:subscription#property", "bpmn:Signal#property", "bpmn:TimerEventDefinition#property",
        "bpmn:ConditionalEventDefinition#property", "bpmn:ConditionalEventDefinition#zeebe:conditionalFilter#property", "zeebe:calledElement",
        "zeebe:userTask", "zeebe:formDefinition", "zeebe:assignmentDefinition", "zeebe:taskSchedule", "zeebe:priorityDefinition",
        "zeebe:jobPriorityDefinition", "zeebe:calledDecision", "zeebe:script", "zeebe:adHoc", "zeebe:executionListener", "zeebe:taskListener",
        "zeebe:linkedResource", "zeebe:agentDefinition"
    };
    private static readonly IReadOnlySet<string> KnownCamunda7Types = new HashSet<string>(StringComparer.Ordinal)
    {
        "property", "camunda:property", "camunda:inputParameter", "camunda:outputParameter", "camunda:in", "camunda:in:businessKey",
        "camunda:out", "camunda:executionListener", "camunda:field", "camunda:errorEventDefinition"
    };
}

/// <summary>A template property definition.</summary>
public sealed record BpmnTemplateProperty
{
    /// <summary>Stable identifier used by conditions.</summary>
    public string? Id { get; init; }
    /// <summary>Visible label.</summary>
    public string? Label { get; init; }
    /// <summary>Property description.</summary>
    public string? Description { get; init; }
    /// <summary>Tooltip text.</summary>
    public string? Tooltip { get; init; }
    /// <summary>Input type.</summary>
    public BpmnTemplatePropertyType Type { get; init; } = BpmnTemplatePropertyType.String;
    /// <summary>Default or configured value, normalized to invariant text.</summary>
    public string? Value { get; init; }
    /// <summary>Generated value configuration.</summary>
    public BpmnTemplateGeneratedValue? GeneratedValue { get; init; }
    /// <summary>Input placeholder.</summary>
    public string? Placeholder { get; init; }
    /// <summary>Dropdown options.</summary>
    public IReadOnlyList<BpmnTemplateChoice> Choices { get; init; } = [];
    /// <summary>FEEL mode.</summary>
    public BpmnTemplateFeel Feel { get; init; }
    /// <summary>Whether value may be omitted.</summary>
    public bool Optional { get; init; }
    /// <summary>Whether users may edit value.</summary>
    public bool Editable { get; init; } = true;
    /// <summary>Group id, when assigned.</summary>
    public string? Group { get; init; }
    /// <summary>Input validation constraints.</summary>
    public BpmnTemplateConstraints? Constraints { get; init; }
    /// <summary>Conditional activation rule.</summary>
    public BpmnTemplateCondition? Condition { get; init; }
    /// <summary>Target BPMN/Camunda binding.</summary>
    public BpmnTemplateBinding? Binding { get; init; }
}

/// <summary>Nested scoped properties in Camunda 7 templates.</summary>
public sealed record BpmnTemplateScope(string Type, IReadOnlyList<BpmnTemplateProperty> Properties, string? Id = null);

/// <summary>Parsed Camunda element template.</summary>
public sealed record BpmnElementTemplate
{
    /// <summary>Schema URI declared by the template.</summary>
    public string? Schema { get; init; }
    /// <summary>Stable template identifier.</summary>
    public string Id { get; init; } = string.Empty;
    /// <summary>Display name.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Template version.</summary>
    public int? Version { get; init; }
    /// <summary>Display description.</summary>
    public string? Description { get; init; }
    /// <summary>Search terms.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];
    /// <summary>Documentation URI.</summary>
    public string? DocumentationRef { get; init; }
    /// <summary>Icon contents or URI; never downloaded.</summary>
    public string? Icon { get; init; }
    /// <summary>Optional category.</summary>
    public BpmnTemplateCategory? Category { get; init; }
    /// <summary>Engine names mapped to semantic-version ranges.</summary>
    public IReadOnlyDictionary<string, string> Engines { get; init; } = new Dictionary<string, string>();
    /// <summary>BPMN types this template supports.</summary>
    public IReadOnlyList<string> AppliesTo { get; init; } = [];
    /// <summary>Element type replacement metadata.</summary>
    public BpmnTemplateElementType? ElementType { get; init; }
    /// <summary>Property groups.</summary>
    public IReadOnlyList<BpmnTemplateGroup> Groups { get; init; } = [];
    /// <summary>Top-level properties.</summary>
    public IReadOnlyList<BpmnTemplateProperty> Properties { get; init; } = [];
    /// <summary>Camunda 7 nested scopes.</summary>
    public IReadOnlyList<BpmnTemplateScope> Scopes { get; init; } = [];
    /// <summary>Visibility rules for standard entries.</summary>
    public BpmnTemplateEntriesVisible EntriesVisible { get; init; } = new();
    /// <summary>Deprecation marker and details.</summary>
    public BpmnTemplateDeprecated? Deprecated { get; init; }
    /// <summary>Inferred product flavor.</summary>
    public BpmnTemplateFlavor Flavor { get; init; }
}

/// <summary>A loader diagnostic.</summary>
public sealed record BpmnTemplateLoadError(string Code, string Message, int? TemplateIndex = null, string? TemplateId = null);
/// <summary>Templates successfully loaded and per-template diagnostics.</summary>
public sealed record BpmnTemplateLoadResult(IReadOnlyList<BpmnElementTemplate> Templates, IReadOnlyList<BpmnTemplateLoadError> Errors);


