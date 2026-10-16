using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;
using Hasseware.AspNetCore.Components.Bpmn.Tests.Components;
using Hasseware.AspNetCore.Components.Bpmn.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing.Templates;

public sealed class TemplateIntegrationTests : BunitContext
{
    private const string Uri = BpmnTemplateMarkers.ZeebeNamespaceUri;
    private const string Json = """
        { "$schema":"https://unpkg.com/@camunda/zeebe-element-templates-json-schema/resources/schema.json",
          "id":"sample", "name":"Sample connector", "version":1, "description":"Sample description", "appliesTo":["bpmn:Task"],
          "category":{"id":"connectors","name":"Connectors"}, "engines":{"camunda":">=8.6.0 <9.0.0"},
          "groups":[{"id":"second","label":"Second","tooltip":"Help","openByDefault":false},{"id":"first","label":"First"}],
          "entriesVisible":{"name":false},
          "properties":[
            {"type":"Hidden","value":"worker","binding":{"type":"zeebe:taskDefinition","property":"type"}},
            {"id":"mode","label":"Mode","group":"first","type":"Dropdown","value":"off","choices":[{"name":"Off","value":"off"},{"name":"On","value":"on"}],"binding":{"type":"zeebe:input","name":"mode"}},
            {"id":"conditional","label":"Conditional","group":"second","type":"String","value":"default","condition":{"property":"mode","equals":"on"},"binding":{"type":"zeebe:input","name":"conditional"}},
            {"id":"locked","label":"Locked","group":"second","type":"String","editable":false,"value":"fixed","binding":{"type":"zeebe:property","name":"locked"}},
            {"id":"url","label":"URL","type":"String","value":"bad","description":"Description","tooltip":"Tooltip","placeholder":"https://","constraints":{"notEmpty":true,"minLength":4,"maxLength":20,"pattern":{"value":"^https://","message":"Use HTTPS"}},"binding":{"type":"zeebe:input","name":"url"}},
            {"id":"notes","label":"Notes","type":"Text","value":"text","binding":{"type":"zeebe:input","name":"notes"}},
            {"id":"count","label":"Count","type":"Number","value":3,"binding":{"type":"zeebe:input","name":"count"}},
            {"id":"flag","label":"Flag","type":"Boolean","value":true,"binding":{"type":"zeebe:input","name":"flag"}},
            {"id":"name","label":"Title","type":"String","value":"Templated task","binding":{"type":"property","name":"name"}},
            {"type":"Hidden","generatedValue":{"type":"uuid"},"binding":{"type":"zeebe:taskHeader","key":"request-id"}}
          ] }
        """;

    [Fact]
    public void SnapshotGroupsRespectDeclaredOrderMetadataKindsAndHiddenProperties()
    {
        var (options, template, document) = Setup();
        var snapshot = Describe(document, options);
        Assert.Equal("sample", snapshot.TemplateId);
        Assert.Equal(1, snapshot.TemplateVersion);
        Assert.Equal(["Second", "First", "Custom properties"], snapshot.ExtensionGroups.Take(3).Select(group => group.Label));
        Assert.Equal("Help", snapshot.ExtensionGroups[0].Tooltip);
        Assert.False(snapshot.ExtensionGroups[0].OpenByDefault);
        var entries = Entries(snapshot);
        Assert.DoesNotContain(entries, entry => entry.Kind == BpmnPropertyEntryKind.Hidden || entry.Id == "template:#0" || entry.Id == "template:#9");
        Assert.DoesNotContain(entries, entry => entry.Id == "template:conditional");
        Assert.Equal(BpmnPropertyEntryKind.ReadOnly, Find(snapshot, "locked").Kind);
        Assert.Equal(BpmnPropertyEntryKind.TextArea, Find(snapshot, "notes").Kind);
        Assert.Equal(BpmnPropertyEntryKind.Number, Find(snapshot, "count").Kind);
        Assert.Equal(BpmnPropertyEntryKind.Checkbox, Find(snapshot, "flag").Kind);
        Assert.Equal("true", Find(snapshot, "flag").Value);
        Assert.Equal("3", Find(snapshot, "count").Value);
        Assert.Equal("Off", Find(snapshot, "mode").Choices[0].Label);
        var url = Find(snapshot, "url");
        Assert.Equal("Description", url.Description);
        Assert.Equal("Tooltip", url.Tooltip);
        Assert.Equal("https://", url.Placeholder);
        Assert.Contains("Use HTTPS", url.Error!, StringComparison.Ordinal);
        Assert.True(url.Constraints!.NotEmpty);
        Assert.Equal("template", url.Binding!.Type);
        Assert.Equal("url", url.Binding.Parameters["propertyKey"]);
        Assert.Equal("1", url.Binding.Parameters["version"]);
        Assert.All(Describe(document, options, true).ExtensionGroups.SelectMany(group => group.Entries), entry => Assert.True(entry.IsReadOnly));
        Assert.Equal(template.Properties.Count - 3, entries.Count(entry => entry.Binding?.Type == "template"));
    }

    [Fact]
    public void ConditionsRefreshAfterFactoryCommandAndSchemaRowsRemainUnclaimed()
    {
        var (options, template, document) = Setup();
        var entry = Find(Describe(document, options), "mode");
        var command = BpmnPropertyEntryCommands.ToCommand("Task", entry, "on", factories: options.Properties.BindingCommandFactories);
        Assert.IsType<SetTemplatePropertyCommand>(command);
        document = Apply(document, command!);
        Assert.Equal("default", Find(Describe(document, options), "conditional").Value);
        document = Apply(document, new SetTemplatePropertyCommand("Task", template, "mode", "off"));
        Assert.DoesNotContain(Entries(Describe(document, options)), item => item.Id == "template:conditional");
    }

    [Fact]
    public void SchemaDedupePreservesUnclaimedScalarsRowsAndOpaqueAttributes()
    {
        var (options, template, document) = Setup();
        document = Apply(document, new SetExtensionValueCommand("Task", $"{{{Uri}}}taskDefinition@retries", "5"));
        document = Apply(document, new SetExtensionValueCommand("Task", $"{{{Uri}}}taskDefinition@vendorField", "keep"));
        document = Apply(document, new SetExtensionValueCommand("Task", $"{{{Uri}}}ioMapping/{{{Uri}}}input[target=url]@vendorField", "keep-row"));
        document = Apply(document, new SetExtensionValueCommand("Task", $"{{{Uri}}}ioMapping/{{{Uri}}}input[target=other]@source", "untouched"));
        var snapshot = Describe(document, options);
        var schema = snapshot.ExtensionGroups.Where(group => !group.Id.StartsWith("template:", StringComparison.Ordinal)).SelectMany(group => group.Entries).ToArray();
        Assert.DoesNotContain(schema, entry => entry.Id == "type" || entry.Kind == BpmnPropertyEntryKind.List && entry.Id == "inputs");
        Assert.Contains(schema, entry => entry.Id == "retries" && entry.Value == "5");
        Assert.Contains(schema, entry => entry.Value == "untouched");
        Assert.Contains(snapshot.ExtensionProperties, entry => entry.Value == "keep");
        Assert.Contains(snapshot.ExtensionProperties, entry => entry.Value == "keep-row");
        Assert.DoesNotContain(snapshot.ExtensionProperties, entry => entry.Value is "worker" or "bad" or "untouched");
        var unclaimed = schema.Single(entry => entry.Value == "untouched");
        document = Apply(document, BpmnPropertyEntryCommands.ToCommand("Task", unclaimed, "changed")!);
        Assert.Equal("bad", BpmnTemplateValues.Read(document, "Task", template)["url"]);
    }

    [Fact]
    public void EntriesVisibleIsOptInAndOnlyHidesBoundStandardEntries()
    {
        var (options, _, document) = Setup();
        Assert.Contains(Describe(document, options).Properties, property => property.Name == "name");
        options.Templates.HonorEntriesVisible = true;
        Assert.DoesNotContain(Describe(document, options).Properties, property => property.Name == "name");
        var filtered = Describe(document, options).Properties.Select(property => property.Name).ToArray();
        options.Templates.HonorEntriesVisible = false;
        Assert.Equal(Describe(document, options).Properties.Where(property => property.Name != "name").Select(property => property.Name), filtered);
    }

    [Fact]
    public void MissingTemplateShowsIdentityNoticeAndRetainsSchemaGroups()
    {
        var (_, _, document) = Setup();
        var options = new BpmnOptions(); options.Extensions.AddZeebe();
        var snapshot = Describe(document, options);
        var missing = snapshot.ExtensionGroups[0];
        Assert.Equal("Template", missing.Label);
        Assert.All(missing.Entries, entry => Assert.Equal(BpmnPropertyEntryKind.ReadOnly, entry.Kind));
        Assert.Contains(missing.Entries, entry => entry.Value == "sample");
        Assert.Contains(missing.Entries, entry => entry.Value == "Template not found");
        Assert.Contains(snapshot.ExtensionGroups, group => group.Id == "task-definition");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnversionedMarkersResolveOnlyExactIdentityAcrossPanelFactoryValidatorAndChooser(bool registerUnversioned)
    {
        var (options, versioned, document) = Setup();
        Services.AddSingleton<IOptions<BpmnOptions>>(Options.Create(options));
        var unversioned = versioned with
        {
            Version = null,
            Name = "Unversioned connector",
            Properties = [versioned.Properties.Single(p => p.Id == "mode") with { Label = "Unversioned mode" }]
        };
        document = Apply(document, new ApplyElementTemplateCommand("Task", unversioned));
        if (registerUnversioned) options.Templates.Registry.Add(unversioned);
        var resolved = BpmnAppliedTemplate.Resolve(document, "Task", options.Templates.Registry);
        Assert.Equal(!registerUnversioned, resolved.IsMissing);
        Assert.Null(resolved.Version);
        var snapshot = Describe(document, options);
        var diagnostics = new BpmnElementTemplateValidator(options.Templates).Validate(document).ToArray();
        var chooser = this.RenderSeeded<BpmnTemplateChooser>(seed => seed.Snapshots([snapshot]), p => p.Add(c => c.Element, snapshot));
        if (registerUnversioned)
        {
            Assert.Same(unversioned, resolved.Template);
            var entry = Find(snapshot, "mode");
            Assert.Equal("Unversioned mode", entry.Label);
            var command = Assert.IsType<SetTemplatePropertyCommand>(BpmnPropertyEntryCommands.ToCommand("Task", entry, "on", factories: options.Properties.BindingCommandFactories));
            Assert.Same(unversioned, command.Template);
            Assert.DoesNotContain(diagnostics, d => d.RuleId is "PROF-101" or "PROF-103");
            Assert.Contains(diagnostics, d => d.RuleId == "PROF-105");
            Assert.Single(chooser.FindAll(".bpmn-templates__update"));
        }
        else
        {
            Assert.Null(resolved.Template);
            Assert.Contains(Entries(snapshot), entry => entry.Value == "Template not found");
            Assert.Contains(diagnostics, d => d.RuleId == "PROF-103");
            Assert.DoesNotContain(diagnostics, d => d.RuleId == "PROF-105");
            Assert.Contains("Template not found", chooser.Markup, StringComparison.Ordinal);
            Assert.Empty(chooser.FindAll(".bpmn-templates__update"));
            Assert.Empty(chooser.FindAll(".bpmn-templates__remove"));
        }
    }

    [Fact]
    public void ChooserFiltersApplicableEngineCompatibleTemplatesSearchesAndEmitsApply()
    {
        var (options, template, _) = Setup();
        Services.AddSingleton<IOptions<BpmnOptions>>(Options.Create(options));
        options.Templates.Engines["camunda"] = "8.6.0";
        options.Templates.Registry.Add(template with { Id = "process", Name = "Process only", AppliesTo = ["bpmn:Process"] });
        options.Templates.Registry.Add(template with { Id = "newengine", Name = "New engine", Engines = new Dictionary<string, string> { ["camunda"] = ">=9.0.0" } });
        BpmnEditCommand? sent = null;
        var chooser = this.RenderSeeded<BpmnTemplateChooser>(seed => seed.Snapshots([Bare()]).OnCommand(command => sent = command), p => p.Add(c => c.Element, Bare()));
        chooser.Find(".bpmn-templates__select").Click();
        Assert.Single(chooser.FindAll("[role=option]"));
        Assert.Contains("Connectors", chooser.Markup, StringComparison.Ordinal);
        Assert.Contains("Sample description", chooser.Markup, StringComparison.Ordinal);
        chooser.Find("input[type=search]").Input("missing");
        Assert.Empty(chooser.FindAll("[role=option]"));
        chooser.Find("input[type=search]").Input("connector");
        chooser.Find("[role=option]").Click();
        var apply = Assert.IsType<ApplyElementTemplateCommand>(sent);
        Assert.Same(template, apply.Template);
        Assert.Equal("Task", apply.ElementId);
        Assert.Same(options.Templates.Registry, apply.Registry);
    }

    [Fact]
    public void ChooserEmitsUnlinkRemoveAndCompatibleUpgradeAndRendersDataIcon()
    {
        var (options, template, document) = Setup();
        Services.AddSingleton<IOptions<BpmnOptions>>(Options.Create(options));
        options.Templates.Engines["camunda"] = "8.6.0";
        options.Templates.Registry.Add(template with { Version = 2 });
        options.Templates.Registry.Add(template with { Version = 3, Engines = new Dictionary<string, string> { ["camunda"] = ">=9.0.0" } });
        var snapshot = Describe(document, options);
        BpmnEditCommand? sent = null;
        var chooser = this.RenderSeeded<BpmnTemplateChooser>(seed => seed.Snapshots([snapshot]).OnCommand(command => sent = command), p => p.Add(c => c.Element, snapshot));
        Assert.Contains("Version 1", chooser.Markup, StringComparison.Ordinal);
        chooser.Find(".bpmn-templates__unlink").Click(); Assert.IsType<UnlinkElementTemplateCommand>(sent);
        chooser.Find(".bpmn-templates__remove").Click(); Assert.IsType<RemoveElementTemplateCommand>(sent);
        chooser.Find(".bpmn-templates__update").Click();
        Assert.Equal(2, Assert.IsType<UpgradeElementTemplateCommand>(sent).NewTemplate.Version);
        var iconOptions = new BpmnOptions(); iconOptions.Templates.Registry.Add(template with { Icon = "data:image/png;base64,aGVsbG8=" });
        using var iconContext = new BunitContext();
        iconContext.Services.AddSingleton<IOptions<BpmnOptions>>(Options.Create(iconOptions));
        var iconChooser = iconContext.RenderSeeded<BpmnTemplateChooser>(seed => seed.Snapshots([snapshot]), p => p.Add(c => c.Element, snapshot));
        Assert.Single(iconChooser.FindAll("img"));
    }

    [Fact]
    public void ReadOnlyCascadeDisablesChooserActionsAndPanelForwardsCommands()
    {
        var (options, _, document) = Setup();
        Services.AddSingleton<IOptions<BpmnOptions>>(Options.Create(options));
        var sent = new List<BpmnEditCommand>();
        var panel = this.RenderSeeded<BpmnPropertyPanel>(seed => seed.Snapshots([Describe(document, options)]).OnCommand(command => sent.Add(command)));
        panel.Find(".bpmn-templates__unlink").Click();
        Assert.IsType<UnlinkElementTemplateCommand>(Assert.Single(sent));
        var locked = this.RenderSeeded<BpmnTemplateChooser>(seed => seed.Snapshots([Describe(document, options)]).ReadOnly().OnCommand(command => sent.Add(command)), p => p.Add(c => c.Element, Describe(document, options)));
        Assert.All(locked.FindAll("button"), button => Assert.True(button.HasAttribute("disabled")));
        locked.Find(".bpmn-templates__unlink").Click();
        Assert.Single(sent);
    }

    [Fact]
    public void PanelPutsTemplateGroupsBetweenGeneralAndSchemaAndAvoidsMultiSelectLeakage()
    {
        var (options, _, document) = Setup();
        Services.AddSingleton<IOptions<BpmnOptions>>(Options.Create(options));
        var snapshot = Describe(document, options);
        var panel = this.RenderSeeded<BpmnPropertyPanel>(seed => seed.Snapshots([snapshot]));
        Assert.Equal(["general", "template:second", "template:first", "template:custom"], panel.FindAll("details").Take(4).Select(group => group.GetAttribute("data-group-id")));
        var multi = this.RenderSeeded<BpmnPropertyPanel>(seed => seed.Snapshots([snapshot, snapshot with { ElementId = "Other" }]));
        Assert.Empty(multi.FindAll(".bpmn-templates__row"));
        Assert.DoesNotContain("Custom properties", multi.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ProfileReportsConstraintsFeelMissingDeprecatedAndCompatibleUpgrade()
    {
        var (options, template, document) = Setup();
        var diagnostics = new BpmnElementTemplateValidator(options.Templates).Validate(document).ToArray();
        Assert.Contains(diagnostics, finding => finding.RuleId == "PROF-101" && finding.Message == "Use HTTPS");
        var required = template with { Id = "required", Properties = [new() { Id = "expression", Feel = BpmnTemplateFeel.Required, Binding = new("property", new Dictionary<string, string> { ["name"] = "name" }) }], Deprecated = new("Retired") };
        options.Templates.Registry.Add(required);
        options.Templates.Registry.Add(required with { Version = 2, Deprecated = null });
        document = Apply(document, new ApplyElementTemplateCommand("Task", required));
        document = Apply(document, new UpdatePropertiesCommand("Task", "name", "literal"));
        diagnostics = new BpmnElementTemplateValidator(options.Templates).Validate(document).ToArray();
        Assert.Contains(diagnostics, finding => finding.RuleId == "PROF-102" && finding.Severity == BpmnSeverity.Error);
        Assert.Contains(diagnostics, finding => finding.RuleId == "PROF-104" && finding.Severity == BpmnSeverity.Warning);
        Assert.Contains(diagnostics, finding => finding.RuleId == "PROF-105" && finding.Severity == BpmnSeverity.Info);
        Assert.Contains(new BpmnElementTemplateValidator(new BpmnTemplateOptions()).Validate(document), finding => finding.RuleId == "PROF-103" && finding.Severity == BpmnSeverity.Info);
    }

    [Fact]
    public void ProfileSkipsInactiveAndHiddenPropertiesAndUsesBoundedPatterns()
    {
        var (options, template, document) = Setup();
        var properties = template.Properties.Select(property => property.Id == "conditional" || property.Type == BpmnTemplatePropertyType.Hidden ? property with { Constraints = new(NotEmpty: true, MinLength: 500) } : property).ToArray();
        var changed = template with { Id = "validation", Properties = properties };
        var isolated = new BpmnTemplateOptions(); isolated.Registry.Add(changed);
        document = Apply(document, new ApplyElementTemplateCommand("Task", changed));
        var findings = new BpmnElementTemplateValidator(isolated).Validate(document).ToArray();
        Assert.DoesNotContain(findings, finding => finding.Message.Contains("500", StringComparison.Ordinal));
        var errors = BpmnTemplatePropertyValidation.Errors(new() { Constraints = new(Pattern: new("[")) }, "value");
        Assert.Single(errors);
        Assert.Contains("invalid", errors[0], StringComparison.Ordinal);
        document = Apply(document, new SetTemplatePropertyCommand("Task", changed, "mode", "on"));
        Assert.Contains(new BpmnElementTemplateValidator(isolated).Validate(document), finding => finding.Message.Contains("500", StringComparison.Ordinal));
    }

    [Fact]
    public void PasteRegeneratesUuidPreservesMarkersAndForeignExtrasAndUndoIsExact()
    {
        var (options, template, document) = Setup();
        var node = document.FlowNodes[0];
        var foreign = new Dictionary<string, string>(node.Extras.ForeignAttributes) { ["{urn:vendor}flag"] = "keep" };
        var opaque = new BpmnExtensionElement("urn:vendor", "data", new Dictionary<string, string> { ["value"] = "keep" }, []);
        document = document with { FlowNodes = [node with { Extras = node.Extras with { ForeignAttributes = foreign, Extensions = [.. node.Extras.Extensions, opaque] } }] };
        var originalUuid = BpmnTemplateValues.Read(document, "Task", template)["#9"];
        var history = new BpmnEditHistory(document, EditingOptions.Default with { PreventCollisions = false, PasteTransforms = [new BpmnTemplatePasteTransform(options.Templates.Registry)] });
        var payload = BpmnClipboard.Copy(document, ["Task"]);
        var result = history.Apply(new PasteCommand(payload, "Process", new DiPoint(20, 20)));
        Assert.True(result.IsApplied, string.Join(';', result.Diagnostics.Select(d => d.Message)));
        var pasted = result.Document.FindFlowNode(Assert.Single(result.CreatedIds))!;
        var uuid = BpmnTemplateValues.Read(result.Document, pasted.Id, template)["#9"];
        Assert.True(Guid.TryParse(uuid, out _)); Assert.NotEqual(originalUuid, uuid);
        Assert.Equal("keep", pasted.Extras.ForeignAttributes["{urn:vendor}flag"]);
        Assert.Contains(opaque, pasted.Extras.Extensions);
        Assert.Equal("sample", BpmnAppliedTemplate.Resolve(result.Document, pasted.Id, options.Templates.Registry).Id);
        var next = history.Apply(new PasteCommand(payload, "Process", new DiPoint(40, 40)));
        Assert.NotEqual(uuid, BpmnTemplateValues.Read(next.Document, Assert.Single(next.CreatedIds), template)["#9"]);
        history.Undo(); history.Undo(); Assert.Same(document, history.Document);
    }

    [Fact]
    public void PasteGeneratedMessageUuidClonesReferencedMessageAndUndoRedoKeepsReference()
    {
        var template = new BpmnElementTemplate
        {
            Id = "generated-message",
            AppliesTo = ["bpmn:FlowNode"],
            Properties =
            [
                new BpmnTemplateProperty
                {
                    Id = "message-name",
                    GeneratedValue = new BpmnTemplateGeneratedValue("uuid"),
                    Binding = new BpmnTemplateBinding("bpmn:Message#property", new Dictionary<string, string> { ["name"] = "name" })
                }
            ]
        };
        var registry = new BpmnElementTemplateRegistry();
        registry.Add(template);
        var originalMessage = new BpmnMessage("Message", "original")
        {
            Extras = new() { ForeignAttributes = new Dictionary<string, string> { ["{urn:vendor}flag"] = "keep" } }
        };
        var document = new BpmnDocument
        {
            Definitions = new("Definitions", "urn:test"),
            Processes = [new("Process")],
            FlowNodes = [new BpmnFlowNode("Receive", BpmnFlowNodeType.ReceiveTask, "Process") { MessageRef = originalMessage.Id }],
            Catalog = new BpmnRootCatalog { Messages = [originalMessage] }
        };
        document = Apply(document, new ApplyElementTemplateCommand("Receive", template));
        var originalUuid = document.Catalog.Messages.Single(message => message.Id == originalMessage.Id).Name;
        var options = EditingOptions.Default with
        {
            PreventCollisions = false,
            PasteTransforms = [new BpmnTemplatePasteTransform(registry)]
        };
        var history = new BpmnEditHistory(document, options);
        var payload = BpmnClipboard.Copy(document, ["Receive"]);

        var result = history.Apply(new PasteCommand(payload, "Process", new DiPoint(20, 20)));
        Assert.True(result.IsApplied, string.Join(';', result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var pasted = result.Document.FindFlowNode(Assert.Single(result.CreatedIds))!;
        var copiedMessage = Assert.Single(result.Document.Catalog.Messages, message => message.Id == pasted.MessageRef);

        Assert.Equal(originalMessage.Id, document.FindFlowNode("Receive")!.MessageRef);
        Assert.Equal(originalMessage.Id, result.Document.FindFlowNode("Receive")!.MessageRef);
        Assert.Equal(originalUuid, result.Document.Catalog.Messages.Single(message => message.Id == originalMessage.Id).Name);
        Assert.NotEqual(originalMessage.Id, pasted.MessageRef);
        Assert.NotEqual(originalUuid, copiedMessage.Name);
        Assert.True(Guid.TryParse(copiedMessage.Name, out _));
        Assert.Equal("keep", copiedMessage.Extras.ForeignAttributes["{urn:vendor}flag"]);

        history.Undo();
        Assert.Same(document, history.Document);
        var redone = history.Redo();
        var redoneNode = redone.FindFlowNode(pasted.Id)!;
        Assert.Equal(pasted.MessageRef, redoneNode.MessageRef);
        Assert.Equal(copiedMessage.Name, redone.Catalog.Messages.Single(message => message.Id == redoneNode.MessageRef).Name);
    }

    [Fact]
    public void TemplateBindingFactoryIsRegisteredInDefaultOptionsAndServices()
    {
        Services.AddBpmnEditor(options => options.Templates.AddTemplates(Json));
        Assert.Contains(Services.GetServices<IBpmnBindingCommandFactory>(), factory => factory.BindingType == "template");
        Assert.Contains(Services.GetServices<IBpmnProfileValidator>(), validator => validator is BpmnElementTemplateValidator);
        Assert.Contains(Services.GetRequiredService<IOptions<BpmnOptions>>().Value.Properties.BindingCommandFactories, factory => factory.BindingType == "template");
    }

    [Fact]
    public void InlineConstraintsCoverEmptyLengthPatternAndRequiredFeel()
    {
        var property = new BpmnTemplateProperty
        {
            Label = "Expression",
            Feel = BpmnTemplateFeel.Required,
            Constraints = new(NotEmpty: true, MinLength: 3, MaxLength: 5, Pattern: new("^=", "Start with equals"))
        };
        Assert.Equal(3, BpmnTemplatePropertyValidation.Errors(property, "").Count);
        Assert.Contains("at most", Assert.Single(BpmnTemplatePropertyValidation.Errors(property, "=abcdef")), StringComparison.Ordinal);
        Assert.Contains("Start with equals", BpmnTemplatePropertyValidation.Errors(property, "abc"));
        Assert.Empty(BpmnTemplatePropertyValidation.Errors(property, "=abc"));
    }

    [Fact]
    public void TemplateSnapshotWorksWithoutExtensionSchemaRegistration()
    {
        var (_, template, document) = Setup();
        var options = new BpmnOptions(); options.Templates.Registry.Add(template);
        var snapshot = Describe(document, options);
        Assert.Equal("sample", snapshot.TemplateId);
        Assert.Contains(snapshot.ExtensionGroups, group => group.Id == "template:custom");
        Assert.Empty(snapshot.ExtensionProperties);
    }

    [Fact]
    public void PasteWithUnregisteredTemplatePreservesGeneratedValueAndRefusedTransformIsAtomic()
    {
        var (_, template, document) = Setup();
        var payload = BpmnClipboard.Copy(document, ["Task"]);
        var options = EditingOptions.Default with { PreventCollisions = false, PasteTransforms = [new BpmnTemplatePasteTransform(new())] };
        var result = BpmnDocumentEditor.Apply(document, new PasteCommand(payload, "Process", new DiPoint(0, 0)), options);
        Assert.True(result.IsApplied);
        Assert.Equal(BpmnTemplateValues.Read(document, "Task", template)["#9"], BpmnTemplateValues.Read(result.Document, Assert.Single(result.CreatedIds), template)["#9"]);
        var rejected = BpmnDocumentEditor.Apply(document, new PasteCommand(payload, "Process", new DiPoint(0, 0)), options with { PasteTransforms = [new RefusedPaste()] });
        Assert.False(rejected.IsApplied); Assert.Same(document, rejected.Document);
        Assert.Contains(rejected.Diagnostics, finding => finding.Code == "EDIT-111");
    }

    [Fact]
    public async Task EditorThreadsTemplateOptionsRunsProfileAndAddsPasteTransformToHistory()
    {
        var (_, template, document) = Setup();
        Services.AddBpmnEditor(options => { options.Extensions.AddZeebe(); options.Templates.AddTemplates(Json); });
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule(BpmnInteropContract.ModulePath);
        var canvas = module.SetupModule("initialize", _ => true);
        canvas.Setup<CanvasInitResult>("getInitResult", _ => true).SetResult(new CanvasInitResult("templates-test", BpmnInteropContract.Version, 1, true, null));
        var editor = Render<BpmnEditor>(p => p.Add(c => c.Document, document));
        Assert.Equal("sample", editor.Instance.Describe("Task")!.TemplateId);
        Assert.Contains(editor.Instance.Validation.Diagnostics, finding => finding.RuleId == "PROF-101");
        BpmnEditResult? result = null;
        await editor.InvokeAsync(async () => result = await editor.Instance.ApplyPropertyCommandAsync(new PasteCommand(BpmnClipboard.Copy(document, ["Task"]), "Process", new DiPoint(20, 20))));
        Assert.True(result!.IsApplied);
        Assert.Single(JSInterop.Invocations["initialize"]);
        Assert.NotEqual(BpmnTemplateValues.Read(document, "Task", template)["#9"], BpmnTemplateValues.Read(result.Document, Assert.Single(result.CreatedIds), template)["#9"]);
    }

    private sealed class RefusedPaste : IBpmnPasteTransform
    {
        public BpmnDocument Transform(BpmnDocument document, IReadOnlyList<string> pastedIds, EditingOptions options) => throw new InvalidOperationException("Refused by host");
    }

    private static (BpmnOptions Options, BpmnElementTemplate Template, BpmnDocument Document) Setup()
    {
        var options = new BpmnOptions(); options.Extensions.AddZeebe();
        var loaded = options.Templates.AddTemplates(Json); Assert.Empty(loaded.Errors);
        var template = Assert.Single(loaded.Templates);
        var document = new BpmnDocument { Definitions = new("Definitions", "urn:test"), Processes = [new("Process")], FlowNodes = [new("Task", BpmnFlowNodeType.ServiceTask, "Process")] };
        return (options, template, Apply(document, new ApplyElementTemplateCommand("Task", template)));
    }
    private static BpmnDocument Apply(BpmnDocument document, BpmnEditCommand command)
    {
        var result = BpmnDocumentEditor.Apply(document, command); Assert.True(result.IsApplied, string.Join(';', result.Diagnostics.Select(d => d.Message))); return result.Document;
    }
    private static BpmnElementSnapshot Describe(BpmnDocument document, BpmnOptions options, bool readOnly = false) => BpmnElementInspector.Describe(document, "Task", readOnly, options.Extensions, options.Templates)!;
    private static BpmnElementSnapshot Bare() => new() { ElementId = "Task", ElementType = "serviceTask", Category = BpmnElementCategory.Activity };
    private static BpmnPropertyEntry[] Entries(BpmnElementSnapshot snapshot) => snapshot.ExtensionGroups.SelectMany(group => group.Entries).ToArray();
    private static BpmnPropertyEntry Find(BpmnElementSnapshot snapshot, string id) => Entries(snapshot).Single(entry => entry.Id == "template:" + id);
}
