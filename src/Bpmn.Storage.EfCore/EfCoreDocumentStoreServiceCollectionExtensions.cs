using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.EfCore;

/// <summary>Registers the Entity Framework Core document store.</summary>
public static class EfCoreDocumentStoreServiceCollectionExtensions
{
    /// <summary>
    /// Uses the documents table of <typeparamref name="TContext"/> as the document store. The host registers the context with
    /// <c>AddDbContextFactory</c> and calls <c>ApplyBpmnDocuments</c> in its <c>OnModelCreating</c>.
    /// </summary>
    /// <typeparam name="TContext">The host's context.</typeparam>
    /// <param name="services">Host services.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddBpmnEfCoreDocumentStore<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(provider => new EfCoreBpmnDocumentStore<TContext>(provider.GetRequiredService<IDbContextFactory<TContext>>(), provider.GetService<TimeProvider>()));
        return services.AddBpmnDocumentStore(provider => provider.GetRequiredService<EfCoreBpmnDocumentStore<TContext>>());
    }
}
