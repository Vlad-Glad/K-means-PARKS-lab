using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Serilog.Sinks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Parcs.Daemon.Configuration;
using Serilog;

namespace Parcs.Daemon.Extensions
{
    public static class ILoggingBuilderExtensions
    {
        public static void AddElasticsearchLogging(this ILoggingBuilder loggingBuilder, IConfiguration configuration)
        {
            var elasticsearchConfiguration = configuration
                .GetSection(ElasticsearchConfiguration.SectionName)
                .Get<ElasticsearchConfiguration>();

            var loggerConfig = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .Enrich.FromLogContext();

            if (!string.IsNullOrWhiteSpace(elasticsearchConfiguration?.BaseUrl) &&
                Uri.TryCreate(elasticsearchConfiguration.BaseUrl, UriKind.Absolute, out var esUri))
            {
                loggerConfig.WriteTo.Elasticsearch([esUri], opts =>
                {
                    opts.DataStream = new DataStreamName("parcs-daemon");
                    opts.BootstrapMethod = BootstrapMethod.Silent;
                });
            }

            loggingBuilder.AddSerilog(loggerConfig.CreateLogger());
        }
    }
}