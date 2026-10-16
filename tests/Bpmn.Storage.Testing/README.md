# Hasseware.AspNetCore.Components.Bpmn.Storage.Testing (preview)

The test suite every BPMN document store must pass, for stores you write yourself. It checks the contract of
`IBpmnDocumentStore`: create-only and conditional saves, stale revisions, exactly one winner among concurrent writers,
hierarchical listing with stable paging, tenants kept apart, and names that differ only by case never overwriting one another.

```csharp
public sealed class MyStoreTests : BpmnDocumentStoreContractTests
{
    protected override IBpmnDocumentStore CreateStore() => new MyStore(/* empty, for this test */);
}
```

Use it from an xUnit v3 test project. It is a test-only dependency: nothing in the editor packages references it.
