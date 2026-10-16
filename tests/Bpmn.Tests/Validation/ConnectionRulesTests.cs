using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Validation;

/// <summary>
/// The live connection rules, evaluated in C# exactly as the canvas evaluates them in TypeScript.
/// </summary>
/// <remarks>
/// The case table is shared with <c>src/Bpmn.Scripts/test/rules/connection-cases.test.ts</c>, so a case
/// whose outcome differs between the two runtimes fails in at least one suite.
/// </remarks>
public sealed class ConnectionRulesTests
{
    // --- the shared table --------------------------------------------------------------------

    public static TheoryData<string> CaseNames => [.. Cases().Select(entry => entry.GetProperty("name").GetString()!)];

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void AgreesWithTheCanvasOnEverySharedCase(string name)
    {
        var entry = Cases().Single(candidate => candidate.GetProperty("name").GetString() == name);
        var source = Endpoint(entry.GetProperty("source"));
        var target = Endpoint(entry.GetProperty("target"));

        var check = BpmnConnectionRules.Check(
            Kind(entry.GetProperty("kind").GetString()!),
            source,
            target,
            entry.GetProperty("isConditional").GetBoolean(),
            entry.GetProperty("isDefault").GetBoolean());

        Assert.Equal(
            [.. entry.GetProperty("violations").EnumerateArray().Select(id => id.GetString()!)],
            check.Violations.Select(violation => violation.RuleId));
        Assert.Equal(Kind(entry.GetProperty("inferredKind").GetString()!), BpmnConnectionRules.InferKind(source, target));
    }

    // --- endpoints from a document -----------------------------------------------------------

    [Fact]
    public void DescribesAFlowNodeWithTheFactsTheCanvasAttaches()
    {
        var document = new BpmnDocumentBuilder()
            .Node("t", BpmnFlowNodeType.UserTask, new Bounds(0, 0, 100, 80), poolId: "pool")
            .Node("e", BpmnFlowNodeType.StartEvent, new Bounds(200, 0, 36, 36), eventDefinition: BpmnEventDefinitionType.Message)
            .Build();

        Assert.Equal(
            new BpmnConnectionEndpoint("t", "userTask", "Process_1", "pool", "none", false),
            BpmnConnectionRules.EndpointOf(document, "t"));
        Assert.Equal("message", BpmnConnectionRules.EndpointOf(document, "e")!.EventDefinitionType);
    }

    [Fact]
    public void DescribesAPoolAndArtifacts()
    {
        var document = new BpmnDocumentBuilder()
            .Pool("pool", "Pool", new Bounds(0, 0, 600, 250))
            .TextAnnotation("note", "Note", new Bounds(0, 300, 100, 30))
            .DataObject("data", "Data", new Bounds(200, 300, 36, 50))
            .Build();

        Assert.Equal(new BpmnConnectionEndpoint("pool", "pool", PoolId: "pool"), BpmnConnectionRules.EndpointOf(document, "pool"));
        Assert.Equal("textAnnotation", BpmnConnectionRules.EndpointOf(document, "note")!.ElementType);
        Assert.Equal("dataObjectReference", BpmnConnectionRules.EndpointOf(document, "data")!.ElementType);
        Assert.Null(BpmnConnectionRules.EndpointOf(document, "missing"));
    }

    [Fact]
    public void RefusesASequenceFlowOutOfAnEndEventInADocument()
    {
        var document = new BpmnDocumentBuilder()
            .Node("end", BpmnFlowNodeType.EndEvent, new Bounds(0, 0, 36, 36))
            .Node("t", BpmnFlowNodeType.Task, new Bounds(100, 0, 100, 80))
            .Build();

        var check = BpmnConnectionRules.Check(document, BpmnConnectorKind.SequenceFlow, "end", "t");

        Assert.False(check.IsValid);
        var violation = Assert.Single(check.Violations);
        Assert.Equal("SF-002", violation.RuleId);
        Assert.Equal("end", violation.ElementId);
        Assert.Equal("t", violation.RelatedId);
    }

    [Fact]
    public void RefusesAnIdThatCannotBeConnected()
    {
        var document = new BpmnDocumentBuilder().Node("t", BpmnFlowNodeType.Task, new Bounds(0, 0, 100, 80)).Build();

        Assert.Throws<ArgumentException>(() => BpmnConnectionRules.Check(document, BpmnConnectorKind.SequenceFlow, "t", "missing"));
        Assert.Null(BpmnConnectionRules.InferKind(document, "t", "missing"));
    }

    // --- helpers -----------------------------------------------------------------------------

    private static List<JsonElement> Cases()
    {
        var path = Path.Combine(FindRepositoryRoot(), "rules", "connection-cases.json");
        using var table = JsonDocument.Parse(File.ReadAllText(path));
        return [.. table.RootElement.GetProperty("cases").EnumerateArray().Select(entry => entry.Clone())];
    }

    private static BpmnConnectionEndpoint Endpoint(JsonElement json) => new(
        json.GetProperty("id").GetString()!,
        json.GetProperty("elementType").GetString()!,
        json.GetProperty("containerId").GetString(),
        json.GetProperty("poolId").GetString(),
        json.GetProperty("eventDefinitionType").GetString(),
        json.GetProperty("isForCompensation").GetBoolean());

    private static BpmnConnectorKind Kind(string name) => JsonSerializer.Deserialize<BpmnConnectorKind>($"\"{name}\"");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "rules")) && Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output folder.");
    }
}
