using System.Reflection;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools.Tests;

/// <summary>
/// The shared tool layer depends on Core and nothing UI- or protocol-specific, so both the browser module
/// and an MCP server can carry it.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly Assembly Tools = typeof(BpmnTools).Assembly;

    [Theory]
    [InlineData("Microsoft.JSInterop")]
    [InlineData("Microsoft.AspNetCore.Components.Web")]
    [InlineData("ModelContextProtocol")]
    [InlineData("Microsoft.FluentUI")]
    public void ReferencesNoBrowserOrProtocolAssembly(string prefix)
    {
        Assert.DoesNotContain(Tools.GetReferencedAssemblies(), name => name.Name!.StartsWith(prefix, StringComparison.Ordinal));
    }

    [Fact]
    public void ReferencesCore()
    {
        Assert.Contains(Tools.GetReferencedAssemblies(), name => name.Name == "Hasseware.AspNetCore.Components.Bpmn");
    }
}
