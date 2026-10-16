using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Icons;
using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Icons;

/// <summary>
/// The C# half of the shared icon contract.
/// </summary>
/// <remarks>
/// The manifest is generated once and consumed by both languages; these assert that the embedded
/// copy is intact, that it is the same file the TypeScript bundle imports, and that the semantic
/// lookups agree with it. That shared origin is what stops stencil and canvas notation drifting.
/// </remarks>
public sealed class BpmnIconRegistryTests
{
    private static readonly BpmnIconRegistry Registry = BpmnIconRegistry.Default;

    [Fact]
    public void LoadsTheEmbeddedManifest()
    {
        Assert.NotEmpty(Registry.Icons);
        Assert.NotEmpty(Registry.Manifest.Assignments);
        Assert.Equal("1.0.0", Registry.Manifest.ManifestVersion);
    }

    [Fact]
    public void CarriesTheLicenceAttribution()
    {
        // The package ships derived glyph data, so the OFL attribution has to travel with it.
        Assert.Equal("OFL-1.1", Registry.Manifest.Source.License);
        Assert.Contains("SIL Open Font License", Registry.Manifest.Source.Attribution, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbedsTheSameFileTheCanvasBundles()
    {
        // Byte-identical, not merely equivalent: one generated artifact, two consumers.
        var onDisk = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "icons", "bpmn-icons.json"));
        using var document = JsonDocument.Parse(onDisk);

        Assert.Equal(
            document.RootElement.GetProperty("contentHash").GetString(),
            Registry.Manifest.ContentHash);
    }

    [Fact]
    public void EveryAssignmentResolvesToAnIcon()
    {
        var unresolved = Registry.Manifest.Assignments
            .Where(assignment => Registry.Find(assignment.IconId) is null)
            .Select(assignment => assignment.IconId)
            .ToList();

        Assert.Empty(unresolved);
    }

    [Fact]
    public void EveryIconHasUsablePathData()
    {
        Assert.All(Registry.Icons, icon =>
        {
            Assert.StartsWith("M", icon.PathData, StringComparison.Ordinal);
            Assert.Equal("0 0 24 24", icon.ViewBox);
            Assert.False(string.IsNullOrWhiteSpace(icon.AccessibilityLabel));
        });
    }

    [Theory]
    [InlineData(BpmnFlowNodeType.ExclusiveGateway, "gateway-xor")]
    [InlineData(BpmnFlowNodeType.InclusiveGateway, "gateway-or")]
    [InlineData(BpmnFlowNodeType.ParallelGateway, "gateway-parallel")]
    [InlineData(BpmnFlowNodeType.ComplexGateway, "gateway-complex")]
    [InlineData(BpmnFlowNodeType.EventBasedGateway, "gateway-eventbased")]
    public void ResolvesGatewayGlyphs(BpmnFlowNodeType type, string iconId)
    {
        var icon = Registry.ForGateway(type);

        Assert.Equal(iconId, icon?.IconId);
        // The editor draws the diamond, so only the inner symbol comes from the manifest.
        Assert.True(icon?.IsInnerSymbol);
    }

    [Theory]
    [InlineData(BpmnFlowNodeType.UserTask, "user")]
    [InlineData(BpmnFlowNodeType.ManualTask, "manual")]
    [InlineData(BpmnFlowNodeType.ServiceTask, "service")]
    [InlineData(BpmnFlowNodeType.ScriptTask, "script")]
    [InlineData(BpmnFlowNodeType.BusinessRuleTask, "business-rule")]
    [InlineData(BpmnFlowNodeType.SendTask, "send")]
    [InlineData(BpmnFlowNodeType.ReceiveTask, "receive")]
    public void ResolvesTaskTypeGlyphs(BpmnFlowNodeType type, string iconId) =>
        Assert.Equal(iconId, Registry.ForTaskType(type)?.IconId);

    [Theory]
    [InlineData(BpmnFlowNodeType.Task)]
    [InlineData(BpmnFlowNodeType.CallActivity)]
    public void ResolvesNoGlyphForATaskThatHasNone(BpmnFlowNodeType type) =>
        Assert.Null(Registry.ForTaskType(type));

    [Theory]
    [InlineData("start", BpmnEventDefinitionType.Message, "start-event-message")]
    [InlineData("startNonInterrupting", BpmnEventDefinitionType.Timer, "start-event-non-interrupting-timer")]
    [InlineData("intermediateCatch", BpmnEventDefinitionType.Signal, "intermediate-event-catch-signal")]
    [InlineData("intermediateThrow", BpmnEventDefinitionType.Message, "intermediate-event-throw-message")]
    [InlineData("end", BpmnEventDefinitionType.Terminate, "end-event-terminate")]
    public void ResolvesEventTriggerGlyphs(string context, BpmnEventDefinitionType definition, string iconId) =>
        Assert.Equal(iconId, Registry.ForEventTrigger(context, definition)?.IconId);

    [Fact]
    public void ResolvesNoGlyphForANoneEvent() =>
        Assert.Null(Registry.ForEventTrigger("start", BpmnEventDefinitionType.None));

    [Fact]
    public void ResolvesNoGlyphForATriggerIllegalInThatContext()
    {
        // Terminate is End-only; the renderer draws nothing rather than inventing a symbol.
        Assert.Null(Registry.ForEventTrigger("start", BpmnEventDefinitionType.Terminate));
    }

    [Theory]
    [InlineData("loop")]
    [InlineData("multiInstanceParallel")]
    [InlineData("multiInstanceSequential")]
    [InlineData("compensation")]
    [InlineData("collapsed")]
    [InlineData("adHoc")]
    public void ResolvesActivityMarkers(string marker)
    {
        var icon = Registry.ForActivityMarker(marker);

        Assert.NotNull(icon);
        // Markers are bare glyphs upstream, so nothing was stripped from them.
        Assert.False(icon.IsInnerSymbol);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "icons"))
                && Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output folder.");
    }
}
