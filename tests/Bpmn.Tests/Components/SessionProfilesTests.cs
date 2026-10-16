using Bunit;

using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>A host that validates the document itself sees the validators the editor uses.</summary>
public sealed class SessionProfilesTests : BunitContext
{
    private sealed class MarkerProfile : IBpmnProfileValidator
    {
        public string Name => "marker";

        public IEnumerable<BpmnProfileDiagnostic> Validate(BpmnDocument document) => [];
    }

    [Fact]
    public void ListsTheRegisteredValidatorsAndTheElementTemplateOne()
    {
        Services.AddBpmnComponents().AddSingleton<IBpmnProfileValidator, MarkerProfile>();

        var profiles = Render<BpmnEditorScope>(parameters => parameters.Add(scope => scope.Document, new BpmnDocumentBuilder().Build()))
            .Instance.Session.Profiles;

        Assert.Contains(profiles, profile => profile is MarkerProfile);
        Assert.Contains(profiles, profile => profile is BpmnElementTemplateValidator);
    }

    [Fact]
    public void ListsTheElementTemplateValidatorEvenWithNoneRegistered()
    {
        Services.AddBpmnComponents();

        var profiles = Render<BpmnEditorScope>(parameters => parameters.Add(scope => scope.Document, new BpmnDocumentBuilder().Build()))
            .Instance.Session.Profiles;

        Assert.Single(profiles, profile => profile is BpmnElementTemplateValidator);
    }
}
