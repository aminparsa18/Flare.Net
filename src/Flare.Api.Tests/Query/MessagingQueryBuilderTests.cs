using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class MessagingQueryBuilderTests
{
    private static readonly DateTimeOffset End = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, MessagingQueryBuilder.DefaultWindowMinutes)]
    [InlineData(0, MessagingQueryBuilder.DefaultWindowMinutes)]
    [InlineData(1, MessagingQueryBuilder.MinWindowMinutes)]
    [InlineData(15, 15)]
    [InlineData(100_000, MessagingQueryBuilder.MaxWindowMinutes)]
    public void ClampWindowMinutes_ClampsAndDefaults(int? requested, int expected)
    {
        Assert.Equal(expected, MessagingQueryBuilder.ClampWindowMinutes(requested));
    }

    [Fact]
    public void SpanRoleExpr_OperationDecidesFirst_KindOnlyWhenNoOperation()
    {
        var role = MessagingQueryBuilder.SpanRoleExpr;

        Assert.Contains("IN ('publish', 'create', 'send'), 'publish'", role);
        Assert.Contains("IN ('process', 'deliver'), 'process'", role);
        Assert.Contains("= 'receive', 'receive'", role);
        Assert.Contains("= '' AND Kind = 4, 'publish'", role);
        Assert.Contains("= '' AND Kind = 5, 'process'", role);
        Assert.EndsWith(", '')", role);
        Assert.True(role.IndexOf("Kind = 4", StringComparison.Ordinal) > role.IndexOf("'receive'", StringComparison.Ordinal));
    }

    [Fact]
    public void SpanSource_DropsReceiveSpansOfConsumersThatAlsoEmitProcessSpans()
    {
        var result = MessagingQueryBuilder.BuildDestinations(new MessagingDestinationsRequest(), 60, End);

        Assert.Contains("max(SpanRole = 'process') OVER (PARTITION BY MsgSystem, MsgDestination, ServiceName, MsgGroup) AS HasProcess", result.Sql);
        Assert.Contains("if(SpanRole = 'publish', 'publish', 'consume') AS Role", result.Sql);
        Assert.Contains("WHERE SpanRole != ''", result.Sql);
        Assert.Contains("WHERE (SpanRole != 'receive' OR NOT HasProcess)", result.Sql);
    }

    [Fact]
    public void AttributeExprs_FallBackToOlderNames()
    {
        Assert.Contains("SpanAttributes['messaging.operation.type'] != '', SpanAttributes['messaging.operation.type'], SpanAttributes['messaging.operation']", MessagingQueryBuilder.OperationExpr);
        Assert.Contains("SpanAttributes['messaging.destination.partition.id'] != '', SpanAttributes['messaging.destination.partition.id'], SpanAttributes['messaging.kafka.destination.partition']", MessagingQueryBuilder.PartitionExpr);
        Assert.Contains("SpanAttributes['messaging.consumer.group.name'] != '', SpanAttributes['messaging.consumer.group.name'], SpanAttributes['messaging.kafka.consumer.group']", MessagingQueryBuilder.ConsumerGroupExpr);
    }

    [Fact]
    public void BuildDestinations_OnlyClassifiedMessagingSpans_InWindow_GroupedBySystemAndDestination()
    {
        var result = MessagingQueryBuilder.BuildDestinations(new MessagingDestinationsRequest(), 60, End);

        Assert.Contains("FROM spans", result.Sql);
        Assert.Contains("mapContains(SpanAttributes, 'messaging.system')", result.Sql);
        Assert.Contains("GROUP BY MsgSystem, MsgDestination", result.Sql);
        Assert.Contains("quantilesIf(0.5, 0.99)(DurationNano, Role = 'publish') AS PublishQuantiles", result.Sql);
        Assert.Contains("quantilesIf(0.5, 0.99)(DurationNano, Role = 'consume') AS ConsumeQuantiles", result.Sql);
        Assert.Contains("ORDER BY PublishCount + ConsumeCount DESC", result.Sql);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(End.AddMinutes(-60).UtcDateTime, parameters["from"]);
        Assert.Equal(End.UtcDateTime, parameters["to"]);
        Assert.Equal((uint)(MessagingQueryBuilder.MaxDestinations + 1), parameters["limit"]);
        Assert.False(parameters.ContainsKey("service"));
        Assert.False(parameters.ContainsKey("system"));
    }

    [Fact]
    public void BuildDestinations_ServiceAndSystemFilters_AreParameterized()
    {
        var result = MessagingQueryBuilder.BuildDestinations(new MessagingDestinationsRequest { Service = "orders", System = "kafka" }, 60, End);

        Assert.Contains("ServiceName = {service:String}", result.Sql);
        Assert.Contains("SpanAttributes['messaging.system'] = {system:String}", result.Sql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("orders", parameters["service"]);
        Assert.Equal("kafka", parameters["system"]);
    }

    [Fact]
    public void BuildFacets_IgnoresServiceAndSystemFilters()
    {
        var result = MessagingQueryBuilder.BuildFacets(60, End);

        Assert.DoesNotContain("{service:String}", result.Sql);
        Assert.DoesNotContain("{system:String}", result.Sql);
        Assert.Contains("arraySort(groupUniqArray(1000)(MsgSystem)) AS Systems", result.Sql);
    }

    [Fact]
    public void BuildServices_ScopesToOneDestination_AndBlanksProducerGroup()
    {
        var request = new MessagingDestinationDetailRequest { System = "kafka", Destination = "orders" };
        var result = MessagingQueryBuilder.BuildServices(request, 60, End);

        Assert.Contains("SpanAttributes['messaging.destination.name'] = {destination:String}", result.Sql);
        Assert.Contains("if(Role = 'publish', '', MsgGroup) AS ConsumerGroup", result.Sql);
        Assert.Contains("GROUP BY Role, ServiceName, ConsumerGroup", result.Sql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("orders", parameters["destination"]);
        Assert.Equal("kafka", parameters["system"]);
    }

    [Fact]
    public void BuildServices_EmptyDestination_StillFiltersOnIt()
    {
        var request = new MessagingDestinationDetailRequest { System = "rabbitmq", Destination = "" };
        var result = MessagingQueryBuilder.BuildServices(request, 60, End);

        Assert.Equal("", result.Parameters.ToDictionary()["destination"]);
    }

    [Fact]
    public void BuildPartitions_SkipsSpansWithoutPartition_NumericOrder()
    {
        var request = new MessagingDestinationDetailRequest { System = "kafka", Destination = "orders" };
        var result = MessagingQueryBuilder.BuildPartitions(request, 60, End);

        Assert.Contains("WHERE (SpanRole != 'receive' OR NOT HasProcess)\n    AND MsgPartition != ''", result.Sql);
        Assert.Contains("ORDER BY toUInt64OrNull(MsgPartition) ASC NULLS LAST", result.Sql);
    }

    [Fact]
    public void BuildConsumerLag_ForOneTopic_ReturnsPerGroupAndPartitionRows()
    {
        var result = MessagingQueryBuilder.BuildConsumerLag(60, End, "orders");

        Assert.Contains("FROM metrics_gauge", result.Sql);
        Assert.Contains("argMax(Value, Time) AS Lag", result.Sql);
        Assert.Contains("DataPointAttributes['topic'] = {topic:String}", result.Sql);
        Assert.StartsWith("SELECT Topic, ConsumerGroup, PartitionId,", result.Sql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(MessagingQueryBuilder.ConsumerLagMetric, parameters["lagMetric"]);
        Assert.Equal("orders", parameters["topic"]);
    }

    [Fact]
    public void BuildConsumerLag_WithoutTopic_SumsPerTopic()
    {
        var result = MessagingQueryBuilder.BuildConsumerLag(60, End, topic: null);

        Assert.StartsWith("SELECT Topic, toInt64(round(sum(Lag))) AS Lag", result.Sql);
        Assert.Contains("GROUP BY Topic", result.Sql);
        Assert.DoesNotContain("{topic:String}", result.Sql);
    }
}
