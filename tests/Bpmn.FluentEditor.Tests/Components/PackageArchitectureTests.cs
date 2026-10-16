using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Hasseware.AspNetCore.Components.Bpmn.Feel;
using Hasseware.FluentUI.AspNetCore.Components.Bpmn;
using Microsoft.JSInterop;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class PackageArchitectureTests
{
    private static readonly Assembly Core = typeof(BpmnCanvas).Assembly;
    public static TheoryData<Type, string, string[]> Packages => new()
    {
        { typeof(BpmnCanvas), "Bpmn", [] },
        { typeof(BpmnEditor), "BpmnEditor", [Core.GetName().Name!] },
        { typeof(FluentBpmnEditor), "FluentUI.BpmnEditor", [Core.GetName().Name!] },
        { typeof(BpmnFeelCodeEditor), "Bpmn.Feel", [Core.GetName().Name!] },
    };

    [Theory, MemberData(nameof(Packages))]
    public void AssemblyAndProjectDependenciesRespectPackageBoundaries(Type marker, string folder, string[] allowed)
    {
        var references = marker.Assembly.GetReferencedAssemblies();
        Assert.Equal(allowed.Order(), references.Where(name => name.Name!.StartsWith("Hasseware.", StringComparison.Ordinal)).Select(name => name.Name!).Order());
        if (marker != typeof(FluentBpmnEditor))
            Assert.DoesNotContain(references, name => name.Name!.StartsWith("Microsoft.FluentUI.", StringComparison.Ordinal));
        else
        {
            Assert.Contains(references, name => name.Name == "Microsoft.FluentUI.AspNetCore.Components");
            Assert.Contains(references, name => name.Name!.StartsWith("Microsoft.FluentUI.AspNetCore.Components.Icons", StringComparison.Ordinal));
        }
        var project = XDocument.Load(Directory.GetFiles(Path.Combine(RepositoryRoot(), "src", folder), "*.csproj").Single());
        var projects = project.Descendants("ProjectReference")
            .Where(element => (string?)element.Attribute("ReferenceOutputAssembly") != "false")
            .Select(element => (string)element.Attribute("Include")!)
            .Where(path => path.EndsWith(".csproj", StringComparison.Ordinal))
            .Select(path => Path.GetFileNameWithoutExtension(path));
        Assert.Equal(allowed.Order(), projects.Order());
        var fluent = project.Descendants("PackageReference").Select(element => (string)element.Attribute("Include")!)
            .Where(name => name.StartsWith("Microsoft.FluentUI.", StringComparison.Ordinal)).Order();
        Assert.Equal(marker == typeof(FluentBpmnEditor)
            ? ["Microsoft.FluentUI.AspNetCore.Components", "Microsoft.FluentUI.AspNetCore.Components.Icons"]
            : [], fluent);
    }

    [Theory]
    [InlineData(typeof(BpmnEditor))]
    [InlineData(typeof(FluentBpmnEditor))]
    [InlineData(typeof(BpmnFeelCodeEditor))]
    public void JavaScriptModuleOwnershipStaysInCanvasAndFeelEditor(Type marker)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var owners = marker.Assembly.GetTypes().Where(type => type.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            .Where(type => type.GetFields(flags).Any(field => typeof(IJSObjectReference).IsAssignableFrom(field.FieldType))
                || type.GetProperties(flags).Any(property => typeof(IJSObjectReference).IsAssignableFrom(property.PropertyType)));
        Assert.Equal(marker == typeof(BpmnFeelCodeEditor) ? new[] { marker } : [], owners);
    }

    [Theory]
    [InlineData(typeof(BpmnEditorSession))]
    [InlineData(typeof(BpmnEditorState))]
    [InlineData(typeof(BpmnCanvasSurface))]
    [InlineData(typeof(BpmnPropertyScope))]
    public void ContextsCannotBeConstructedByConsumers(Type type) => Assert.Empty(type.GetConstructors());

    [Fact]
    public void CoreInternalsAreVisibleOnlyToTestsAndBenchmarks()
    {
        var friends = Core.GetCustomAttributes<InternalsVisibleToAttribute>().Select(attribute => new AssemblyName(attribute.AssemblyName).Name!);
        Assert.All(friends, name => Assert.True(name.EndsWith(".Tests", StringComparison.Ordinal)
            || name == "Hasseware.AspNetCore.Components.Bpmn.Benchmarks", $"Unexpected friend assembly: {name}"));
        Assert.DoesNotContain(typeof(BpmnEditor).Assembly.GetName().Name!, friends);
        Assert.DoesNotContain(typeof(FluentBpmnEditor).Assembly.GetName().Name!, friends);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Bpmn"))) return directory.FullName;
        throw new InvalidOperationException("Cannot locate repository root.");
    }
}
