using Azure.Storage.Blobs;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.AzureBlob;

/// <summary>Registers the Azure Blob Storage document store.</summary>
public static class AzureBlobDocumentStoreServiceCollectionExtensions
{
    /// <summary>Uses a blob container as the document store.</summary>
    /// <param name="services">Host services.</param>
    /// <param name="container">Gets the container, from the host's own configuration and credentials (a managed identity is best).</param>
    /// <param name="prefix">A folder inside the container to keep documents under; the container's root when null.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddBpmnAzureBlobDocumentStore(this IServiceCollection services, Func<IServiceProvider, BlobContainerClient> container, string? prefix = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(container);

        services.TryAddSingleton(provider => new AzureBlobBpmnDocumentStore(container(provider), prefix));
        return services.AddBpmnDocumentStore(provider => provider.GetRequiredService<AzureBlobBpmnDocumentStore>());
    }
}
