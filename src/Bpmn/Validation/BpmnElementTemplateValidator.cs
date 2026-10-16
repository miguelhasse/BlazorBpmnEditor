using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;
using Microsoft.Extensions.Options;

namespace Hasseware.AspNetCore.Components.Bpmn.Validation;

/// <summary>Applied-template constraints, required FEEL and template lifecycle diagnostics (PROF-101 through PROF-105).</summary>
public sealed class BpmnElementTemplateValidator(BpmnTemplateOptions options) : IBpmnProfileValidator
{
    /// <summary>Creates a validator from configured component options.</summary>
    public BpmnElementTemplateValidator(IOptions<BpmnOptions> configured) : this(configured.Value.Templates) { }
    /// <inheritdoc />
    public string Name => "Element templates";
    /// <inheritdoc />
    public IEnumerable<BpmnProfileDiagnostic> Validate(BpmnDocument document)
    {
        foreach (var (id, extras) in BpmnValidator.Extended(document).DistinctBy(item => item.Id))
        {
            if (!BpmnTemplateMarkers.TryReadApplied(extras, out var templateId, out var version, out _)) continue;
            if (BpmnAppliedTemplate.Resolve(document, id, options.Registry).Template is not { } template)
            {
                yield return FromText("PROF-103", BpmnSeverity.Info,
                    BpmnDiagnosticText.Create("PROF-103", "Template not found: '{0}' version {1}.", templateId, version), id);
                continue;
            }
            if (template.Deprecated is { } deprecated)
                yield return FromText("PROF-104", BpmnSeverity.Warning,
                    deprecated.Message is { } custom
                        ? BpmnDiagnosticText.Create("PROF-104.Detail.Custom", "{0}", custom)
                        : BpmnDiagnosticText.Create("PROF-104", "Template '{0}' is deprecated.", template.Name), id);
            var snapshot = BpmnElementInspector.Describe(document, id);
            if (snapshot is not null && BpmnTemplateSelection.Upgrade(snapshot, template, options) is { } newer)
                yield return FromText("PROF-105", BpmnSeverity.Info,
                    BpmnDiagnosticText.Create("PROF-105", "Template '{0}' version {1} is available.", template.Name, newer.Version), id);
            var values = BpmnTemplateValues.Read(document, id, template);
            var active = BpmnTemplateValues.ActiveProperties(template, values);
            foreach (var (property, index) in BpmnTemplateScopes.Flatten(template).Properties.Select((property, index) => (property, index)))
            {
                var key = BpmnTemplateValues.PropertyKey(property, index);
                if (!active.Contains(key) || property.Type == BpmnTemplatePropertyType.Hidden) continue;
                foreach (var error in BpmnTemplatePropertyValidation.LocalizedErrors(property, values[key]))
                    yield return FromText(property.Feel == BpmnTemplateFeel.Required && error.Message.Contains("FEEL", StringComparison.Ordinal)
                        ? "PROF-102" : "PROF-101", BpmnSeverity.Error, error, id);
            }
        }
    }

    private static BpmnProfileDiagnostic FromText(string ruleId, BpmnSeverity severity, BpmnDiagnosticText text, string id) =>
        new(ruleId, severity, text.Message, id)
        {
            MessageResourceKey = text.ResourceKey,
            MessageArguments = text.Arguments,
        };
}
