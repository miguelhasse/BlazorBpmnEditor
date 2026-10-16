using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools;

/// <summary>Reads tool arguments and writes tool results as JSON, for adapters that speak JSON.</summary>
/// <remarks>
/// Source-generated, so it works in trimmed WebAssembly hosts. Names are camelCase and nulls are omitted.
/// </remarks>
public static class BpmnToolJson
{
    /// <summary>Reads a tool's arguments.</summary>
    /// <typeparam name="TRequest">The request record.</typeparam>
    /// <param name="arguments">The arguments as JSON; an empty object when the tool takes none.</param>
    /// <param name="request">The request, when it could be read.</param>
    /// <returns>Whether the arguments were well formed.</returns>
    public static bool TryRead<TRequest>(JsonElement arguments, out TRequest? request)
        where TRequest : class
    {
        request = null;
        if (BpmnToolsJsonContext.Default.GetTypeInfo(typeof(TRequest)) is not JsonTypeInfo<TRequest> info) return false;

        try
        {
            request = arguments.Deserialize(info);
            return request is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Writes a result, including its data, whatever the result's payload type.</summary>
    /// <param name="result">The result.</param>
    /// <returns>The JSON text.</returns>
    public static string Serialize(BpmnToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var info = BpmnToolsJsonContext.Default.GetTypeInfo(result.GetType())
            ?? throw new InvalidOperationException($"'{result.GetType()}' is not a tool result type.");
        return JsonSerializer.Serialize(result, info);
    }
}

/// <summary>The serialization metadata for every tool request and result.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,

    // A request is what the agent said. A required argument that is missing is a refusal and not a default (a missing `value` was a null, which
    // clears the property; a missing `elementIds` was a null list), and an argument the tool does not take is a mistake to report, not to ignore.
    RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    AllowDuplicateProperties = false,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(BpmnGetDiagramRequest))]
[JsonSerializable(typeof(BpmnDescribeElementRequest))]
[JsonSerializable(typeof(BpmnExportRequest))]
[JsonSerializable(typeof(BpmnListCreatableRequest))]
[JsonSerializable(typeof(BpmnCreateElementRequest))]
[JsonSerializable(typeof(BpmnConnectRequest))]
[JsonSerializable(typeof(BpmnSetPropertyRequest))]
[JsonSerializable(typeof(BpmnDeleteRequest))]
[JsonSerializable(typeof(BpmnSelectRequest))]
[JsonSerializable(typeof(BpmnNoArguments))]
[JsonSerializable(typeof(BpmnToolResult))]
[JsonSerializable(typeof(BpmnToolResult<object>))]
[JsonSerializable(typeof(BpmnToolResult<BpmnDiagramSummary>))]
[JsonSerializable(typeof(BpmnToolResult<BpmnSelection>))]
[JsonSerializable(typeof(BpmnToolResult<BpmnElementDescription>))]
[JsonSerializable(typeof(BpmnToolResult<BpmnValidationSummary>))]
[JsonSerializable(typeof(BpmnToolResult<BpmnExportData>))]
[JsonSerializable(typeof(BpmnToolResult<BpmnCreatableList>))]
[JsonSerializable(typeof(BpmnToolResult<BpmnCreatedElement>))]
[JsonSerializable(typeof(BpmnToolResult<BpmnCreatedConnector>))]
[JsonSerializable(typeof(BpmnToolResult<BpmnDocumentDiffResult>))]
[JsonSerializable(typeof(BpmnToolResult<BpmnDeletedElements>))]
[JsonSerializable(typeof(BpmnToolResult<BpmnDocumentInfo>))]
[JsonSerializable(typeof(BpmnToolResult<BpmnSaved>))]
[JsonSerializable(typeof(BpmnToolResult<BpmnDocumentListing>))]
[JsonSerializable(typeof(BpmnToolResult<BpmnBatchResult>))]
internal sealed partial class BpmnToolsJsonContext : JsonSerializerContext;

/// <summary>The arguments of a tool that takes none.</summary>
public sealed record BpmnNoArguments;
