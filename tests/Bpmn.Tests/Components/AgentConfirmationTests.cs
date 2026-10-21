using Bunit;

using Hasseware.AspNetCore.Components.Bpmn.Editing;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>
/// An agent's consequential action waits on the user's explicit yes (design doc, "Security and consent").
/// </summary>
public sealed class AgentConfirmationTests : BunitContext
{
    private static readonly BpmnConfirmationRequest Delete = new(
        "Delete 2 elements?",
        "They and their connections are removed. You can undo this.",
        BpmnEditOrigin.FromAgent("webmcp"),
        ["Review Request", "Done"],
        IsDestructive: true);

    public AgentConfirmationTests() => Services.AddBpmnComponents();

    [Fact]
    public async Task ShowsTheQuestionAndAnswersYesWhenAllowed()
    {
        var scope = Render<BpmnEditorScope>();
        var session = scope.Instance.Session;

        var answer = await AskAsync(scope, Delete, Token);
        var shown = session.State.PendingConfirmation;
        await scope.InvokeAsync(() => session.ResolveConfirmation(allowed: true));

        Assert.Same(Delete, shown);
        Assert.True(await answer);
        Assert.Null(session.State.PendingConfirmation);
    }

    [Fact]
    public async Task AnswersNoWhenDenied()
    {
        var scope = Render<BpmnEditorScope>();
        var session = scope.Instance.Session;

        var answer = await AskAsync(scope, Delete, Token);
        await scope.InvokeAsync(() => session.ResolveConfirmation(allowed: false));

        Assert.False(await answer);
    }

    [Fact]
    public async Task DeclinesASecondRequestWhileOneIsPending()
    {
        var scope = Render<BpmnEditorScope>();
        var session = scope.Instance.Session;

        var first = await AskAsync(scope, Delete, Token);
        var second = await AskAsync(scope, Delete with { Title = "Another?" }, Token);

        Assert.True(second.IsCompleted);
        Assert.False(await second);
        Assert.False(first.IsCompleted);
        Assert.Same(Delete, session.State.PendingConfirmation);
    }

    [Fact]
    public async Task WithdrawingTheQuestionAnswersNo()
    {
        var scope = Render<BpmnEditorScope>();
        var session = scope.Instance.Session;
        using var withdraw = new CancellationTokenSource();

        var answer = await AskAsync(scope, Delete, withdraw.Token);
        await scope.InvokeAsync(withdraw.Cancel);

        Assert.False(await answer);
        Assert.Null(session.State.PendingConfirmation);
    }

    [Fact]
    public async Task AnAlreadyWithdrawnQuestionIsNeverShown()
    {
        var scope = Render<BpmnEditorScope>();
        var session = scope.Instance.Session;

        var answer = await AskAsync(scope, Delete, new CancellationToken(canceled: true));

        Assert.False(await answer);
        Assert.Null(session.State.PendingConfirmation);
    }

    [Fact]
    public async Task OnlyAnAgentAsks()
    {
        var scope = Render<BpmnEditorScope>();

        await Assert.ThrowsAsync<ArgumentException>(() => AskAsync(scope, Delete with { Origin = BpmnEditOrigin.User }, Token));
    }

    [Fact]
    public async Task ClosingTheEditorAnswersNo()
    {
        var scope = Render<BpmnEditorScope>();
        var answer = await AskAsync(scope, Delete, Token);

        await DisposeComponentsAsync();

        Assert.False(await answer);
    }

    private static CancellationToken Token => Xunit.TestContext.Current.CancellationToken;

    // Starts the request on the renderer's context and hands back the pending answer, unawaited.
    private static async Task<Task<bool>> AskAsync(IRenderedComponent<BpmnEditorScope> scope, BpmnConfirmationRequest request, CancellationToken token)
    {
        Task<bool>? answer = null;
        await scope.InvokeAsync(() => { answer = scope.Instance.Session.RequestConfirmationAsync(request, token); });
        return answer!;
    }
}
