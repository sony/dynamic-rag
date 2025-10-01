using Microsoft.Extensions.DependencyInjection;
using PgVectorDynamicRAG.Services;
using PgVectorDynamicRAG.Factories;


namespace PgVectorDynamicRAG.Services
{
    /// <summary>
    /// Extension methods for IServiceCollection to register embedding services
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Add embedding services to the service collection
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddEmbeddingServices(this IServiceCollection services)
        {
            // Register the embedding services
            services.AddSingleton<TextEmbeddingService>();
            services.AddSingleton<ImageEmbeddingService>();
            services.AddSingleton<EmbeddingServiceFactory>();
            
            return services;
        }
    }
}
