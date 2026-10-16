namespace Hasseware.AspNetCore.Components.Bpmn.WebMcp.Tests;

/// <summary>A browser refuses two tools with one name, so editors on one page need suffixes of their own.</summary>
public sealed class RegistryTests
{
    [Fact]
    public void GivesTheFirstUnnamedEditorNoSuffix()
    {
        var suffix = new BpmnWebMcpRegistry().Claim(null, out var renamed);

        Assert.Equal(string.Empty, suffix);
        Assert.False(renamed);
    }

    [Fact]
    public void NumbersUnnamedEditorsAfterTheFirst()
    {
        var registry = new BpmnWebMcpRegistry();
        registry.Claim(null, out _);

        var second = registry.Claim(null, out var secondRenamed);
        var third = registry.Claim(null, out var thirdRenamed);

        Assert.Equal(("_2", true), (second, secondRenamed));
        Assert.Equal(("_3", true), (third, thirdRenamed));
    }

    [Fact]
    public void UsesTheNameAnEditorWasGiven()
    {
        var suffix = new BpmnWebMcpRegistry().Claim("left", out var renamed);

        Assert.Equal("_left", suffix);
        Assert.False(renamed);
    }

    [Fact]
    public void NumbersAnInstanceNameThatIsAlreadyTaken()
    {
        var registry = new BpmnWebMcpRegistry();
        registry.Claim("left", out _);

        var suffix = registry.Claim("left", out var renamed);

        Assert.Equal("_left_2", suffix);
        Assert.True(renamed);
    }

    [Fact]
    public void ReusesASuffixOnceItIsReleased()
    {
        var registry = new BpmnWebMcpRegistry();
        var first = registry.Claim(null, out _);
        registry.Claim(null, out _);

        registry.Release(first);

        Assert.Equal(string.Empty, registry.Claim(null, out _));
    }

    [Fact]
    public async Task WaitsForAnEditorThatIsGoingAwayAndTakesItsName()
    {
        var registry = new BpmnWebMcpRegistry();
        var old = registry.Claim(null, out _);
        registry.BeginRelease(old);

        var claiming = registry.ClaimAsync(null, TestContext.Current.CancellationToken);
        Assert.False(claiming.IsCompleted);
        registry.Release(old);

        Assert.Equal((string.Empty, false), await claiming);
    }

    [Fact]
    public async Task NumbersPastAnEditorThatIsStayingEvenWhenAsynchronous()
    {
        var registry = new BpmnWebMcpRegistry();
        registry.Claim(null, out _);

        var (suffix, renamed) = await registry.ClaimAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(("_2", true), (suffix, renamed));
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("bad/name")]
    [InlineData("é")]
    public void RefusesANameAToolNameCannotCarry(string name)
    {
        Assert.Throws<ArgumentException>(() => new BpmnWebMcpRegistry().Claim(name, out _));
    }
}
