using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

/// <summary>Reads the JSON wire name of the library's enums without reflection-based serialization.</summary>
public static class BpmnWireNames
{
    /// <summary>Returns the serialized enum name, or its numeric representation for an unknown value.</summary>
    /// <typeparam name="TEnum">An enum with its wire-name converter.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>The wire name.</returns>
    public static string Of<TEnum>(TEnum value) where TEnum : struct, Enum =>
        JsonSerializer.Serialize(value, EnumContext<TEnum>.TypeInfo).Trim('"');

    private static class EnumContext<TEnum> where TEnum : struct, Enum
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(),
        };

        internal static readonly JsonTypeInfo<TEnum> TypeInfo =
            JsonMetadataServices.CreateValueInfo<TEnum>(Options,
                new JsonStringEnumConverter<TEnum>().CreateConverter(typeof(TEnum), Options));
    }
}
