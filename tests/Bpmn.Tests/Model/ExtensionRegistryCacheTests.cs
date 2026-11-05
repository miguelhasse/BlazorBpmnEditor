using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Extensions;

public sealed class ExtensionRegistryCacheTests
{
    [Fact]
    public void QueriesAfterLateSuperclassRegistrationRefreshPropertiesAndChildOrder()
    {
        var registry = new BpmnExtensionRegistry().Register(new("Derived", "urn:d", "d",
            [new("Item", ["base:Parent"], [], [Property("own")])]));
        var item = registry.TryGetSchema("urn:d")!.Types[0];
        Assert.Equal(["own"], registry.GetAllProperties(item).Select(property => property.Name));
        Assert.Equal(["own"], registry.GetChildOrder(item));

        registry.Register(new("Base", "urn:b", "base", [new("Parent", [], [], [Property("first")])]));

        Assert.Equal(["first", "own"], registry.GetAllProperties(item).Select(property => property.Name));
        Assert.Equal(["first", "own"], registry.GetChildOrder(item));
        Assert.True(registry.CompareChildOrder(item, "first", "own") < 0);
    }

    [Fact]
    public void QueriesAfterLateTraitRegistrationPreserveOverridePositionAndReferences()
    {
        var registry = new BpmnExtensionRegistry().Register(new("Base", "urn:b", "b",
            [new("Item", [], [], [Property("first"), Property("second")])]));
        var item = registry.TryGetSchema("urn:b")!.Types[0];
        var before = registry.GetAllProperties(item);
        Assert.Equal(["first", "second"], registry.GetChildOrder(item));

        registry.Register(new("Trait", "urn:t", "t",
            [new("Trait", [], ["b:Item"], [Property("first") with { IsAttr = true, IsReference = true }, Property("third")])]));

        Assert.Equal(["first", "second", "third"], registry.GetAllProperties(item).Select(property => property.Name));
        Assert.Equal(["second", "third"], registry.GetChildOrder(item));
        Assert.True(registry.IsReference(item, "first"));
        Assert.False(before[0].IsReference);
    }

    [Fact]
    public void QueriesAfterLateCyclicSuperclassThrowAfterEarlierWarmQueries()
    {
        var registry = new BpmnExtensionRegistry().Register(new("First", "urn:a", "a",
            [new("Item", ["b:Parent"], [], [Property("own")])]));
        var item = registry.TryGetSchema("urn:a")!.Types[0];
        registry.GetAllProperties(item);
        registry.GetChildOrder(item);

        registry.Register(new("Second", "urn:b", "b", [new("Parent", ["a:Item"], [], [])]));

        Assert.Throws<BpmnExtensionSchemaException>(() => registry.GetAllProperties(item));
        Assert.Throws<BpmnExtensionSchemaException>(() => registry.GetChildOrder(item));
        Assert.Throws<BpmnExtensionSchemaException>(() => registry.GetAllProperties(item));
    }

    [Fact]
    public void ChildOrderAfterLateReferencedTypeRegistrationUsesNewAlias()
    {
        var registry = new BpmnExtensionRegistry().Register(new("Parent", "urn:p", "p",
            [new("Parent", [], [], [Property("fallback") with { Type = "late:Child", Serialize = null }])]));
        var parent = registry.TryGetSchema("urn:p")!.Types[0];
        Assert.Equal(["fallback"], registry.GetChildOrder(parent));

        registry.Register(new("Late", "urn:l", "late", [new("Child", [], [], []) { ElementName = "alias" }]));

        Assert.Equal(["alias"], registry.GetChildOrder(parent));
    }

    [Fact]
    public void QueriesAfterCallerListMutationsKeepRegisteredSnapshot()
    {
        var properties = new List<BpmnExtensionProperty> { Property("first") };
        var superClasses = new List<string>();
        var traits = new List<string> { "s:Item" };
        var types = new List<BpmnExtensionType>
        {
            new("Item", superClasses, [], properties),
            new("Trait", [], traits, [Property("second")]),
        };
        var registry = new BpmnExtensionRegistry().Register(new("Schema", "urn:s", "s", types));
        var item = registry.TryGetSchema("urn:s")!.Types[0];
        Assert.Equal(["first", "second"], registry.GetChildOrder(item));
        properties.Clear();
        superClasses.Add("Item");
        traits.Clear();
        types.Clear();

        var result = registry.GetChildOrder(item);

        Assert.Equal(["first", "second"], result);
        Assert.Equal(["first", "second"], registry.GetAllProperties(item).Select(property => property.Name));
    }

    [Fact]
    public void QueriesWithDiamondInheritanceAndPropertyAliasesPreserveOrderAndDeduplication()
    {
        var registry = new BpmnExtensionRegistry().Register(new("Diamond", "urn:d", "d",
        [
            new("Root", [], [], [Property("root")]),
            new("Left", ["Root"], [], [Property("left")]),
            new("Right", ["Root"], [], [Property("right")]),
            new("Leaf", ["Left", "Right"], [],
            [
                Property("root") with { Default = "override" },
                Property("byType") with { Type = "Child", Serialize = null },
                Property("byProperty") with { Type = "Child" },
                Property("body") with { IsBody = true },
            ]),
            new("Child", [], [], []) { ElementName = "alias" },
        ]));
        var leaf = registry.TryGetSchema("urn:d")!.Types[3];

        var first = registry.GetAllProperties(leaf);
        var order = registry.GetChildOrder(leaf);
        var repeated = registry.GetAllProperties(leaf);

        Assert.Equal(["root", "left", "right", "byType", "byProperty", "body"], first.Select(property => property.Name));
        Assert.Equal("override", first[0].Default);
        Assert.Equal(first, repeated);
        Assert.Equal(["root", "left", "right", "alias", "byProperty"], order);
        Assert.Equal(0, registry.CompareChildOrder(leaf, "unknown", "otherUnknown"));
    }

    [Fact]
    public void QueriesWithExternalMutableTypeObserveChangesAndDoNotContaminateRegisteredNames()
    {
        var registry = new BpmnExtensionRegistry();
        var properties = new List<BpmnExtensionProperty> { Property("first") };
        var external = new BpmnExtensionType("Item", [], [], properties);
        Assert.Single(registry.GetAllProperties(external));
        Assert.Equal(["first"], registry.GetChildOrder(external));
        properties.Add(Property("second"));

        var changed = registry.GetAllProperties(external);

        Assert.Equal(["first", "second"], changed.Select(property => property.Name));
        Assert.Equal(["first", "second"], registry.GetChildOrder(external));
        registry.Register(new("Registered", "urn:r", "r", [new("Item", [], [], [Property("registered")])]));
        var registered = registry.TryGetSchema("urn:r")!.Types[0];
        Assert.Equal(["registered"], registry.GetChildOrder(registered));
        Assert.Equal(["registered"], registry.GetChildOrder(external));
        Assert.Equal(["registered"], registry.GetChildOrder(registered with { Properties = properties }));
    }

    [Fact]
    public void QueryCollectionsRejectMutationAndPreserveRegistryIsolation()
    {
        var schema = new BpmnExtensionSchema("Schema", "urn:s", "s", [new("Item", [], [], [Property("first")])]);
        var first = new BpmnExtensionRegistry().Register(schema);
        var second = new BpmnExtensionRegistry().Register(schema);
        var type = first.TryGetSchema("urn:s")!.Types[0];
        var properties = first.GetAllProperties(type);
        var order = first.GetChildOrder(type);

        Assert.Throws<NotSupportedException>(() => ((IList<BpmnExtensionProperty>)properties).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)order)[0] = "changed");
        first.Register(new("Trait", "urn:t", "t", [new("Trait", [], ["s:Item"], [Property("second")])]));

        Assert.Equal(["first", "second"], first.GetChildOrder(type));
        Assert.Equal(["first"], second.GetChildOrder(second.TryGetSchema("urn:s")!.Types[0]));
        Assert.Equal(["first"], order);
    }

    [Fact]
    public void IdempotentOrConflictingRegistrationKeepsWarmQueryContents()
    {
        var schema = new BpmnExtensionSchema("Schema", "urn:s", "s", [new("Item", [], [], [Property("first")])]);
        var registry = new BpmnExtensionRegistry().Register(schema);
        var type = registry.TryGetSchema("urn:s")!.Types[0];
        var before = registry.GetAllProperties(type);
        var order = registry.GetChildOrder(type);

        registry.Register(schema);

        Assert.Throws<BpmnExtensionSchemaException>(() => registry.Register(schema with { Version = "conflict" }));
        Assert.Equal(before, registry.GetAllProperties(type));
        Assert.Equal(order, registry.GetChildOrder(type));
    }

    [Fact]
    public async Task QueriesWithConcurrentRegistrationPublishOnlyCompleteResults()
    {
        var registry = new BpmnExtensionRegistry().Register(new("Schema", "urn:s", "s",
            [new("Item", [], [], [Property("first")])]));
        var type = registry.TryGetSchema("urn:s")!.Types[0];
        registry.GetAllProperties(type);
        registry.GetChildOrder(type);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            for (var index = 0; index < 100; index++)
            {
                var properties = registry.GetAllProperties(type);
                Assert.InRange(properties.Count, 1, 2);
                Assert.Equal("first", properties[0].Name);
                Assert.Equal(properties.Count == 1 ? "first" : "second", properties[^1].Name);
                var order = registry.GetChildOrder(type);
                Assert.InRange(order.Count, 1, 2);
                Assert.Equal("first", order[0]);
                Assert.Equal(order.Count == 1 ? "first" : "second", order[^1]);
            }
        }, TestContext.Current.CancellationToken)).ToArray();
        var registration = Task.Run(async () =>
        {
            await start.Task;
            registry.Register(new("Trait", "urn:t", "t", [new("Trait", [], ["s:Item"], [Property("second")])]));
        }, TestContext.Current.CancellationToken);

        start.SetResult();
        await Task.WhenAll([.. readers, registration]);

        Assert.Equal(["first", "second"], registry.GetChildOrder(type));
    }

    [Fact]
    public void QueriesForWarmedOwnedTypeStayWithinAllocationBudget()
    {
        var registry = new BpmnExtensionRegistry().Register(BpmnExtensionSchemas.Zeebe).Register(BpmnExtensionSchemas.Camunda7);
        var type = registry.TryGetSchema(BpmnExtensionSchemas.ZeebeNamespace)!.Types[0];
        registry.GetAllProperties(type);
        registry.GetChildOrder(type);
        var before = GC.GetAllocatedBytesForCurrentThread();

        var properties = registry.GetAllProperties(type);
        var order = registry.GetChildOrder(type);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.NotNull(properties);
        Assert.NotNull(order);
        Assert.InRange(allocated, 0, 256);
    }

    private static BpmnExtensionProperty Property(string name) =>
        new(name, "String", false, false, false, false, null) { Serialize = "property" };
}
