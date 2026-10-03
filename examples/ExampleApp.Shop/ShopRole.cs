namespace ExampleApp.Shop;

/// <summary>
/// Which shop service this process is. The call graph ExampleApp.AppHost wires up:
/// <code>
/// storefront ──HTTP──▶ checkout-api ──HTTP──▶ payment-service ──HTTP──▶ fraud-check (sometimes)
///     │                    │   │                   └──▶ api.stripe.com (fake-upstream)
///     │                    │   ├──HTTP──▶ inventory-service ──▶ postgres
///     │                    │   ├──HTTP──▶ order-service ──▶ postgres
///     │                    │   └──▶ maps.googleapis.com, inventory.partner-corp.com (fake-upstream)
///     └──kafka clickstream.events           kafka orders.created / payments.completed / inventory.reserved
///                                           ──▶ order-service, notification-service consumer groups
/// notification-service (order-notifier group) ──▶ api.twilio.com, api.sendgrid.com
/// (fake models, see <see cref="LlmClients"/>: fraud-check and notification-service chat, storefront embeddings)
/// </code>
/// </summary>
public enum ShopRole
{
    Storefront,
    CheckoutApi,
    PaymentService,
    FraudCheck,
    OrderService,
    NotificationService,
    InventoryService,

    /// <summary>
    /// Not a shop service: the stand-in for every third-party API the shop calls. See
    /// <see cref="FakeUpstream"/> and <see cref="ExternalApiClient"/>.
    /// </summary>
    FakeUpstream,
}

public static class ShopRoles
{
    /// <summary>Kebab-case resource name per role - also the Aspire resource name, so also the OTel service.name.</summary>
    public static string ResourceName(ShopRole role) => role switch
    {
        ShopRole.Storefront => "storefront",
        ShopRole.CheckoutApi => "checkout-api",
        ShopRole.PaymentService => "payment-service",
        ShopRole.FraudCheck => "fraud-check",
        ShopRole.OrderService => "order-service",
        ShopRole.NotificationService => "notification-service",
        ShopRole.InventoryService => "inventory-service",
        ShopRole.FakeUpstream => "fake-upstream",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    public static ShopRole Parse(string? value)
    {
        foreach (var role in Enum.GetValues<ShopRole>())
        {
            if (string.Equals(ResourceName(role), value, StringComparison.OrdinalIgnoreCase))
            {
                return role;
            }
        }

        throw new InvalidOperationException(
            $"Shop:Role must be one of {string.Join(", ", Enum.GetValues<ShopRole>().Select(ResourceName))} (got '{value}'). " +
            "ExampleApp.AppHost sets it per resource via Shop__Role.");
    }
}

public sealed record ShopRoleInfo(ShopRole Role)
{
    public string Name => ShopRoles.ResourceName(Role);
}
