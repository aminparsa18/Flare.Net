using System.Text.Json;

namespace ExampleApp.Shop;

public sealed record CartLine(int Sku, int Quantity);

public sealed record CheckoutRequest(int UserId, IReadOnlyList<CartLine> Items, string Currency, string CardNumber, string Email, string Phone, string? PromoCode);

public sealed record ReserveRequest(Guid OrderId, IReadOnlyList<CartLine> Items);

public sealed record ReserveResult(decimal Total, IReadOnlyList<int> PartnerSkus);

public sealed record ChargeRequest(Guid OrderId, int UserId, decimal Amount, string Currency, string CardNumber);

public sealed record ConfirmOrderRequest(Guid OrderId, int UserId, decimal Total, string Currency, IReadOnlyList<CartLine> Items, string Email);

/// <summary>The Kafka message payload - one shape for every topic, kept simple on purpose.</summary>
public sealed record OrderEvent(Guid OrderId, int UserId, decimal Total, string Currency, string? Email = null, string? Phone = null)
{
    public string ToJson() => JsonSerializer.Serialize(this, JsonSerializerOptions.Web);

    public static OrderEvent FromJson(string json) => JsonSerializer.Deserialize<OrderEvent>(json, JsonSerializerOptions.Web)!;
}

public static class ShopServiceClients
{
    /// <summary>A named client for another shop service - <c>http://{name}</c>, resolved by service discovery from the AppHost's WithReference.</summary>
    public static IServiceCollection AddShopServiceClient(this IServiceCollection services, string name)
    {
        services.AddHttpClient(name, client => client.BaseAddress = new Uri($"http://{name}"));
        return services;
    }
}
