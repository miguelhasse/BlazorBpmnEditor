using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// History remembers who asked for each edit, so an agent can undo only its own work (design doc,
/// "Security and consent").
/// </summary>
public sealed class EditOriginTests
{
    private static readonly BpmnEditOrigin Agent = BpmnEditOrigin.FromAgent("webmcp");

    [Fact]
    public void CommandsAreTheUsersUnlessTheySayOtherwise()
    {
        var command = Rename("x");

        Assert.Equal(BpmnEditOrigin.User, command.Origin);
        Assert.False(command.Origin.IsAgent);
        Assert.Equal("user", command.Origin.ToString());
    }

    [Fact]
    public void RecordsTheOriginOfEachEntry()
    {
        var history = new BpmnEditHistory(Document());

        history.Apply(Rename("Mine"));
        var afterUser = history.UndoOrigin;
        history.Apply(Rename("Theirs") with { Origin = Agent });

        Assert.Equal(BpmnEditOrigin.User, afterUser);
        Assert.Equal(Agent, history.UndoOrigin);
        Assert.Equal("webmcp", history.UndoOrigin?.Agent);
    }

    [Fact]
    public void KeepsTheOriginThroughUndoAndRedo()
    {
        var history = new BpmnEditHistory(Document());
        history.Apply(Rename("Mine"));
        history.Apply(Rename("Theirs") with { Origin = Agent });

        history.Undo();
        var redo = history.RedoOrigin;
        var undo = history.UndoOrigin;
        history.Redo();

        Assert.Equal(Agent, redo);
        Assert.Equal(BpmnEditOrigin.User, undo);
        Assert.Equal(Agent, history.UndoOrigin);
        Assert.Null(history.RedoOrigin);
    }

    [Fact]
    public void ReportsNoOriginWhenThereIsNothingToUndoOrRedo()
    {
        var history = new BpmnEditHistory(Document());

        Assert.Null(history.UndoOrigin);
        Assert.Null(history.RedoOrigin);
    }

    [Fact]
    public void ARefusedCommandRecordsNothing()
    {
        var history = new BpmnEditHistory(Document());

        history.Apply(new UpdatePropertiesCommand("missing", "name", "x") { Origin = Agent });

        Assert.Null(history.UndoOrigin);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RefusesAnUnnamedAgent(string name)
    {
        Assert.Throws<ArgumentException>(() => BpmnEditOrigin.FromAgent(name));
    }

    private static UpdatePropertiesCommand Rename(string name) => new("s", "name", name);

    private static BpmnDocument Document() => BpmnXmlReader.Read(
        """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="d" targetNamespace="t">
          <process id="p"><startEvent id="s" name="Start" /></process>
        </definitions>
        """).Document;
}
