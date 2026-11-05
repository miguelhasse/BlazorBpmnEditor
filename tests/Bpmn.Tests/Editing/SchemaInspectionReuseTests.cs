using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class SchemaInspectionReuseTests
{
    private static readonly string[] SchemaSets = ["zeebe", "camunda", "both"];

    public static IEnumerable<object[]> NodeTypesAndSchemas =>
        from type in Enum.GetValues<BpmnFlowNodeType>()
        from schemas in SchemaSets
        select new object[] { type, schemas };

    [Theory]
    [MemberData(nameof(NodeTypesAndSchemas))]
    public void ReusedApplicabilityMatchesIndependentQueries(BpmnFlowNodeType type, string schemas)
    {
        var options = new BpmnExtensionOptions();
        if (schemas is "zeebe" or "both") options.AddZeebe();
        if (schemas is "camunda" or "both") options.AddCamunda7();
        var document = new BpmnDocumentBuilder().Node("task", type, new Bounds(0, 0, 100, 80)).Build();
        var node = document.FlowNodes[0] with
        {
            Extras = new()
            {
                Extensions =
                [
                    new(BpmnExtensionSchemas.ZeebeNamespace, "ioMapping", new Dictionary<string, string>(),
                    [
                        new(BpmnExtensionSchemas.ZeebeNamespace, "input",
                            new Dictionary<string, string> { ["target"] = "customer", ["source"] = "=customer.id" }, []),
                    ]),
                    new("urn:host", "opaque", new Dictionary<string, string> { ["value"] = "preserved" }, []),
                ],
            },
        };
        var snapshot = BpmnElementInspector.Describe(document, "task")!;
        var builder = new BpmnSchemaPropertiesProvider();
        var expected = builder.Build(snapshot, node, node.Extras, options.Registry, new HostLabels());

        var actual = builder.Build(snapshot, node, node.Extras, options.Registry, options.LabelProvider);

        Assert.Equal(expected.ClaimedRoots, actual.ClaimedRoots);
        Assert.Equivalent(expected.Groups, actual.Groups.Select(group => group with { HasBuiltInLabels = false }).ToArray(), strict: true);
    }

    [Fact]
    public void SchemaRegistrationRemainsVisibleBetweenSnapshots()
    {
        var options = new BpmnExtensionOptions();
        var document = new BpmnDocumentBuilder()
            .Node("task", BpmnFlowNodeType.ServiceTask, new Bounds(0, 0, 100, 80)).Build();
        var before = BpmnElementInspector.Describe(document, "task", extensionOptions: options)!;
        options.AddZeebe();

        var after = BpmnElementInspector.Describe(document, "task", extensionOptions: options)!;

        Assert.Empty(before.ExtensionGroups);
        Assert.Contains(after.ExtensionGroups, group => group.Id == "task-definition");
    }

    [Fact]
    public void CustomLabelProviderCanRegisterSchemasBetweenApplicabilityQueries()
    {
        var registry = new BpmnExtensionRegistry().Register(new("Zeebe", BpmnExtensionSchemas.ZeebeNamespace, "zeebe",
        [
            new("TaskDefinition", ["Element"], [], []) { AllowedIn = ["bpmn:ServiceTask"] },
            new("IoMapping", ["late:Root"], [], []),
            new("Input", ["Element"], [], []),
            new("Output", ["Element"], [], []),
        ])
        { TagAlias = "lowerCase" });
        var document = new BpmnDocumentBuilder()
            .Node("task", BpmnFlowNodeType.ServiceTask, new Bounds(0, 0, 100, 80)).Build();
        var snapshot = BpmnElementInspector.Describe(document, "task")!;
        Assert.DoesNotContain(registry.ExtensionElementTypesFor("serviceTask"), type => type.Name == "IoMapping");

        var result = new BpmnSchemaPropertiesProvider().Build(snapshot, document.FlowNodes[0],
            BpmnElementExtras.None, registry, new RegisteringLabels(registry));

        Assert.Contains(result.Groups, group => group.Id == "inputs");
        Assert.Contains(result.Groups, group => group.Id == "outputs");
    }

    [Fact]
    public void ZeebeSnapshotStaysWithinAllocationBudget()
    {
        var options = new BpmnExtensionOptions().AddZeebe();
        var document = new BpmnDocumentBuilder()
            .Node("task", BpmnFlowNodeType.ServiceTask, new Bounds(0, 0, 100, 80)).Build();
        for (var warm = 0; warm < 3; warm++)
            BpmnElementInspector.Describe(document, "task", extensionOptions: options);
        var before = GC.GetAllocatedBytesForCurrentThread();

        var snapshot = BpmnElementInspector.Describe(document, "task", extensionOptions: options)!;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Contains(snapshot.ExtensionGroups, group => group.Id == "task-definition");
        Assert.InRange(allocated, 0, 140_000);
    }

    private sealed class HostLabels : IBpmnPropertyLabelProvider
    {
        public string GetLabel(string key, string englishFallback) => englishFallback;
    }

    private sealed class RegisteringLabels(BpmnExtensionRegistry registry) : IBpmnPropertyLabelProvider
    {
        public string GetLabel(string key, string englishFallback)
        {
            registry.Register(new("Late", "urn:late", "late", [new("Root", ["Element"], [], [])]));
            return englishFallback;
        }
    }
}
