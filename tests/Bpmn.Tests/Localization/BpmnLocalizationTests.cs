using System.Globalization;
using Hasseware.AspNetCore.Components.Bpmn.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Localization;

public sealed class BpmnLocalizationTests
{
    [Fact]
    public void AddBpmnComponentsResolvesSeededResources()
    {
        using var provider = new ServiceCollection().AddBpmnComponents().BuildServiceProvider();
        var strings = provider.GetRequiredService<IStringLocalizer<BpmnStrings>>();
        var rules = provider.GetRequiredService<IStringLocalizer<BpmnRuleMessages>>();

        AssertResource(strings["AccessibleTree.AriaLabel"], "Diagram contents");
        AssertResource(strings["LabelEditor.AriaLabel"], "Edit label");
        AssertResource(rules["SF-001"], "Sequence Flow source and target must be a FlowNode (Event, Activity, or Gateway).");
        AssertResource(rules["SF-002"], "Source must not be an End Event.");
        AssertResource(rules["SF-003"], "Target must not be a Start Event.");
    }

    [Theory]
    [InlineData("sv")]
    [InlineData("sv-SE")]
    [InlineData("nl")]
    public void UnsupportedCulturesFallBackToEnglish(string cultureName) =>
        WithUiCulture(cultureName, (strings, rules) =>
        {
            AssertResource(strings["AccessibleTree.AriaLabel"], "Diagram contents");
            AssertResource(rules["SF-002"], "Source must not be an End Event.");
        });

    [Theory]
    [InlineData("de", "Diagramminhalt")]
    [InlineData("de-DE", "Diagramminhalt")]
    [InlineData("zh-Hant", "圖表內容")]
    public void SupportedCulturesResolveTranslations(string cultureName, string expected) =>
        WithUiCulture(cultureName, (strings, rules) =>
        {
            AssertResource(strings["AccessibleTree.AriaLabel"], expected);
            AssertResource(rules["SF-002"], rules["SF-002"].Value);
            Assert.NotEqual("Source must not be an End Event.", rules["SF-002"].Value);
        });

    [Fact]
    public void SatelliteAssemblyContainsMatchingResourceManifests()
    {
        var assembly = typeof(BpmnStrings).Assembly.GetSatelliteAssembly(CultureInfo.GetCultureInfo("de"));

        Assert.Contains("Hasseware.AspNetCore.Components.Bpmn.Resources.BpmnStrings.de.resources", assembly.GetManifestResourceNames());
        Assert.Contains("Hasseware.AspNetCore.Components.Bpmn.Resources.BpmnRuleMessages.de.resources", assembly.GetManifestResourceNames());
    }

    private static void WithUiCulture(string cultureName, Action<IStringLocalizer<BpmnStrings>, IStringLocalizer<BpmnRuleMessages>> assert)
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            using var provider = new ServiceCollection().AddBpmnComponents().BuildServiceProvider();
            assert(provider.GetRequiredService<IStringLocalizer<BpmnStrings>>(), provider.GetRequiredService<IStringLocalizer<BpmnRuleMessages>>());
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    private static void AssertResource(LocalizedString value, string expected)
    {
        Assert.False(value.ResourceNotFound);
        Assert.Equal(expected, value.Value);
    }
}
