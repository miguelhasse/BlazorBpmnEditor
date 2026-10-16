using Hasseware.AspNetCore.Components.Bpmn.Feel;
using Hasseware.FluentUI.AspNetCore.Components.Bpmn;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebAssemblyHostBuilder.CreateDefault(args);


builder.Services.AddFluentUIComponents();
builder.Services.AddFluentBpmnEditor().AddBpmnFeelEditor();
builder.Services.Configure<Hasseware.AspNetCore.Components.Bpmn.Configuration.BpmnOptions>(options =>
{
    options.Extensions.AddZeebe().AddCamunda7();
    options.Extensions.AlwaysOfferRegisteredSchemas = false;
    options.Templates.Engines["camunda"] = "8.6.0";
    var assembly = typeof(Program).Assembly;
    foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.Contains(".templates.", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal)))
    {
        using var stream = assembly.GetManifestResourceStream(resource)!;
        options.Templates.AddTemplates(stream);
    }
});

// The demo fetches its .bpmn fixtures from the host's wwwroot.
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Build().RunAsync();
