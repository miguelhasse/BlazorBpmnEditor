using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Interop;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Localization;

public sealed class CanvasStringsTests
{
    [Fact]
    public void IdenticalLocalizedConfigurationsCompareEqualDespiteFreshDictionaries()
    {
        var configuration = EditorConfig.CreateDefault();
        var first = configuration with
        {
            Strings = new CanvasStrings(new Dictionary<string, string>
            {
                ["Expand"] = "Expand sub-process",
                ["Type.userTask"] = "User Task",
            }),
        };
        var second = configuration with
        {
            Strings = new CanvasStrings(new Dictionary<string, string>
            {
                ["Type.userTask"] = "User Task",
                ["Expand"] = "Expand sub-process",
            }),
        };

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Single(new HashSet<EditorConfig> { first, second });
    }

    [Theory]
    [InlineData("Expand", "Ouvrir")]
    [InlineData("expand", "Expand sub-process")]
    public void ChangedLocalizedValuesOrOrdinalKeysCompareUnequal(string key, string value)
    {
        var configuration = EditorConfig.CreateDefault();
        var first = configuration with
        {
            Strings = new CanvasStrings(new Dictionary<string, string> { ["Expand"] = "Expand sub-process" }),
        };
        var second = configuration with
        {
            Strings = new CanvasStrings(new Dictionary<string, string> { [key] = value }),
        };

        Assert.NotEqual(first, second);
        Assert.True(first != second);
    }

    [Fact]
    public void SnapshotEqualityCannotChangeWhenCallerMutatesDictionary()
    {
        var values = new Dictionary<string, string> { ["Expand"] = "Expand sub-process" };
        var first = new CanvasStrings(values);
        var second = new CanvasStrings(values);
        var hash = first.GetHashCode();

        values["Expand"] = "Ouvrir";
        values["Type.userTask"] = "Tâche utilisateur";

        Assert.Equal("Expand sub-process", first.Values["Expand"]);
        Assert.Single(first.Values);
        Assert.Equal(second, first);
        Assert.Equal(hash, first.GetHashCode());
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, string>)first.Values)["Expand"] = "Changed");
    }

    [Fact]
    public void MissingEntriesAndNullCompareUnequal()
    {
        var strings = new CanvasStrings(new Dictionary<string, string> { ["Expand"] = "Expand sub-process" });

        Assert.NotEqual(new CanvasStrings(new Dictionary<string, string>()), strings);
        Assert.False(strings.Equals(null));
    }
}
