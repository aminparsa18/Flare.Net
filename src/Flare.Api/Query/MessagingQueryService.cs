using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Readers;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IMessagingQueryService
{
    Task<MessagingDestinationsResponse> GetDestinationsAsync(MessagingDestinationsRequest request, CancellationToken cancellationToken);

    Task<MessagingDestinationDetailResponse> GetDestinationDetailAsync(MessagingDestinationDetailRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The one component holding an <see cref="IClickHouseClient"/> for the <c>/messaging</c>
/// page - see <see cref="MessagingQueryBuilder"/> for the SQL. Same <c>ExecuteReaderAsync</c> +
/// ordinal-read style as <see cref="ExceptionQueryService"/>/<see cref="HostInventoryQueryService"/>.
/// </summary>
public sealed class MessagingQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IMessagingQueryService
{
    /// <summary>The <c>messaging.system</c> value Kafka instrumentations set - the only system consumer lag is looked up for.</summary>
    private const string KafkaSystem = "kafka";

    public async Task<MessagingDestinationsResponse> GetDestinationsAsync(MessagingDestinationsRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = MessagingQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var end = MessagingQueryBuilder.ResolveWindowEnd(request.EndUnixMs, timeProvider.GetUtcNow());
        var seconds = windowMinutes * 60.0;

        var destinations = new List<MessagingDestination>();
        var built = MessagingQueryBuilder.BuildDestinations(request, windowMinutes, end);
        await using (var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read() && destinations.Count < MessagingQueryBuilder.MaxDestinations)
            {
                var publishCount = reader.GetFieldValue<ulong>(2);
                var publishQuantiles = reader.GetFieldValue<double[]>(4);
                var consumeCount = reader.GetFieldValue<ulong>(5);
                var consumeQuantiles = reader.GetFieldValue<double[]>(7);
                destinations.Add(new MessagingDestination
                {
                    System = reader.GetString(0),
                    Destination = reader.GetString(1),
                    PublishCount = publishCount,
                    PublishErrorCount = reader.GetFieldValue<ulong>(3),
                    PublishPerSecond = publishCount / seconds,
                    PublishP50Ms = NanosToMs(publishQuantiles, 0),
                    PublishP99Ms = NanosToMs(publishQuantiles, 1),
                    ConsumeCount = consumeCount,
                    ConsumeErrorCount = reader.GetFieldValue<ulong>(6),
                    ConsumePerSecond = consumeCount / seconds,
                    ConsumeP50Ms = NanosToMs(consumeQuantiles, 0),
                    ConsumeP99Ms = NanosToMs(consumeQuantiles, 1),
                    ProducerServiceCount = reader.GetFieldValue<ulong>(8),
                    ConsumerServiceCount = reader.GetFieldValue<ulong>(9),
                    AvgMessageBytes = ReadNullableDouble(reader, 10),
                    ConsumerLag = null,
                });
            }
        }

        if (destinations.Exists(d => d.System == KafkaSystem))
        {
            var lagSql = MessagingQueryBuilder.BuildConsumerLag(windowMinutes, end, topic: null);
            var lagByTopic = new Dictionary<string, long>(StringComparer.Ordinal);
            await using (var reader = await client.ExecuteReaderAsync(lagSql.Sql, lagSql.Parameters, SafetyOptions(), cancellationToken))
            {
                while (reader.Read())
                {
                    lagByTopic[reader.GetString(0)] = reader.GetFieldValue<long>(1);
                }
            }

            for (var i = 0; i < destinations.Count; i++)
            {
                if (destinations[i].System == KafkaSystem && lagByTopic.TryGetValue(destinations[i].Destination, out var lag))
                {
                    destinations[i] = destinations[i] with { ConsumerLag = lag };
                }
            }
        }

        string[] systems = [];
        string[] services = [];
        var facetsSql = MessagingQueryBuilder.BuildFacets(windowMinutes, end);
        await using (var reader = await client.ExecuteReaderAsync(facetsSql.Sql, facetsSql.Parameters, SafetyOptions(), cancellationToken))
        {
            if (reader.Read())
            {
                systems = reader.GetFieldValue<string[]>(0);
                services = reader.GetFieldValue<string[]>(1);
            }
        }

        return new MessagingDestinationsResponse
        {
            WindowMinutes = windowMinutes,
            Destinations = destinations,
            Systems = systems,
            Services = services,
        };
    }

    public async Task<MessagingDestinationDetailResponse> GetDestinationDetailAsync(MessagingDestinationDetailRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = MessagingQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var end = MessagingQueryBuilder.ResolveWindowEnd(request.EndUnixMs, timeProvider.GetUtcNow());
        var seconds = windowMinutes * 60.0;

        var producers = new List<MessagingServiceStats>();
        var consumers = new List<MessagingServiceStats>();
        var servicesSql = MessagingQueryBuilder.BuildServices(request, windowMinutes, end);
        await using (var reader = await client.ExecuteReaderAsync(servicesSql.Sql, servicesSql.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                var count = reader.GetFieldValue<ulong>(3);
                var quantiles = reader.GetFieldValue<double[]>(5);
                var row = new MessagingServiceStats
                {
                    ServiceName = reader.GetString(1),
                    ConsumerGroup = reader.GetString(2),
                    Count = count,
                    ErrorCount = reader.GetFieldValue<ulong>(4),
                    PerSecond = count / seconds,
                    P50Ms = NanosToMs(quantiles, 0),
                    P99Ms = NanosToMs(quantiles, 1),
                    AvgMessageBytes = ReadNullableDouble(reader, 6),
                };
                (reader.GetString(0) == MessagingQueryBuilder.PublishRole ? producers : consumers).Add(row);
            }
        }

        var partitions = new List<MessagingPartitionStats>();
        var partitionsSql = MessagingQueryBuilder.BuildPartitions(request, windowMinutes, end);
        await using (var reader = await client.ExecuteReaderAsync(partitionsSql.Sql, partitionsSql.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read() && partitions.Count < MessagingQueryBuilder.MaxDestinations)
            {
                var publishCount = reader.GetFieldValue<ulong>(1);
                var consumeCount = reader.GetFieldValue<ulong>(2);
                partitions.Add(new MessagingPartitionStats
                {
                    Partition = reader.GetString(0),
                    PublishCount = publishCount,
                    PublishPerSecond = publishCount / seconds,
                    ConsumeCount = consumeCount,
                    ConsumePerSecond = consumeCount / seconds,
                    ErrorCount = reader.GetFieldValue<ulong>(3),
                });
            }
        }

        var lag = new List<MessagingConsumerLag>();
        if (request.System == KafkaSystem)
        {
            var lagSql = MessagingQueryBuilder.BuildConsumerLag(windowMinutes, end, request.Destination);
            await using var reader = await client.ExecuteReaderAsync(lagSql.Sql, lagSql.Parameters, SafetyOptions(), cancellationToken);
            while (reader.Read() && lag.Count < MessagingQueryBuilder.MaxDestinations)
            {
                lag.Add(new MessagingConsumerLag
                {
                    ConsumerGroup = reader.GetString(1),
                    Partition = reader.GetString(2),
                    Lag = reader.GetFieldValue<long>(3),
                });
            }
        }

        return new MessagingDestinationDetailResponse
        {
            System = request.System,
            Destination = request.Destination,
            WindowMinutes = windowMinutes,
            Producers = Cap(producers),
            Consumers = Cap(consumers),
            Partitions = partitions,
            ConsumerLag = lag,
        };
    }

    /// <summary><c>quantilesIf</c> yields <c>nan</c> for a side with no matching spans - reported as 0, which the dashboard shows as "-" off the zero count anyway.</summary>
    private static double NanosToMs(double[] quantiles, int index) =>
        index < quantiles.Length && double.IsFinite(quantiles[index]) ? quantiles[index] / 1_000_000.0 : 0;

    /// <summary><c>avg()</c> over a <c>Nullable</c> column is <c>NULL</c> when every value was null, and <c>nan</c> over zero rows.</summary>
    private static double? ReadNullableDouble(ClickHouseDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetDouble(ordinal);
        return double.IsFinite(value) ? value : null;
    }

    private static List<MessagingServiceStats> Cap(List<MessagingServiceStats> rows) =>
        rows.Count > MessagingQueryBuilder.MaxDestinations ? rows.GetRange(0, MessagingQueryBuilder.MaxDestinations) : rows;

    /// <summary>Same scan/time safety cap as <see cref="ExceptionQueryService"/>.</summary>
    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
