using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Validation;

/// <summary>What a connector can be attached to, described by the facts the connection rules read.</summary>
/// <param name="Id">The element's id.</param>
/// <param name="ElementType">
/// The BPMN element type in wire form (<c>userTask</c>, <c>exclusiveGateway</c>), or one of the pseudo
/// types <c>pool</c>, <c>lane</c>, <c>dataObjectReference</c>, <c>dataStoreReference</c>,
/// <c>textAnnotation</c> and <c>group</c>.
/// </param>
/// <param name="ContainerId">The Process or Sub-Process that owns it, which decides Sequence Flow legality.</param>
/// <param name="PoolId">The Pool that draws it, which decides Message Flow legality.</param>
/// <param name="EventDefinitionType">The Event's trigger in wire form (<c>message</c>, <c>none</c>), or null.</param>
/// <param name="IsForCompensation">Whether the Activity is a compensation handler.</param>
public sealed record BpmnConnectionEndpoint(
    string Id,
    string ElementType,
    string? ContainerId = null,
    string? PoolId = null,
    string? EventDefinitionType = null,
    bool IsForCompensation = false);

/// <summary>The outcome of checking one connector against the connection rules.</summary>
/// <param name="IsValid">Whether no rule of error severity is violated.</param>
/// <param name="Violations">Every violated rule, errors and warnings, in table order.</param>
public sealed record BpmnConnectionCheck(bool IsValid, IReadOnlyList<BpmnDiagnostic> Violations);

/// <summary>
/// Whether a connector may join two elements, judged by the <c>live</c> rules of
/// <c>rules/bpmn-rules.json</c>: the same rules, from the same file, that the canvas applies while a
/// connection is being dragged.
/// </summary>
/// <remarks>
/// <para>
/// The canvas refuses an illegal connection before it is ever drawn, so <see cref="BpmnDocumentEditor"/>
/// does not repeat the check. A caller that builds connectors without the canvas — an agent tool, an
/// import step — checks here first, and gets the same verdict and the same rule ids a user would.
/// </para>
/// <para>
/// This is a port of the TypeScript evaluator (<c>src/Bpmn.Scripts/src/rules</c>). Both interpret the
/// table's predicates identically and are tested against one shared case table
/// (<c>rules/connection-cases.json</c>), so a rule cannot pass in one runtime and fail in the other.
/// Rules marked <c>native</c> are evaluated elsewhere, by hand-written code, and are skipped here as
/// they are there.
/// </para>
/// </remarks>
public static class BpmnConnectionRules
{
    private const string ResourceName = "Hasseware.AspNetCore.Components.Bpmn.bpmn-rules.json";

    private static readonly HashSet<string> Events = new(StringComparer.Ordinal)
    {
        "startEvent", "endEvent", "intermediateCatchEvent", "intermediateThrowEvent", "boundaryEvent",
    };

    private static readonly HashSet<string> Activities = new(StringComparer.Ordinal)
    {
        "task", "userTask", "serviceTask", "scriptTask", "manualTask", "businessRuleTask", "sendTask",
        "receiveTask", "subProcess", "eventSubProcess", "transaction", "adHocSubProcess", "callActivity",
    };

    private static readonly HashSet<string> Gateways = new(StringComparer.Ordinal)
    {
        "exclusiveGateway", "inclusiveGateway", "parallelGateway", "complexGateway", "eventBasedGateway",
    };

    private static readonly HashSet<string> Data = new(StringComparer.Ordinal) { "dataObjectReference", "dataStoreReference" };

    private static readonly HashSet<string> Artifacts = new(StringComparer.Ordinal) { "textAnnotation", "group" };

    private static readonly Lazy<Dictionary<BpmnConnectorKind, IReadOnlyList<Rule>>> LiveRules = new(Load);

    /// <summary>Checks a connector between two elements of a document.</summary>
    /// <param name="document">The document.</param>
    /// <param name="kind">The kind of connector.</param>
    /// <param name="sourceId">Where it starts.</param>
    /// <param name="targetId">Where it ends.</param>
    /// <param name="isConditional">Whether a Sequence Flow carries a condition.</param>
    /// <param name="isDefault">Whether a Sequence Flow is its source's default flow.</param>
    /// <returns>The verdict and every violated rule.</returns>
    /// <exception cref="ArgumentException">An id is not something a connector can attach to.</exception>
    public static BpmnConnectionCheck Check(
        BpmnDocument document,
        BpmnConnectorKind kind,
        string sourceId,
        string targetId,
        bool isConditional = false,
        bool isDefault = false)
    {
        ArgumentNullException.ThrowIfNull(document);

        var source = EndpointOf(document, sourceId) ?? throw new ArgumentException($"'{sourceId}' cannot be connected.", nameof(sourceId));
        var target = EndpointOf(document, targetId) ?? throw new ArgumentException($"'{targetId}' cannot be connected.", nameof(targetId));

        return Check(kind, source, target, isConditional, isDefault);
    }

    /// <summary>Checks a connector between two described endpoints.</summary>
    /// <param name="kind">The kind of connector.</param>
    /// <param name="source">Where it starts.</param>
    /// <param name="target">Where it ends.</param>
    /// <param name="isConditional">Whether a Sequence Flow carries a condition.</param>
    /// <param name="isDefault">Whether a Sequence Flow is its source's default flow.</param>
    /// <returns>The verdict and every violated rule.</returns>
    public static BpmnConnectionCheck Check(
        BpmnConnectorKind kind,
        BpmnConnectionEndpoint source,
        BpmnConnectionEndpoint target,
        bool isConditional = false,
        bool isDefault = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var facts = FactsFor(source, target, isConditional, isDefault);
        var violations = new List<BpmnDiagnostic>();

        foreach (var rule in LiveRules.Value.TryGetValue(kind, out var rules) ? rules : [])
        {
            bool violated;
            string message;
            try
            {
                violated = Evaluate(rule.Predicate, facts);
                message = rule.Message;
            }
            catch (InvalidOperationException failure)
            {
                // A rule that cannot be evaluated refuses, as it does on the canvas, rather than passing.
                violations.Add(new BpmnDiagnostic(rule.Id, BpmnSeverity.Error, $"Rule '{rule.Id}' could not be evaluated: {failure.Message}", source.Id, target.Id));
                continue;
            }

            if (violated) violations.Add(new BpmnDiagnostic(rule.Id, rule.Severity, message, source.Id, target.Id));
        }

        return new BpmnConnectionCheck(!violations.Any(violation => violation.Severity == BpmnSeverity.Error), violations);
    }

    /// <summary>The kind of connector a user drawing between two elements would get.</summary>
    /// <param name="source">Where it starts.</param>
    /// <param name="target">Where it ends.</param>
    /// <returns>The connector kind.</returns>
    public static BpmnConnectorKind InferKind(BpmnConnectionEndpoint source, BpmnConnectionEndpoint target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var sourceKind = KindOf(source.ElementType);
        var targetKind = KindOf(target.ElementType);

        if (source.ElementType == "textAnnotation" || target.ElementType == "textAnnotation") return BpmnConnectorKind.Association;
        if (sourceKind == "data" || targetKind == "data") return BpmnConnectorKind.DataAssociation;
        if (sourceKind == "pool" || targetKind == "pool") return BpmnConnectorKind.MessageFlow;

        // Different Pools can only be joined by a Message Flow; the same Pool only by a Sequence Flow.
        if (source.PoolId is not null && target.PoolId is not null && source.PoolId != target.PoolId) return BpmnConnectorKind.MessageFlow;

        // A compensation handler hangs off its Compensation Boundary Event by an Association (SF-008/SF-009).
        if (sourceKind == "event" && source.ElementType == "boundaryEvent" && source.EventDefinitionType == "compensation"
            && targetKind == "activity" && target.IsForCompensation)
        {
            return BpmnConnectorKind.Association;
        }

        return BpmnConnectorKind.SequenceFlow;
    }

    /// <summary>The kind of connector a user drawing between two elements of a document would get.</summary>
    /// <param name="document">The document.</param>
    /// <param name="sourceId">Where it starts.</param>
    /// <param name="targetId">Where it ends.</param>
    /// <returns>The connector kind, or null when either id cannot be connected.</returns>
    public static BpmnConnectorKind? InferKind(BpmnDocument document, string sourceId, string targetId) =>
        EndpointOf(document, sourceId) is { } source && EndpointOf(document, targetId) is { } target ? InferKind(source, target) : null;

    /// <summary>
    /// Describes an element as a connector endpoint, with the same facts the canvas attaches to its shape.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="elementId">The element.</param>
    /// <returns>The endpoint, or null when nothing with that id can be connected.</returns>
    public static BpmnConnectionEndpoint? EndpointOf(BpmnDocument document, string elementId)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.Find(elementId) switch
        {
            BpmnFlowNode node => new BpmnConnectionEndpoint(
                node.Id,
                BpmnElementInspector.WireNameOf(node.Type),
                node.ContainerId,
                node.PoolId,
                JsonSerializer.Serialize(node.EventDefinition).Trim('"'),
                node.IsForCompensation),
            BpmnParticipant pool => new BpmnConnectionEndpoint(pool.Id, "pool", PoolId: pool.Id),
            BpmnLane lane => new BpmnConnectionEndpoint(lane.Id, "lane"),
            BpmnDataObjectReference data => new BpmnConnectionEndpoint(data.Id, "dataObjectReference", data.ContainerId),
            BpmnDataStoreReference store => new BpmnConnectionEndpoint(store.Id, "dataStoreReference", store.ContainerId),
            BpmnTextAnnotation annotation => new BpmnConnectionEndpoint(annotation.Id, "textAnnotation", annotation.ContainerId),
            BpmnGroup group => new BpmnConnectionEndpoint(group.Id, "group", group.ContainerId),
            _ => null,
        };
    }

    private static Dictionary<string, object?> FactsFor(
        BpmnConnectionEndpoint source,
        BpmnConnectionEndpoint target,
        bool isConditional,
        bool isDefault)
    {
        var facts = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["isConditional"] = isConditional,
            ["isDefault"] = isDefault,
        };

        AddSide(facts, "source", source);
        AddSide(facts, "target", target);
        return facts;
    }

    private static void AddSide(Dictionary<string, object?> facts, string side, BpmnConnectionEndpoint endpoint)
    {
        var type = endpoint.ElementType;
        facts[$"{side}.id"] = endpoint.Id;
        facts[$"{side}.elementType"] = type;
        facts[$"{side}.kind"] = KindOf(type);
        facts[$"{side}.category"] = CategoryOf(type);
        facts[$"{side}.subProcessKind"] = SubProcessKindOf(type);
        facts[$"{side}.gatewayType"] = Gateways.Contains(type) ? type[..^"Gateway".Length] : null;
        facts[$"{side}.taskType"] = Activities.Contains(type) ? type : null;
        facts[$"{side}.artifactType"] = Artifacts.Contains(type) || Data.Contains(type) ? type : null;
        facts[$"{side}.containerId"] = endpoint.ContainerId;
        facts[$"{side}.poolId"] = endpoint.PoolId;
        facts[$"{side}.eventDefinitionType"] = endpoint.EventDefinitionType;
        facts[$"{side}.isForCompensation"] = endpoint.IsForCompensation;
    }

    private static string KindOf(string type) =>
        Events.Contains(type) ? "event"
        : Activities.Contains(type) ? "activity"
        : Gateways.Contains(type) ? "gateway"
        : Data.Contains(type) ? "data"
        : Artifacts.Contains(type) ? "artifact"
        : type is "pool" or "lane" ? type
        : "unknown";

    private static string? CategoryOf(string type) => type switch
    {
        "startEvent" => "start",
        "endEvent" => "end",
        "intermediateCatchEvent" => "intermediateCatch",
        "intermediateThrowEvent" => "intermediateThrow",
        "boundaryEvent" => "boundary",
        _ => null,
    };

    private static string? SubProcessKindOf(string type) => type switch
    {
        "subProcess" => "embedded",
        "eventSubProcess" => "eventSubProcess",
        "transaction" => "transaction",
        "adHocSubProcess" => "adHoc",
        "callActivity" => "callActivity",
        _ => null,
    };

    // A predicate is true when the rule is violated: a rule describes what is wrong, not what is required.
    private static bool Evaluate(JsonElement predicate, IReadOnlyDictionary<string, object?> facts)
    {
        var op = predicate.GetProperty("op").GetString();
        return op switch
        {
            "and" => Args(predicate, op).All(arg => Evaluate(arg, facts)),
            "or" => Args(predicate, op).Any(arg => Evaluate(arg, facts)),
            "not" => !Evaluate(predicate.GetProperty("arg"), facts),
            "eq" => Same(Fact(facts, predicate, "field"), Value(predicate.GetProperty("value"))),
            "in" => predicate.GetProperty("values").EnumerateArray().Any(value => Same(Fact(facts, predicate, "field"), Value(value))),
            "notIn" => !predicate.GetProperty("values").EnumerateArray().Any(value => Same(Fact(facts, predicate, "field"), Value(value))),
            "exists" => Fact(facts, predicate, "field") is not null,
            "fieldsEqual" => ComparePair(Fact(facts, predicate, "left"), Fact(facts, predicate, "right"), wantEqual: true),
            "fieldsNotEqual" => ComparePair(Fact(facts, predicate, "left"), Fact(facts, predicate, "right"), wantEqual: false),
            _ => throw new InvalidOperationException($"Unknown operator '{op}'."),
        };
    }

    // An empty 'and' is vacuously true, which would make a malformed rule fire on everything.
    private static List<JsonElement> Args(JsonElement predicate, string? op)
    {
        var args = predicate.GetProperty("args").EnumerateArray().ToList();
        return args.Count == 0 ? throw new InvalidOperationException($"'{op}' needs at least one argument.") : args;
    }

    private static object? Fact(IReadOnlyDictionary<string, object?> facts, JsonElement predicate, string property) =>
        facts.TryGetValue(predicate.GetProperty(property).GetString() ?? string.Empty, out var value) ? value : null;

    private static object? Value(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Number => value.GetDouble(),
        _ => throw new InvalidOperationException($"Unsupported value '{value}'."),
    };

    // A missing value never satisfies either comparison: two absent Pool ids are not "in the same Pool".
    private static bool ComparePair(object? left, object? right, bool wantEqual) =>
        left is not null && right is not null && (wantEqual ? Same(left, right) : !Same(left, right));

    // Absent values are interchangeable, so 'eq: null' works; otherwise strict, ordinal equality.
    private static bool Same(object? left, object? right) =>
        left is null && right is null || (left is not null && left.Equals(right));

    private static Dictionary<BpmnConnectorKind, IReadOnlyList<Rule>> Load()
    {
        using var stream = typeof(BpmnConnectionRules).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded rule table '{ResourceName}' is missing.");
        using var table = JsonDocument.Parse(stream);

        var rules = new Dictionary<BpmnConnectorKind, List<Rule>>();
        foreach (var entry in table.RootElement.GetProperty("rules").EnumerateArray())
        {
            if (entry.GetProperty("scope").GetString() != "live"
                || (entry.TryGetProperty("native", out var native) && native.ValueKind == JsonValueKind.True)
                || !entry.TryGetProperty("predicate", out var predicate)
                || !entry.TryGetProperty("connectorKind", out var kindName)
                || KindNamed(kindName.GetString()) is not { } kind)
            {
                continue;
            }

            if (!rules.TryGetValue(kind, out var list)) rules[kind] = list = [];
            list.Add(new Rule(
                entry.GetProperty("id").GetString()!,
                entry.GetProperty("severity").GetString() == "warning" ? BpmnSeverity.Warning : BpmnSeverity.Error,
                entry.GetProperty("message").GetString()!,
                predicate.Clone()));
        }

        return rules.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<Rule>)pair.Value);
    }

    private static BpmnConnectorKind? KindNamed(string? name) => name switch
    {
        "sequenceFlow" => BpmnConnectorKind.SequenceFlow,
        "messageFlow" => BpmnConnectorKind.MessageFlow,
        "association" => BpmnConnectorKind.Association,
        "dataAssociation" => BpmnConnectorKind.DataAssociation,
        _ => null,
    };

    private sealed record Rule(string Id, BpmnSeverity Severity, string Message, JsonElement Predicate);
}
