using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class MessageAuthoringTests
{
    private const string Xml = """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="Definitions_1" targetNamespace="http://example.test/bpmn">
          <bpmn:process id="Process_1">
            <bpmn:sendTask id="Send_1" />
            <bpmn:intermediateCatchEvent id="Catch_1">
              <bpmn:messageEventDefinition messageRef="Message_1" />
            </bpmn:intermediateCatchEvent>
          </bpmn:process>
          <bpmn:message id="Message_1" name="Order" itemRef="Payload_1">
            <bpmn:documentation>Message notes</bpmn:documentation>
          </bpmn:message>
          <bpmn:itemDefinition id="Payload_1" structureRef="example:Order" />
        </bpmn:definitions>
        """;

    [Fact]
    public void CreatingAndAssigningMessageIsAtomicAndRoundTripsAsRootDefinition()
    {
        var document = BpmnXmlReader.Read(Xml).Document with
        {
            Catalog = BpmnRootCatalog.Empty,
            FlowNodes = [new BpmnFlowNode("Send_1", BpmnFlowNodeType.SendTask, "Process_1")],
        };

        var result = BpmnDocumentEditor.Apply(document, new CompoundCommand(
        [
            new CreateMessageCommand("Message_New", "Order"),
            new UpdatePropertiesCommand("Send_1", "messageRef", "Message_New"),
        ], "Create and assign Message"));

        Assert.True(result.IsApplied, string.Join("; ", result.Rejections.Select(rejection => rejection.Message)));
        Assert.Equal("Message_New", result.Document.FindFlowNode("Send_1")!.MessageRef);
        Assert.Equal("Order", Assert.Single(result.Document.Catalog.Messages).Name);
        Assert.Empty(result.Document.Diagrams);

        var written = BpmnXmlWriter.Write(result.Document).Xml;
        var reread = BpmnXmlReader.Read(written).Document;
        Assert.Equal("Order", Assert.Single(reread.Catalog.Messages).Name);
        Assert.Equal("Message_New", reread.FindFlowNode("Send_1")!.MessageRef);
        Assert.DoesNotContain("BPMNShape", written, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageCreationValidatesIdsDocumentWideButAllowsBlankAndDuplicateNames()
    {
        var document = BpmnXmlReader.Read(Xml).Document;
        var blankName = BpmnDocumentEditor.Apply(document, new CreateMessageCommand("Message_Blank", ""));
        var duplicateName = BpmnDocumentEditor.Apply(blankName.Document, new CreateMessageCommand("Message_Duplicate", "Order"));

        Assert.True(blankName.IsApplied);
        Assert.True(duplicateName.IsApplied);
        Assert.Equal(["Order", "", "Order"], duplicateName.Document.Catalog.Messages.Select(message => message.Name));
        Assert.False(BpmnDocumentEditor.Apply(document, new CreateMessageCommand("Definitions_1")).IsApplied);
        Assert.False(BpmnDocumentEditor.Apply(document, new CreateMessageCommand("not a valid id")).IsApplied);
    }

    [Fact]
    public void NameEditPreservesPayloadAndImportedExtras()
    {
        const string xml = """
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:vendor="urn:vendor" id="Definitions_1" targetNamespace="http://example.test/bpmn">
              <bpmn:message id="Message_1" name="Before" itemRef="Payload_1" vendor:tag="keep">
                <bpmn:documentation>Notes</bpmn:documentation>
                <bpmn:extensionElements><vendor:payload key="keep" /></bpmn:extensionElements>
              </bpmn:message>
              <bpmn:itemDefinition id="Payload_1" structureRef="example:Order" />
            </bpmn:definitions>
            """;

        var document = BpmnXmlReader.Read(xml).Document;
        var original = Assert.Single(document.Catalog.Messages);
        var updated = BpmnDocumentEditor.Apply(document, new UpdateMessageNameCommand("Message_1", "After"));

        Assert.True(updated.IsApplied);
        var message = Assert.Single(updated.Document.Catalog.Messages);
        Assert.Equal("After", message.Name);
        Assert.Equal(original.ItemRef, message.ItemRef);
        Assert.Equal(original.Extras, message.Extras);

        var saved = BpmnXmlWriter.Write(updated.Document, BpmnWriteOptions.Fidelity).Xml;
        var reread = Assert.Single(BpmnXmlReader.Read(saved).Document.Catalog.Messages);
        Assert.Equal("After", reread.Name);
        Assert.Equal(original.ItemRef, reread.ItemRef);
        Assert.Equal(original.Extras, reread.Extras);
    }

    [Fact]
    public void CatalogReportsTaskAndEventUsagesAndBothDeleteCommandsRefuseThem()
    {
        var document = BpmnXmlReader.Read(Xml).Document;
        document = document with
        {
            FlowNodes =
            [
                .. document.FlowNodes.Select(node => node.Id == "Send_1"
                    ? node with { MessageRef = "Message_1" }
                    : node),
            ],
        };
        var inspection = BpmnMessageCatalog.Inspect(document, "Message_1")!;

        Assert.False(inspection.CanDelete);
        Assert.Contains(inspection.Usages, usage => usage.OwnerId == "Catch_1" && usage.Attribute == "message/ref");
        Assert.Contains(inspection.Usages, usage => usage.OwnerId == "Send_1" && usage.Attribute == "messageRef");
        Assert.Contains("Catch_1", inspection.Reason, StringComparison.Ordinal);

        var specific = BpmnDocumentEditor.Apply(document, new DeleteMessageCommand("Message_1"));
        var generic = BpmnDocumentEditor.Apply(document, new DeleteElementsCommand(["Message_1"]));

        Assert.False(specific.IsApplied);
        Assert.False(generic.IsApplied);
        Assert.Contains("Catch_1", Assert.Single(specific.Rejections).Message, StringComparison.Ordinal);
        Assert.Single(generic.Document.Catalog.Messages);

        var withFlow = document with
        {
            MessageFlows = [new BpmnMessageFlow("MessageFlow_1", "Send_1", "Catch_1", MessageRef: "Message_1")],
        };
        var flowInspection = BpmnMessageCatalog.Inspect(withFlow, "Message_1")!;
        Assert.Contains(flowInspection.Usages, usage => usage.OwnerId == "MessageFlow_1" && usage.OwnerKind == "Message Flow");
    }

    [Fact]
    public void OperationQNameUsageResolvesByNamespaceIdentity()
    {
        const string xml = """
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:tns="http://example.test/bpmn"
                              xmlns:other="urn:other" id="Definitions_1" targetNamespace="http://example.test/bpmn">
              <bpmn:message id="Message_1" />
              <bpmn:message id="Message_2" />
              <bpmn:interface id="Interface_1" name="Gateway">
                <bpmn:operation id="Operation_1">
                  <bpmn:inMessageRef>tns:Message_1</bpmn:inMessageRef>
                  <bpmn:outMessageRef>other:Message_2</bpmn:outMessageRef>
                </bpmn:operation>
              </bpmn:interface>
            </bpmn:definitions>
            """;

        var document = BpmnXmlReader.Read(xml).Document;
        var used = BpmnMessageCatalog.Inspect(document, "Message_1")!;
        var unrelated = BpmnMessageCatalog.Inspect(document, "Message_2")!;

        Assert.False(used.CanDelete);
        Assert.Contains(used.Usages, usage =>
            usage.OwnerId == "Operation_1" && usage.OwnerKind == "Interface Operation" && usage.Attribute == "inMessageRef");
        Assert.True(unrelated.CanDelete);

        var deletion = BpmnDocumentEditor.Apply(document, new DeleteMessageCommand("Message_1"));
        Assert.False(deletion.IsApplied);
        Assert.Contains("Operation_1", Assert.Single(deletion.Rejections).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceOnlyQNameReferenceAndOpaqueReferenceBlockDeletion()
    {
        const string xml = """
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:tns="http://example.test/bpmn"
                              xmlns:vendor="urn:vendor"
                              id="Definitions_1" targetNamespace="http://example.test/bpmn">
              <bpmn:message id="Message_1" />
              <bpmn:choreography id="Opaque_1">
                <bpmn:choreographyTask id="OpaqueTask_1" messageRef="tns:Message_1" />
              </bpmn:choreography>
            </bpmn:definitions>
            """;

        var document = BpmnXmlReader.Read(xml).Document;
        var inspection = BpmnMessageCatalog.Inspect(document, "Message_1")!;

        Assert.False(inspection.CanDelete);
        Assert.Contains(inspection.Usages, usage => usage.OwnerId == "OpaqueTask_1" && usage.TargetId == "Message_1");
        Assert.Contains("OpaqueTask_1", inspection.Reason, StringComparison.Ordinal);

        var extensionDocument = BpmnXmlReader.Read("""
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:vendor="urn:vendor"
                              id="Definitions_1" targetNamespace="http://example.test/bpmn">
              <bpmn:message id="Message_1" />
              <bpmn:process id="Process_1">
                <bpmn:task id="Task_1">
                  <bpmn:extensionElements><vendor:correlationKey value="opaque" /></bpmn:extensionElements>
                </bpmn:task>
              </bpmn:process>
            </bpmn:definitions>
            """).Document;

        var opaque = BpmnMessageCatalog.Inspect(extensionDocument, "Message_1")!;
        Assert.False(opaque.CanDelete);
        Assert.Contains("opaque", opaque.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StaleSourceMessageRefDoesNotPreventDeletion()
    {
        var document = BpmnXmlReader.Read(Xml).Document;
        var edited = document with
        {
            FlowNodes =
            [
                .. document.FlowNodes.Select(node => node.Id == "Catch_1"
                    ? node with { EventDefinitions = [] }
                    : node),
            ],
        };

        var inspection = BpmnMessageCatalog.Inspect(edited, "Message_1")!;
        Assert.True(inspection.CanDelete);
        Assert.Empty(inspection.Usages);

        var deleted = BpmnDocumentEditor.Apply(edited, new DeleteMessageCommand("Message_1"));
        Assert.True(deleted.IsApplied);
        Assert.Empty(deleted.Document.Catalog.Messages);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ClearingImportedMessageNameSurvivesFidelityExport(string? name)
    {
        var document = BpmnXmlReader.Read(Xml).Document;
        var result = BpmnDocumentEditor.Apply(document, new UpdateMessageNameCommand("Message_1", name));
        var saved = BpmnXmlReader.Read(BpmnXmlWriter.Write(result.Document, BpmnWriteOptions.Fidelity).Xml).Document;
        Assert.True(string.IsNullOrEmpty(Assert.Single(saved.Catalog.Messages).Name));
    }

    [Theory]
    [InlineData("sendTask")]
    [InlineData("receiveTask")]
    [InlineData("messageFlow")]
    [InlineData("messageEventDefinition")]
    public void PrefixedModeledMessageReferencesBlockDeletion(string owner)
    {
        var reference = owner == "messageEventDefinition"
            ? "<bpmn:intermediateCatchEvent id=\"Event_1\"><bpmn:messageEventDefinition messageRef=\"tns:Message_1\" /></bpmn:intermediateCatchEvent>"
            : $"<bpmn:{owner} id=\"Owner_1\" messageRef=\"tns:Message_1\" sourceRef=\"A\" targetRef=\"B\" />";
        var content = owner == "messageFlow"
            ? $"<bpmn:collaboration id=\"Collaboration_1\">{reference}</bpmn:collaboration>"
            : $"<bpmn:process id=\"Process_1\">{reference}</bpmn:process>";
        var document = BpmnXmlReader.Read($"""
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:tns="urn:test" id="Definitions_1" targetNamespace="urn:test">
              <bpmn:message id="Message_1" />
              {content}
            </bpmn:definitions>
            """).Document;
        Assert.False(BpmnMessageCatalog.Inspect(document, "Message_1")!.CanDelete);
        Assert.False(BpmnDocumentEditor.Apply(document, new DeleteMessageCommand("Message_1")).IsApplied);
    }

    [Fact]
    public void UnprefixedOperationMessageReferenceBlocksDeletion()
    {
        var document = BpmnXmlReader.Read("""
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1" targetNamespace="urn:test">
              <bpmn:message id="Message_1" />
              <bpmn:interface id="Interface_1" name="Contract">
                <bpmn:operation id="Operation_1"><bpmn:inMessageRef>Message_1</bpmn:inMessageRef></bpmn:operation>
              </bpmn:interface>
            </bpmn:definitions>
            """).Document;
        Assert.False(BpmnMessageCatalog.Inspect(document, "Message_1")!.CanDelete);
    }

    [Fact]
    public void ClearingMessageFlowReferenceThenDeletingMessageExportsNoDanglingReference()
    {
        var document = BpmnXmlReader.Read("""
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1" targetNamespace="urn:test">
              <bpmn:message id="Message_1" />
              <bpmn:collaboration id="Collaboration_1">
                <bpmn:participant id="Sender" />
                <bpmn:participant id="Receiver" />
                <bpmn:messageFlow id="Flow_1" sourceRef="Sender" targetRef="Receiver" messageRef="Message_1" />
              </bpmn:collaboration>
            </bpmn:definitions>
            """).Document;
        var cleared = BpmnDocumentEditor.Apply(document, new UpdatePropertiesCommand("Flow_1", "messageRef", null));
        Assert.True(cleared.IsApplied);
        var deleted = BpmnDocumentEditor.Apply(cleared.Document, new DeleteMessageCommand("Message_1"));
        Assert.True(deleted.IsApplied);
        var saved = BpmnXmlReader.Read(BpmnXmlWriter.Write(deleted.Document, BpmnWriteOptions.Fidelity).Xml).Document;
        Assert.Empty(saved.Catalog.Messages);
        Assert.Null(Assert.Single(saved.MessageFlows).MessageRef);
    }

    [Fact]
    public void ClearingEventReferenceThenDeletingMessageExportsNoDanglingReference()
    {
        var document = BpmnXmlReader.Read(Xml).Document;
        document = document with
        {
            FlowNodes = [.. document.FlowNodes.Select(node => node.Id == "Catch_1"
                ? node with { EventDefinitions = [.. node.EventDefinitions.Select(definition => definition with { Ref = null })] }
                : node)],
        };
        var deleted = BpmnDocumentEditor.Apply(document, new DeleteMessageCommand("Message_1"));
        Assert.True(deleted.IsApplied);
        var saved = BpmnXmlReader.Read(BpmnXmlWriter.Write(deleted.Document, BpmnWriteOptions.Fidelity).Xml).Document;
        Assert.Empty(saved.Catalog.Messages);
        Assert.Null(Assert.Single(saved.FindFlowNode("Catch_1")!.EventDefinitions).Ref);
    }

    [Fact]
    public void ForeignMessageReferenceAttributeOnModeledTaskBlocksDeletion()
    {
        var document = BpmnXmlReader.Read("""
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:vendor="urn:vendor" id="Definitions_1" targetNamespace="urn:test">
              <bpmn:message id="Message_1" />
              <bpmn:process id="Process_1"><bpmn:sendTask id="Task_1" vendor:messageRef="Message_1" /></bpmn:process>
            </bpmn:definitions>
            """).Document;
        Assert.False(BpmnMessageCatalog.Inspect(document, "Message_1")!.CanDelete);
        Assert.False(BpmnDocumentEditor.Apply(document, new DeleteMessageCommand("Message_1")).IsApplied);
        Assert.False(BpmnMessageCatalog.Inspect(document with { Source = null }, "Message_1")!.CanDelete);
    }
}
