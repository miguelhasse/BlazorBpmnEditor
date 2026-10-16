using Hasseware.AspNetCore.Components.Bpmn.Storage;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>"Save a copy" names the copy without ever producing a path the store would refuse.</summary>
public sealed class ExternalChangeCopyPathTests
{
    [Theory]
    [InlineData("claims/intake.bpmn", 1, "claims/intake (copy).bpmn")]
    [InlineData("claims/intake.bpmn", 3, "claims/intake (copy 3).bpmn")]
    [InlineData("intake.json", 1, "intake (copy).json")]
    public void NamesTheCopyBesideTheOriginal(string original, int attempt, string expected)
    {
        var copy = BpmnExternalChangeOfferBase.CopyPath(BpmnDocumentPath.Parse(original), attempt);

        Assert.Equal(expected, copy?.ToString());
    }

    [Fact]
    public void ShortensANameThatTheSuffixWouldPushPastTheLimit()
    {
        var stem = new string('a', BpmnDocumentPath.MaxSegmentLength - ".bpmn".Length);

        var copy = BpmnExternalChangeOfferBase.CopyPath(BpmnDocumentPath.Parse($"folder/{stem}.bpmn"), 12);

        Assert.NotNull(copy);
        Assert.True(copy.Value.Name.Length <= BpmnDocumentPath.MaxSegmentLength);
        Assert.EndsWith(" (copy 12).bpmn", copy.Value.Name, StringComparison.Ordinal);
        Assert.Equal("folder", copy.Value.Parent.ToString());
    }

    [Fact]
    public void NeverSplitsASurrogatePair()
    {
        var stem = new string('a', BpmnDocumentPath.MaxSegmentLength - " (copy).bpmn".Length - 1) + "\U00010000";

        var copy = BpmnExternalChangeOfferBase.CopyPath(BpmnDocumentPath.Parse($"{stem}.bpmn"), 1);

        Assert.NotNull(copy);
        Assert.False(char.IsHighSurrogate(copy.Value.Name[^(" (copy).bpmn".Length + 1)]));
    }

    [Fact]
    public void GivesUpWhenNoValidNameFits()
    {
        // The folder path alone leaves no room under the whole-path limit for a longer name.
        var folder = string.Join('/', Enumerable.Repeat(new string('f', 30), 15));
        var original = BpmnDocumentPath.Parse($"{folder}/{new string('n', BpmnDocumentPath.MaxLength - folder.Length - 1 - ".bpmn".Length)}.bpmn");

        Assert.Null(BpmnExternalChangeOfferBase.CopyPath(original, 1));
    }
}
