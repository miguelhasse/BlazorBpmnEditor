using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.FileSystem;

/// <summary>Registers the file-system document store.</summary>
public static class FileDocumentStoreServiceCollectionExtensions
{
    /// <summary>Uses a folder of files as the document store.</summary>
    /// <param name="services">Host services.</param>
    /// <param name="root">The folder that holds the documents; created on the first save.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddBpmnFileDocumentStore(this IServiceCollection services, string root)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        services.TryAddSingleton(provider => new FileBpmnDocumentStore(root, provider.GetService<TimeProvider>()));
        return services.AddBpmnDocumentStore(provider => provider.GetRequiredService<FileBpmnDocumentStore>());
    }
}
