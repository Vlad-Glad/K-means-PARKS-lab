using Parcs.Portal.Services.Interfaces;
using Parcs.Portal.Services;
using Parcs.Core.Configuration;
using Parcs.Portal.Configuration;
using Polly.Extensions.Http;
using Polly;

namespace Parcs.Portal.Extensions
{
    public static class IServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            return services
                .AddScoped<IDocumentResponseProcessor, DocumentResponseProcessor>()
                .AddScoped<IHostClient, HostClient>();
        }

        public static IServiceCollection AddApplicationOptions(this IServiceCollection services, IConfiguration configuration)
        {
            return services
                .Configure<HostingConfiguration>(configuration.GetSection(HostingConfiguration.SectionName))
                .Configure<HostConfiguration>(configuration.GetSection(HostConfiguration.SectionName))
                .Configure<PortalConfiguration>(configuration.GetSection(PortalConfiguration.SectionName));
        }

        public static IServiceCollection AddApplicationHealthChecks(this IServiceCollection services, IConfiguration configuration)
        {
            var elasticsearchConfiguration = configuration
                .GetSection(ElasticsearchConfiguration.SectionName)
                .Get<ElasticsearchConfiguration>();

            var healthChecks = services.AddHealthChecks();
            if (!string.IsNullOrWhiteSpace(elasticsearchConfiguration?.BaseUrl) &&
                Uri.TryCreate(elasticsearchConfiguration.BaseUrl, UriKind.Absolute, out _))
            {
                healthChecks.AddElasticsearch(elasticsearchConfiguration.BaseUrl, "elasticsearch");
            }

            return services;
        }

        public static IServiceCollection AddHttpClients(this IServiceCollection services, IConfiguration configuration)
        {
            var hostConfiguration = configuration
                .GetSection(HostConfiguration.SectionName)
                .Get<HostConfiguration>();

            var retryPolicy = HttpPolicyExtensions
                .HandleTransientHttpError()
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

            services.AddHttpClient<IHostClient, HostClient>(client =>
            {
                var rawUri = hostConfiguration?.Uri ?? "localhost";
                if (!rawUri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !rawUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    rawUri = rawUri.Contains(':') ? $"http://{rawUri}" : $"http://{rawUri}:80";
                }
                client.BaseAddress = new Uri(rawUri);
            })
            .AddPolicyHandler(retryPolicy);

            return services;
        }
    }
}