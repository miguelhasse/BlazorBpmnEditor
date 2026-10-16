namespace Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema;

/// <summary>A moddle-format extension package, identified by namespace URI rather than prefix.</summary>
public sealed record BpmnExtensionSchema(string Name, string Uri, string Prefix, IReadOnlyList<BpmnExtensionType> Types)
{
    /// <summary>Optional package version.</summary>
    public string? Version { get; init; }
    /// <summary>Declared enumeration types.</summary>
    public IReadOnlyList<BpmnExtensionEnumeration> Enumerations { get; init; } = [];
    /// <summary>XML tag alias convention; moddle uses "lowerCase" to lowercase the first character.</summary>
    public string? TagAlias { get; init; }
    /// <summary>Optional prefix used for xsi:type values.</summary>
    public string? TypePrefix { get; init; }
}

/// <summary>A moddle extension type or trait.</summary>
public sealed record BpmnExtensionType(string Name, IReadOnlyList<string> SuperClass, IReadOnlyList<string> Extends, IReadOnlyList<BpmnExtensionProperty> Properties)
{
    /// <summary>Namespace URI of the declaring package.</summary>
    public string NamespaceUri { get; init; } = "";
    /// <summary>Preferred package prefix.</summary>
    public string Prefix { get; init; } = "";
    /// <summary>Qualified moddle type name.</summary>
    public string QualifiedName => string.IsNullOrEmpty(Prefix) ? Name : $"{Prefix}:{Name}";
    /// <summary>XML element name, respecting the package tag alias.</summary>
    public string ElementName { get; init; } = Name;
    /// <summary>Whether the type cannot be instantiated.</summary>
    public bool IsAbstract { get; init; }
    /// <summary>Optional restrictions from meta.allowedIn; empty means unspecified.</summary>
    public IReadOnlyList<string> AllowedIn { get; init; } = [];
}

/// <summary>A moddle property, including its XML representation and reference semantics.</summary>
public sealed record BpmnExtensionProperty(string Name, string Type, bool IsAttr, bool IsMany, bool IsBody, bool IsReference, string? Default)
{
    /// <summary>XML serialization hint, normally "property" or "xsi:type".</summary>
    public string? Serialize { get; init; }
    /// <summary>Namespace URI of the declaring package.</summary>
    public string NamespaceUri { get; init; } = "";
    /// <summary>Preferred package prefix.</summary>
    public string Prefix { get; init; } = "";
    /// <summary>Qualified type declaring this property.</summary>
    public string DeclaringType { get; init; } = "";
    /// <summary>Qualified property name.</summary>
    public string QualifiedName => Name.Contains(':') || string.IsNullOrEmpty(Prefix) ? Name : $"{Prefix}:{Name}";
}

/// <summary>A named enumeration in an extension package.</summary>
public sealed record BpmnExtensionEnumeration(string Name, IReadOnlyList<string> Values);

/// <summary>Invalid, conflicting or oversized extension schema content.</summary>
public sealed class BpmnExtensionSchemaException : Exception
{
    /// <summary>Creates a schema error with optional underlying cause.</summary>
    public BpmnExtensionSchemaException(string message, Exception? innerException = null) : base(message, innerException) { }
}
