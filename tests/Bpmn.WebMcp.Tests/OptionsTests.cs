namespace Hasseware.AspNetCore.Components.Bpmn.WebMcp.Tests;

/// <summary>A limit of nothing would make every list look empty, so it is refused where it is set.</summary>
public sealed class OptionsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RefusesAnElementLimitBelowOne(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BpmnWebMcpOptions { MaxElementsPerCall = value });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void RefusesAnExportLimitBelowOne(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BpmnWebMcpOptions { MaxXmlBytes = value });
    }
}
