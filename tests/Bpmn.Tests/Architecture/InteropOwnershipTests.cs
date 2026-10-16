using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.JSInterop;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Architecture;

/// <summary>
/// Enforces the ownership rules the interop design depends on.
/// </summary>
/// <remarks>
/// These are the constraints that keep the JavaScript boundary to one lifetime and one owner
/// (implementation plan section 5). They are the kind of rule that decays quietly under review
/// pressure — a second component "just needs" a module reference once — so they are asserted
/// mechanically rather than documented and hoped for.
/// </remarks>
public sealed class InteropOwnershipTests
{
    private static readonly Assembly LibraryAssembly = typeof(BpmnCanvas).Assembly;

    private static readonly string SourceRoot = FindSourceRoot();

    [Fact]
    public void CoreHasNoFluentDependency() =>
        Assert.DoesNotContain(LibraryAssembly.GetReferencedAssemblies(), name => name.Name!.StartsWith("Microsoft.FluentUI.", StringComparison.Ordinal));

    [Fact]
    public void TheTwoSidesAgreeOnTheContractVersion()
    {
        // The literal on each side is the only thing tying a skewed module to a clear error at
        // initialisation, so they are compared rather than trusted to be edited together.
        var script = File.ReadAllText(
            Path.Combine(SourceRoot, "Bpmn.Scripts", "src", "interop", "contract-version.ts"));

        var match = System.Text.RegularExpressions.Regex.Match(
            script,
            @"INTEROP_CONTRACT_VERSION\s*=\s*'([^']+)'");

        Assert.True(match.Success, "contract-version.ts no longer declares INTEROP_CONTRACT_VERSION as a literal.");
        Assert.Equal(Interop.BpmnInteropContract.Version, match.Groups[1].Value);
    }

    [Fact]
    public void OnlyBpmnCanvasHoldsAJavaScriptModuleReference()
    {
        // Async state machines are compiler-generated nested types that hoist locals into fields;
        // they belong to the method that declared them, not to a second owner.
        var owners = LibraryAssembly.GetTypes()
            .Where(type => type.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            .Where(type => type
                .GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(field => typeof(IJSObjectReference).IsAssignableFrom(field.FieldType))
                || type.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Any(property => typeof(IJSObjectReference).IsAssignableFrom(property.PropertyType)))
            .Select(type => type.Name)
            .ToList();

        Assert.Equal([nameof(BpmnCanvas)], owners);
    }

    [Fact]
    public void OnlyTheEventBridgeDeclaresJSInvokableMethods()
    {
        var owners = LibraryAssembly.GetTypes()
            .Where(type => type
                .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(method => method.GetCustomAttribute<JSInvokableAttribute>() is not null))
            .Select(type => type.Name)
            .ToList();

        Assert.Equal([nameof(BpmnCanvasEventBridge)], owners);
    }

    [Fact]
    public void OnlyBpmnCanvasSourceMentionsDotNetObjectReferenceOrModuleImport()
    {
        var offenders = EnumerateLibrarySources()
            .Where(file => Path.GetFileName(file) != "BpmnCanvas.razor.cs")
            .Where(file =>
            {
                var text = File.ReadAllText(file);
                return text.Contains("DotNetObjectReference.Create", StringComparison.Ordinal)
                    || text.Contains("InvokeAsync<IJSObjectReference>", StringComparison.Ordinal);
            })
            .Select(file => Path.GetRelativePath(SourceRoot, file))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryJSInvokableMethodHasAMatchingTypeScriptBridgeMethod()
    {
        // Closes the loop on the 1:1 rule from the C# side: a method added here without its
        // TypeScript counterpart is an event nothing will ever raise.
        var bridgeSource = File.ReadAllText(
            Path.Combine(SourceRoot, "Bpmn.Scripts", "src", "interop", "event-bridge.ts"));

        var invokableNames = typeof(BpmnCanvasEventBridge)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.GetCustomAttribute<JSInvokableAttribute>() is not null)
            .Select(method => method.Name)
            .ToList();

        Assert.NotEmpty(invokableNames);

        foreach (var name in invokableNames)
        {
            Assert.Contains($"'{name}'", bridgeSource, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OnlySerializationReferencesTheInterchangePackages()
    {
        // Bpmn.Model is an execution-interpreter model and cannot represent the specification's
        // section 6 data model, so it is an interchange detail rather than this editor's model of
        // record. Letting it leak into components or the semantic model would reintroduce exactly
        // the fidelity limits the owned model exists to avoid.
        //
        // Matched as whole using-directives and qualified names: this library's own
        // Hasseware.AspNetCore.Components.Bpmn.Model namespace must not trip the check.
        string[] forbidden =
        [
            "using Bpmn.Model;",
            "using Bpmn.Interchange;",
            " Bpmn.Model.",
            " Bpmn.Interchange.",
            "(Bpmn.Model.",
            "(Bpmn.Interchange.",
        ];

        var offenders = EnumerateLibrarySources()
            .Where(file => !Path.GetRelativePath(SourceRoot, file)
                .Contains($"Serialization{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file =>
            {
                var text = File.ReadAllText(file);
                return forbidden.Any(marker => text.Contains(marker, StringComparison.Ordinal));
            })
            .Select(file => Path.GetRelativePath(SourceRoot, file))
            .ToList();

        Assert.Empty(offenders);
    }

    private static IEnumerable<string> EnumerateLibrarySources() =>
        Directory.EnumerateFiles(Path.Combine(SourceRoot, "Bpmn"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>
    /// Walks up from the test binaries to the repository's <c>src</c> folder.
    /// </summary>
    /// <remarks>
    /// These tests read source text, not just metadata, because the rules are about how code is
    /// written rather than what it compiles to.
    /// </remarks>
    private static string FindSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src");
            if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(candidate, "Bpmn")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository 'src' directory from the test output folder.");
    }
}
