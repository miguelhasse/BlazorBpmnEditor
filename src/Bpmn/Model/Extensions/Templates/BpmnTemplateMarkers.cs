namespace Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;

/// <summary>Names used to persist applied template identity as foreign BPMN attributes.</summary>
public static class BpmnTemplateMarkers
{
    /// <summary>Camunda 8 Zeebe extension namespace.</summary>
    public const string ZeebeNamespaceUri = "http://camunda.org/schema/zeebe/1.0";
    /// <summary>Camunda 7 extension namespace.</summary>
    public const string CamundaNamespaceUri = "http://camunda.org/schema/1.0/bpmn";
    /// <summary>Applied-template id attribute local name.</summary>
    public const string ModelerTemplate = "modelerTemplate";
    /// <summary>Applied-template version attribute local name.</summary>
    public const string ModelerTemplateVersion = "modelerTemplateVersion";
    /// <summary>Applied-template icon attribute local name.</summary>
    public const string ModelerTemplateIcon = "modelerTemplateIcon";

    /// <summary>Reads template id, optional version, and flavor from imported foreign attributes.</summary>
    public static bool TryReadApplied(BpmnElementExtras extras, out string id, out int? version, out BpmnTemplateFlavor flavor)
    {
        ArgumentNullException.ThrowIfNull(extras);
        foreach (var (uri, candidateFlavor) in new[]
        {
            (ZeebeNamespaceUri, BpmnTemplateFlavor.Zeebe), (CamundaNamespaceUri, BpmnTemplateFlavor.Camunda7)
        })
        {
            if (!extras.ForeignAttributes.TryGetValue($"{{{uri}}}{ModelerTemplate}", out var foundId) || string.IsNullOrWhiteSpace(foundId)) continue;
            id = foundId;
            extras.ForeignAttributes.TryGetValue($"{{{uri}}}{ModelerTemplateVersion}", out var versionText);
            version = int.TryParse(versionText, out var parsed) ? parsed : null;
            flavor = candidateFlavor;
            return true;
        }
        id = string.Empty;
        version = null;
        flavor = default;
        return false;
    }
}
