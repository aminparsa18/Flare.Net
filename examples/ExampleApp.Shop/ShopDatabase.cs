using Npgsql;

namespace ExampleApp.Shop;

/// <summary>
/// The shop's Postgres (<c>shopdb</c>): a <c>products</c> table inventory-service reads and
/// decrements, and an <c>orders</c> table order-service writes. Npgsql 10's own ActivitySource
/// traces every command.
/// </summary>
public static class ShopDatabase
{
    public const int ProductCount = 500;

    public static void AddShopDatabase(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(sp =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("shopdb")
                ?? throw new InvalidOperationException("No 'shopdb' connection string - reference the Postgres database from the AppHost (.WithReference(shopdb)).");
            return NpgsqlDataSource.Create(connectionString);
        });
        builder.Services.AddHostedService<SchemaInitializer>();
    }

    /// <summary>Creates and seeds the tables before the host starts serving. Both roles that use the database run it; it's idempotent.</summary>
    private sealed class SchemaInitializer(NpgsqlDataSource dataSource) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using var command = dataSource.CreateCommand($"""
                CREATE TABLE IF NOT EXISTS products (
                    sku integer PRIMARY KEY,
                    name text NOT NULL,
                    price numeric(10, 2) NOT NULL,
                    stock integer NOT NULL,
                    partner_sku integer
                );
                INSERT INTO products (sku, name, price, stock, partner_sku)
                SELECT s, 'Product ' || s, round((5 + random() * 195)::numeric, 2), 50 + (random() * 950)::int,
                       CASE WHEN s % 3 = 0 THEN 100000 + s * 7 END
                FROM generate_series(1, {ProductCount}) AS s
                ON CONFLICT (sku) DO NOTHING;
                CREATE TABLE IF NOT EXISTS orders (
                    id uuid PRIMARY KEY,
                    user_id integer NOT NULL,
                    total numeric(10, 2) NOT NULL,
                    currency text NOT NULL,
                    status text NOT NULL,
                    created_at timestamptz NOT NULL DEFAULT now()
                );
                """);
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await command.ExecuteNonQueryAsync(cancellationToken);
                    return;
                }
                catch (PostgresException ex) when (attempt < 3 && ex.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.DuplicateObject)
                {
                    // Both roles raced CREATE TABLE IF NOT EXISTS on first start (it isn't
                    // atomic in Postgres) - the tables exist now, so run the rest again.
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
