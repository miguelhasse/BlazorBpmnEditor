using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.JSInterop;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Architecture;

public sealed class PackageBoundaryTests
{
    [Fact]
    public void StandardEditorHasNoFluentDependency() =>
        Assert.DoesNotContain(typeof(BpmnEditor).Assembly.GetReferencedAssemblies(), name => name.Name!.StartsWith("Microsoft.FluentUI.", StringComparison.Ordinal));

    [Fact]
    public void StandardEditorHasNoJavaScriptModuleOwners() =>
        Assert.DoesNotContain(typeof(BpmnEditor).Assembly.GetTypes(),
            type => type.GetCustomAttribute<CompilerGeneratedAttribute>() is null
                && type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(field => typeof(IJSObjectReference).IsAssignableFrom(field.FieldType)));
}
