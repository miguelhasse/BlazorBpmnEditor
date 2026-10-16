using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Interop;

/// <summary>
/// The document as the canvas receives it: the same JSON the interop serializer would write, without the properties the canvas never reads.
/// </summary>
/// <remarks>
/// <para>
/// Every load sent the whole document, including what only the C# side uses (the root catalog, data objects, process roots, script text,
/// loop details, event definitions, documentation, vendor extension trees, DI label ids and styles). On the 602-shape stress diagram that was
/// 833,459 characters, and serializing it in WebAssembly is real time. The TypeScript contract (<c>model/contracts.ts</c>) is what the
/// canvas reads; properties it marks optional or does not declare are left out here.
/// </para>
/// <para>
/// Only what the canvas never reads is left out, and the rest is written exactly as before, nulls included: the canvas distinguishes
/// <c>null</c> from absent in places, so no value is dropped for being empty. The result is cached for the document it was made from, so a
/// re-render that sends the same document again does not serialize it again.
/// </para>
/// </remarks>
internal static class CanvasDocumentPayload
{
    /// <summary>The properties left out, by the name of the type that declares them and the name they are written under.</summary>
    private static readonly Dictionary<string, HashSet<string>> Omitted = new(StringComparer.Ordinal)
    {
        [nameof(BpmnDocument)] = ["processes", "dataObjects", "catalog"],
        [nameof(BpmnFlowNode)] =
        [
            "instantiate", "defaultFlowRef", "calledElementQName", "messageRef", "scriptFormat", "script",
            "standardLoop", "multiInstanceLoop", "activationCondition", "eventDefinitions",
        ],
        [nameof(BpmnSequenceFlow)] = ["conditionLanguage"],
        [nameof(BpmnMessageFlow)] = ["messageRef"],
        [nameof(BpmnDataAssociation)] = ["transformation"],
        [nameof(BpmnGroup)] = ["categoryValueRef"],
        [nameof(BpmnShape)] = ["isMarkerVisible"],
        [nameof(BpmnLabel)] = ["id", "styleId"],
        // The template icon is read from a flow node's foreign attributes; nothing else of the extras is.
        [nameof(BpmnElementExtras)] = ["documentation", "extensions", "isEmpty"],
    };

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { Trim } },
    };

    private static readonly ConditionalWeakTable<BpmnDocument, object> Made = [];

    /// <summary>The canvas's view of a document, or null when there is none.</summary>
    /// <param name="document">The document to draw.</param>
    /// <returns>JSON for the interop call, which writes it as it is.</returns>
    internal static JsonElement? For(BpmnDocument? document)
    {
        if (document is null) return null;

        // GetValue, not a check and then an add: two circuits can render the same document at once (a shared sample, a cached document), and the second
        // add of one key throws. At worst both serialize and one result is kept.
        return (JsonElement)Made.GetValue(document, key => JsonSerializer.SerializeToElement(key, Options));
    }

    /// <summary>The same projection as JSON text, for measuring it.</summary>
    /// <param name="document">The document to project.</param>
    /// <returns>The JSON the canvas is sent.</returns>
    internal static string Serialize(BpmnDocument document) => JsonSerializer.Serialize(document, Options);

    private static void Trim(JsonTypeInfo type)
    {
        if (type.Kind != JsonTypeInfoKind.Object) return;

        Omitted.TryGetValue(type.Type.Name, out var names);

        for (var index = type.Properties.Count - 1; index >= 0; index--)
        {
            var name = type.Properties[index].Name;

            // The other drawn elements carry the same extras; only a flow node's foreign attributes are read.
            var drop = (names?.Contains(name) ?? false)
                || (name == "extras" && type.Type != typeof(BpmnFlowNode));
            if (drop)
            {
                type.Properties.RemoveAt(index);
                continue;
            }

            // A flow node with no foreign attributes carries no extras: the canvas reads `extras?.foreignAttributes`, so an absent object
            // and an empty one are the same to it, and most nodes have none.
            if (name == "extras")
            {
                type.Properties[index].ShouldSerialize = static (_, value) => value is BpmnElementExtras { ForeignAttributes.Count: > 0 };
            }
        }
    }
}
