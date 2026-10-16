using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// The <c>messageRef</c> of a Send Task or Receive Task: read, written, validated, edited.
/// </summary>
public sealed class TaskMessageRefTests
{
    private const string Xml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="Definitions_1" targetNamespace="http://example.test/bpmn">
          <bpmn:message id="Message_1" name="Order" />
          <bpmn:message id="Message_2" name="Invoice" />
          <bpmn:process id="Process_1" isExecutable="false">
            <bpmn:receiveTask id="Receive_1" name="Wait" messageRef="Message_1" />
            <bpmn:sendTask id="Send_1" name="Notify" messageRef="Message_2" />
            <bpmn:task id="Task_1" name="Plain" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Fact]
    public void ReceiveAndSendTaskMessageRefRoundTrip()
    {
        var document = BpmnXmlReader.Read(Xml).Document;

        Assert.Equal("Message_1", document.FindFlowNode("Receive_1")!.MessageRef);
        Assert.Equal("Message_2", document.FindFlowNode("Send_1")!.MessageRef);
        Assert.Null(document.FindFlowNode("Task_1")!.MessageRef);
        Assert.DoesNotContain("messageRef", document.FindFlowNode("Receive_1")!.Extras.ForeignAttributes.Keys);

        var written = BpmnXmlWriter.Write(document).Xml;
        Assert.Equal(1, Count(written, "messageRef=\"Message_1\""));
        Assert.Equal(1, Count(written, "messageRef=\"Message_2\""));

        var reread = BpmnXmlReader.Read(written).Document;
        Assert.Equal("Message_1", reread.FindFlowNode("Receive_1")!.MessageRef);
        Assert.Equal("Message_2", reread.FindFlowNode("Send_1")!.MessageRef);
    }

    [Fact]
    public void ValidatorFlagsADanglingTaskMessageRef()
    {
        var document = BpmnXmlReader.Read(Xml).Document;
        Assert.DoesNotContain(BpmnValidator.Validate(document).Diagnostics, d => d.RuleId == "D017" && d.ElementId == "Receive_1");

        var dangling = document with
        {
            FlowNodes = [.. document.FlowNodes.Select(node => node.Id == "Receive_1" ? node with { MessageRef = "Message_Missing" } : node)],
        };
        Assert.Contains(BpmnValidator.Validate(dangling).Diagnostics, d => d.RuleId == "D017" && d.ElementId == "Receive_1");

        var wrongKind = document with
        {
            FlowNodes = [.. document.FlowNodes.Select(node => node.Id == "Receive_1" ? node with { MessageRef = "Task_1" } : node)],
        };
        Assert.Contains(BpmnValidator.Validate(wrongKind).Diagnostics, d => d.RuleId == "D017" && d.ElementId == "Receive_1");
    }

    [Fact]
    public void TheReferenceMapFindsTheTaskAsAUsageOfTheMessage()
    {
        var document = BpmnXmlReader.Read(Xml).Document;

        var usage = Assert.Single(BpmnReferenceMap.UsagesOf(document, "Message_1"));
        Assert.Equal(("Receive_1", "messageRef"), (usage.OwnerId, usage.Attribute));
    }

    [Fact]
    public void InspectorOffersMessagesAndTheEditorSetsAndClearsTheReference()
    {
        var document = BpmnXmlReader.Read(Xml).Document;

        var snapshot = BpmnElementInspector.Describe(document, "Receive_1")!;
        var property = Assert.Single(snapshot.Properties, p => p.Name == "messageRef");
        Assert.Equal(BpmnPropertyKind.Choice, property.Kind);
        Assert.Equal("Message_1", property.Value);
        Assert.Equal(["", "Message_1", "Message_2"], property.Choices.Select(choice => choice.Value));
        Assert.Contains(snapshot.References, r => r.Name == "messageRef" && r.TargetId == "Message_1");

        Assert.DoesNotContain(BpmnElementInspector.Describe(document, "Task_1")!.Properties, p => p.Name == "messageRef");

        var set = BpmnDocumentEditor.Apply(document, new UpdatePropertiesCommand("Receive_1", [new("messageRef", "Message_2")]));
        Assert.True(set.IsApplied);
        Assert.Equal("Message_2", set.Document.FindFlowNode("Receive_1")!.MessageRef);

        var cleared = BpmnDocumentEditor.Apply(set.Document, new UpdatePropertiesCommand("Receive_1", [new("messageRef", "")]));
        Assert.True(cleared.IsApplied);
        Assert.Null(cleared.Document.FindFlowNode("Receive_1")!.MessageRef);

        var unknown = BpmnDocumentEditor.Apply(document, new UpdatePropertiesCommand("Receive_1", [new("messageRef", "Nope")]));
        Assert.False(unknown.IsApplied);
    }

    [Fact]
    public void ClearingTheReferenceRemovesItFromAFidelitySave()
    {
        var read = BpmnXmlReader.Read(Xml);
        var cleared = BpmnDocumentEditor.Apply(read.Document, new UpdatePropertiesCommand("Receive_1", [new("messageRef", "")])).Document;

        var written = BpmnXmlWriter.Write(cleared, BpmnWriteOptions.Fidelity).Xml;

        Assert.DoesNotContain("messageRef=\"Message_1\"", written, StringComparison.Ordinal);
        Assert.Null(BpmnXmlReader.Read(written).Document.FindFlowNode("Receive_1")!.MessageRef);
    }

    [Fact]
    public void SwitchingTypeKeepsOrDropsTheReference()
    {
        var document = BpmnXmlReader.Read(Xml).Document;

        var toSend = BpmnDocumentEditor.Apply(document, new SwitchElementTypeCommand("Receive_1", BpmnFlowNodeType.SendTask));
        Assert.True(toSend.IsApplied);
        Assert.Equal("Message_1", toSend.Document.FindFlowNode("Receive_1")!.MessageRef);

        var toTask = BpmnDocumentEditor.Apply(document, new SwitchElementTypeCommand("Receive_1", BpmnFlowNodeType.UserTask));
        Assert.True(toTask.IsApplied);
        Assert.Null(toTask.Document.FindFlowNode("Receive_1")!.MessageRef);
        Assert.Contains(toTask.Diagnostics, d => d.Code == "EDIT-082" && d.Message.Contains("messageRef", StringComparison.Ordinal));
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + 1, StringComparison.Ordinal)) count++;
        return count;
    }
}
