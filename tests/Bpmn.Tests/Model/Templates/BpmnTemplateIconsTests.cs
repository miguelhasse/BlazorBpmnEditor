using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.TemplateModel;

public sealed class BpmnTemplateIconsTests
{
    private const string Svg = "data:image/svg+xml;base64,AA==";
    private const string Png = "data:image/png;base64,iVBORw0KGgo=";
    private static readonly string ZeebeIcon = $"{{{BpmnTemplateMarkers.ZeebeNamespaceUri}}}{BpmnTemplateMarkers.ModelerTemplateIcon}";
    private static readonly string CamundaIcon = $"{{{BpmnTemplateMarkers.CamundaNamespaceUri}}}{BpmnTemplateMarkers.ModelerTemplateIcon}";

    [Theory]
    [InlineData(Svg)]
    [InlineData(Png)]
    [InlineData("DATA:IMAGE/JPEG;base64,AA==")]
    [InlineData("data:image/svg+xml,%3Csvg/%3E")]
    public void AcceptsInlineImages(string icon) => Assert.True(BpmnTemplateIcons.IsSafe(icon));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/icon.svg")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("data:image/x-icon;base64,AA==")]
    public void RefusesAnythingElse(string? icon) => Assert.False(BpmnTemplateIcons.IsSafe(icon));

    [Fact]
    public void RefusesAnIconAboveTheCap() =>
        Assert.False(BpmnTemplateIcons.IsSafe("data:image/png;base64," + new string('A', BpmnTemplateIcons.MaxLength)));

    [Fact]
    public void PrefersTheCamunda8Attribute()
    {
        var extras = new BpmnElementExtras
        {
            ForeignAttributes = new Dictionary<string, string> { [CamundaIcon] = Png, [ZeebeIcon] = Svg },
        };

        Assert.Equal(Svg, BpmnTemplateIcons.Of(extras));
    }

    [Fact]
    public void FallsBackToCamunda7AndSkipsUnsafeValues()
    {
        var extras = new BpmnElementExtras
        {
            ForeignAttributes = new Dictionary<string, string> { [ZeebeIcon] = "https://x/y.png", [CamundaIcon] = Png },
        };

        Assert.Equal(Png, BpmnTemplateIcons.Of(extras));
        Assert.Null(BpmnTemplateIcons.Of(BpmnElementExtras.None));
    }
}
