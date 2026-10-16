using Bpmn.Demo.Components;
using Hasseware.AspNetCore.Components.Bpmn.Feel;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Storage;
using Hasseware.AspNetCore.Components.Bpmn.Storage.ServerSentEvents;
using Hasseware.FluentUI.AspNetCore.Components.Bpmn;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();
builder.Services.AddFluentUIComponents();
builder.Services.AddFluentBpmnEditor().AddBpmnFeelEditor();
builder.Services.Configure<Hasseware.AspNetCore.Components.Bpmn.Configuration.BpmnOptions>(options => options.Extensions.AddZeebe());

// The live-updates page: a server-side store and change bus, and the Server-Sent Events feed over them.
// Anonymous and one shared space, because this is a playground; a real host authenticates and takes the tenant from a claim.
// Streams end after a few seconds so that the page shows the client reconnecting.
builder.Services.AddBpmnInMemoryDocumentStore();
builder.Services.AddBpmnChangeEvents(options =>
{
    options.SingleTenant = true;
    options.AllowAnonymous = true;
    options.HeartbeatInterval = TimeSpan.FromSeconds(2);
    options.MaxStreamLifetime = TimeSpan.FromSeconds(6);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapBpmnDocumentChangeEvents("/bpmn/changes");

// Stands in for "someone else saved the document": writes the next revision of a document in the server's store.
app.MapPost("/demo/live/touch", async (string path, IBpmnDocumentStore store, CancellationToken cancellationToken) =>
{
    if (!BpmnDocumentPath.TryParse(path, out var parsed) || !parsed.IsDocumentName) return Results.BadRequest();

    var key = new BpmnDocumentKey(null, parsed);
    var current = await store.StatAsync(key, cancellationToken);
    var document = new BpmnDocumentBuilder("Process_1", "Touched " + DateTimeOffset.UtcNow.ToString("O")).Build();
    var saved = await store.SaveDocumentAsync(key, document, current?.Revision, cancellationToken);
    return saved.Succeeded ? Results.Ok(saved.Revision) : Results.Conflict();
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Bpmn.Demo.Client._Imports).Assembly);

app.Run();
