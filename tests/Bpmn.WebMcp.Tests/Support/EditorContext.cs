using Bunit;

using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Storage;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Hasseware.AspNetCore.Components.Bpmn.WebMcp.Tests.Support;

/// <summary>A test context with the editor's services, WebMCP registered, and a way to open an editor scope.</summary>
public abstract class EditorContext : BunitContext
{
    protected EditorContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddBpmnComponents().AddBpmnWebMcp();
    }

    /// <summary>Opens an editor scope on a document, with no UI parts.</summary>
    internal IRenderedComponent<BpmnEditorScope> RenderScope(BpmnDocument? document = null, bool readOnly = false) =>
        Render<BpmnEditorScope>(parameters => parameters
            .Add(scope => scope.Document, document ?? Documents.Simple())
            .Add(scope => scope.IsReadOnly, readOnly));

    /// <summary>Opens an editor scope with a <see cref="BpmnWebMcp"/> in it, against a browser the test controls.</summary>
    internal (IRenderedComponent<BpmnEditorScope> Scope, FakeBrowser Browser) RenderWithBrowser(
        FakeBrowser? browser = null,
        Action<ComponentParameterCollectionBuilder<BpmnWebMcp>>? parameters = null,
        BpmnDocument? document = null,
        bool readOnly = false,
        BpmnDocumentBinding? binding = null,
        bool browserRegistered = false)
    {
        browser ??= new FakeBrowser();
        if (!browserRegistered) Services.AddSingleton<IJSRuntime>(browser);

        var scope = Render<BpmnEditorScope>(scopeParameters =>
        {
            if (binding is null) scopeParameters.Add(item => item.Document, document ?? Documents.Simple());
            else scopeParameters.Add(item => item.DocumentBinding, binding);

            scopeParameters.Add(item => item.IsReadOnly, readOnly).AddChildContent<BpmnWebMcp>(parameters);
        });
        return (scope, browser);
    }
}
