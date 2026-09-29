using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExampleApp.Seeder.Scenarios;

/// <summary>
/// Four pipeline rules - redact card numbers, extract <c>user_id</c>, parse JSON bodies, and a
/// paused email mask - plus the logs they act on, for the pipeline-rules docs. Rules apply at
/// ingest time, so they're created first and the logs are sent once Flare.Ingest has picked
/// them up (<see cref="IngestSettleTime"/>).
/// </summary>
/// <remarks>
/// Rules persist and rewrite everything ingested after them, not just these logs - run
/// <c>pipeline --clear</c> when you're done with the page.
/// </remarks>
public sealed class PipelineScenario : Scenario
{
    public override string Name => "pipeline";

    public override string Description => "4 pipeline rules (localized) + card-number, user_id= and JSON-body logs";

    // Flare.Ingest re-polls rules every PipelineRules__RefreshInterval (30 s by default).
    public override TimeSpan IngestSettleTime => TimeSpan.FromSeconds(35);

    private static readonly (Localized Name, Localized Description, string Service, bool Enabled, JsonObject Action)[] Rules =
    [
        (new("Redact card numbers", "Маскировать номера карт", "脱敏银行卡号"),
         new("Mask PANs before storage", "Скрывать номера карт до сохранения", "存储前屏蔽卡号"),
         "payment-service", true,
         new JsonObject { ["kind"] = "RedactRegex", ["redactRegex"] = new JsonObject { ["pattern"] = @"\b\d{13,16}\b", ["replacement"] = "***" } }),
        (new("Extract user_id", "Извлечь user_id", "提取 user_id"),
         new("Make user_id a queryable attribute", "Сделать user_id атрибутом для запросов", "将 user_id 设为可查询属性"),
         "auth-service", true,
         new JsonObject { ["kind"] = "ExtractRegex", ["extractRegex"] = new JsonObject { ["pattern"] = @"user_id=(?<user_id>\d+)" } }),
        (new("Parse JSON bodies", "Разбор JSON-тела", "解析 JSON 正文"),
         new("Flatten orders-api JSON logs", "Развернуть JSON-логи orders-api", "展开 orders-api 的 JSON 日志"),
         "orders-api", true,
         new JsonObject { ["kind"] = "ParseJson", ["parseJson"] = new JsonObject { ["keyPrefix"] = "json.", ["maxDepth"] = 5, ["maxKeys"] = 100 } }),
        (new("Mask email addresses", "Маскировать email-адреса", "屏蔽邮箱地址"),
         new("Paused while we check false positives", "Приостановлено: проверяем ложные срабатывания", "暂停中：正在排查误报"),
         "orders-api", false,
         new JsonObject { ["kind"] = "RedactRegex", ["redactRegex"] = new JsonObject { ["pattern"] = @"[\w.+-]+@[\w-]+\.[\w.]+", ["replacement"] = "<email>" } }),
    ];

    public override IEnumerable<(FlareApiClient.Kind, Localized)> Objects => Rules.Select(r => (FlareApiClient.Kind.PipelineRules, r.Name));

    public override async Task CreateObjectsAsync(SeedContext c, CancellationToken ct)
    {
        foreach (var (name, description, service, enabled, action) in Rules)
        {
            var rule = new JsonObject
            {
                ["name"] = c.T(name),
                ["description"] = c.T(description),
                ["enabled"] = enabled,
                ["condition"] = new JsonObject { ["services"] = new JsonArray(service) },
                ["actions"] = new JsonArray(action.DeepClone()),
            };
            Console.WriteLine($"  pipeline rule {await c.Api.CreateAsync(FlareApiClient.Kind.PipelineRules, rule, ct)} ({c.T(name)})");
        }
    }

    public override void Generate(SeedContext c)
    {
        string[] cards = ["4242424242424242", "5555555555554444", "4000056655665556", "378282246310005"];
        string[] currencies = ["USD", "EUR", "GBP"];

        var payments = c.Batch.Service("payment-service");
        for (var i = 0; i < c.PerHour(240); i++)
        {
            var order = c.Rng.Next(10000, 99999);
            var (severity, body) = c.Rng.NextDouble() switch
            {
                < 0.75 => (9, $"Charging card {c.Pick(cards)} for order {order}: {c.Rng.Next(5, 900)}.{c.Rng.Next(0, 99):D2} {c.Pick(currencies)}"),
                < 0.9 => (9, $"Payment captured for order {order}"),
                _ => (13, $"Card {c.Pick(cards)} declined for order {order}: card_declined"),
            };
            c.Log(payments, "PaymentService.Charges", c.RandomTime(1), severity, body);
        }

        var auth = c.Batch.Service("auth-service");
        for (var i = 0; i < c.PerHour(300); i++)
        {
            var user = c.Rng.Next(1000, 60000);
            var (severity, body) = c.Rng.NextDouble() switch
            {
                < 0.6 => (9, $"Login succeeded user_id={user} method=password ip=10.0.{c.Rng.Next(0, 8)}.{c.Rng.Next(2, 250)}"),
                < 0.85 => (9, $"Token refreshed user_id={user} ttl=3600"),
                _ => (13, $"Login failed user_id={user} reason=bad_password attempts={c.Rng.Next(1, 6)}"),
            };
            c.Log(auth, "AuthService.Sessions", c.RandomTime(1), severity, body);
        }

        var orders = c.Batch.Service("orders-api");
        string[] names = ["jane.doe", "li.wei", "olga.petrova", "sam.taylor", "amir.haddad"];
        for (var i = 0; i < c.PerHour(200); i++)
        {
            var failed = c.Rng.NextDouble() < 0.06;
            var body = JsonSerializer.Serialize(new
            {
                @event = failed ? "order.failed" : "order.created",
                order_id = c.Rng.Next(10000, 99999),
                user = new { id = c.Rng.Next(1000, 60000), email = $"{c.Pick(names)}@example.com" },
                total = Math.Round(c.Rng.NextDouble() * 400 + 10, 2),
                currency = c.Pick(currencies),
                items = Enumerable.Range(0, c.Rng.Next(1, 4)).Select(_ => new { sku = $"SKU-{c.Rng.Next(100, 999)}", qty = c.Rng.Next(1, 4) }),
                reason = failed ? "inventory_unavailable" : null,
            }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            c.Log(orders, "OrdersApi.Events", c.RandomTime(1), failed ? 17 : 9, body);
        }
    }
}
