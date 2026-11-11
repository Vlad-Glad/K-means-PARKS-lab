using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Serilog.Sinks;
using Parcs.Host.Configuration;
using Serilog;

namespace Parcs.Host.Extensions
{
    public static class ConfigureHostBuilderExtensions
    {
        public static void AddElasticSearchLogging(this ConfigureHostBuilder hostBuilder, IConfiguration configuration)
        {
            var elasticsearchConfiguration = configuration
                .GetSection(ElasticsearchConfiguration.SectionName)
                .Get<ElasticsearchConfiguration>();

            hostBuilder.UseSerilog((hostContext, logging) =>
            {
                if (!string.IsNullOrWhiteSpace(elasticsearchConfiguration?.BaseUrl) &&
                    Uri.TryCreate(elasticsearchConfiguration.BaseUrl, UriKind.Absolute, out var esUri))
                {
                    logging.WriteTo.Elasticsearch([esUri], options =>
                    {
                        options.DataStream = new DataStreamName("parcs-portal");
                        options.BootstrapMethod = BootstrapMethod.Silent;
                    });
                }

                logging.ReadFrom.Configuration(hostContext.Configuration);
            });
        }
    }
}