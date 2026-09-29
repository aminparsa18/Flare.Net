using System.Diagnostics.Metrics;

namespace ExampleApp.Shop;

/// <summary>
/// The shop's own business metrics, on top of what ASP.NET Core/HttpClient/runtime/Kafka/
/// Npgsql instrumentation already record.
/// </summary>
/// <remarks>
/// <see cref="RecordItemsAdded"/> is the deliberately bad one: with
/// <c>Shop:HighCardinalityMetrics=true</c> it tags every data point with the shopper's
/// <c>user.id</c> - thousands of series from one metric, the classic mistake the Metrics
/// catalog's cardinality view exists to catch. Off by default so the demo doesn't bloat
/// storage unless you're looking at that page.
/// </remarks>
public sealed class ShopMetrics
{
    public const string MeterName = "ExampleApp.Shop";

    private readonly bool _highCardinality;
    private readonly Counter<long> _itemsAdded;
    private readonly Counter<long> _ordersPlaced;
    private readonly Histogram<double> _orderValue;

    public ShopMetrics(IMeterFactory meterFactory, IConfiguration configuration)
    {
        _highCardinality = configuration.GetValue("Shop:HighCardinalityMetrics", false);
        var meter = meterFactory.Create(MeterName);
        _itemsAdded = meter.CreateCounter<long>("checkout.cart.items_added", "{item}", "Items added to a shopping cart");
        _ordersPlaced = meter.CreateCounter<long>("shop.orders.placed", "{order}", "Orders confirmed by order-service");
        _orderValue = meter.CreateHistogram<double>("shop.order.value", "USD", "Order total at confirmation");
    }

    public void RecordItemsAdded(int count, int userId, string currency)
    {
        if (_highCardinality)
        {
            _itemsAdded.Add(count, new("cart.currency", currency), new("user.id", $"u_{userId:D6}"));
        }
        else
        {
            _itemsAdded.Add(count, new KeyValuePair<string, object?>("cart.currency", currency));
        }
    }

    public void RecordOrderPlaced(decimal total, string currency)
    {
        _ordersPlaced.Add(1, new KeyValuePair<string, object?>("cart.currency", currency));
        _orderValue.Record((double)total, new KeyValuePair<string, object?>("cart.currency", currency));
    }
}
