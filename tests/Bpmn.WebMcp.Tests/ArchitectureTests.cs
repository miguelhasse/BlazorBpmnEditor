using System.Reflection;

namespace Hasseware.AspNetCore.Components.Bpmn.WebMcp.Tests;

/// <summary>
/// The browser surface depends on Core and the shared tools, never on a UI kit, so it works with any kit and
/// with the plain components.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly Assembly WebMcp = typeof(BpmnWebMcp).Assembly;

    [Theory]
    [InlineData("Hasseware.AspNetCore.Components.BpmnEditor")]
    [InlineData("Hasseware.FluentUI")]
    [InlineData("Microsoft.FluentUI")]
    [InlineData("ModelContextProtocol")]
    public void ReferencesNoKitAndNoServerProtocol(string prefix)
    {
        Assert.DoesNotContain(WebMcp.GetReferencedAssemblies(), name => name.Name!.StartsWith(prefix, StringComparison.Ordinal));
    }

    [Fact]
    public void ReferencesCoreAndTheSharedTools()
    {
        var references = WebMcp.GetReferencedAssemblies().Select(name => name.Name).ToList();

        Assert.Contains("Hasseware.AspNetCore.Components.Bpmn", references);
        Assert.Contains("Hasseware.AspNetCore.Components.Bpmn.Tools", references);
    }

    [Fact]
    public void ShipsTheBrowserModuleAsAStaticWebAsset()
    {
        var module = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Bpmn.WebMcp.Scripts", "dist", "webmcp.js");

        Assert.True(File.Exists(module), "The module is built by the scripts project.");
    }
}
