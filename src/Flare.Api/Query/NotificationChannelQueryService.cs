using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

public interface INotificationChannelQueryService
{
    Task<NotificationChannel> CreateAsync(NotificationChannelRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationChannel>> ListAsync(CancellationToken cancellationToken);

    Task<NotificationChannel?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Bulk counterpart to <see cref="GetAsync"/>, used by <c>NotificationChannelResolver</c> to resolve an <see cref="AlertRule.ChannelIds"/> list in one round trip rather than N.</summary>
    Task<IReadOnlyList<NotificationChannel>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken);

    Task<NotificationChannel?> UpdateAsync(Guid id, NotificationChannelRequest request, CancellationToken cancellationToken);

    /// <summary>Soft-deletes (inserts a tombstone version) - see 0016_notification_channels.sql. Returns false if <paramref name="id"/> doesn't exist.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// The ClickHouse seam for notification-channel CRUD - mirrors <see cref="AlertQueryService"/>'s
/// role/shape (including its <c>ReplacingMergeTree(UpdatedAt)</c> / tombstone-delete /
/// <c>FINAL WHERE IsDeleted = 0</c> CRUD pattern - see that class's remarks for the full
/// rationale, which applies here unchanged) against <c>notification_channels</c> instead
/// of <c>alert_rules</c>.
/// </summary>
public sealed class NotificationChannelQueryService(IClickHouseClient client, TimeProvider timeProvider) : INotificationChannelQueryService
{
    private const string ChannelColumns =
        "Id, Name, Description, Type, WebhookUrl, TelegramBotToken, TelegramChatId, EmailTo, PagerDutyRoutingKey, CreatedAt, UpdatedAt";

    /// <summary>See <see cref="AlertQueryService.ResolveDefaults"/>'s remarks - same nullable-optional-field coalescing, for <see cref="NotificationChannelRequest"/> instead of <see cref="AlertRuleRequest"/>.</summary>
    internal static (string Description, string WebhookUrl, string TelegramBotToken, string TelegramChatId, string EmailTo, string PagerDutyRoutingKey) ResolveDefaults(NotificationChannelRequest request) => (
        Description: request.Description ?? "",
        WebhookUrl: request.WebhookUrl ?? "",
        TelegramBotToken: request.TelegramBotToken ?? "",
        TelegramChatId: request.TelegramChatId ?? "",
        EmailTo: request.EmailTo ?? "",
        PagerDutyRoutingKey: request.PagerDutyRoutingKey ?? "");

    public async Task<NotificationChannel> CreateAsync(NotificationChannelRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var defaults = ResolveDefaults(request);
        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = defaults.Description,
            Type = request.Type,
            WebhookUrl = defaults.WebhookUrl,
            TelegramBotToken = defaults.TelegramBotToken,
            TelegramChatId = defaults.TelegramChatId,
            EmailTo = defaults.EmailTo,
            PagerDutyRoutingKey = defaults.PagerDutyRoutingKey,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await InsertChannelVersionAsync(channel, isDeleted: false, cancellationToken);
        return channel;
    }

    public async Task<IReadOnlyList<NotificationChannel>> ListAsync(CancellationToken cancellationToken)
    {
        var sql = $"SELECT {ChannelColumns} FROM notification_channels FINAL WHERE IsDeleted = 0 ORDER BY Name";
        await using var reader = await client.ExecuteReaderAsync(sql, null, SafetyOptions(), cancellationToken);
        return ReadChannels(reader);
    }

    public async Task<NotificationChannel?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", id);
        var sql = $"SELECT {ChannelColumns} FROM notification_channels FINAL WHERE Id = {{id:UUID}} AND IsDeleted = 0";
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        return reader.Read() ? ReadChannel(reader) : null;
    }

    public async Task<IReadOnlyList<NotificationChannel>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("ids", ids.ToArray());
        var sql = $"SELECT {ChannelColumns} FROM notification_channels FINAL WHERE Id IN {{ids:Array(UUID)}} AND IsDeleted = 0";
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        return ReadChannels(reader);
    }

    public async Task<NotificationChannel?> UpdateAsync(Guid id, NotificationChannelRequest request, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var defaults = ResolveDefaults(request);
        var updated = existing with
        {
            Name = request.Name,
            Description = defaults.Description,
            Type = request.Type,
            WebhookUrl = defaults.WebhookUrl,
            TelegramBotToken = defaults.TelegramBotToken,
            TelegramChatId = defaults.TelegramChatId,
            EmailTo = defaults.EmailTo,
            PagerDutyRoutingKey = defaults.PagerDutyRoutingKey,
            UpdatedAt = timeProvider.GetUtcNow(),
        };

        await InsertChannelVersionAsync(updated, isDeleted: false, cancellationToken);
        return updated;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        var tombstone = existing with { UpdatedAt = timeProvider.GetUtcNow() };
        await InsertChannelVersionAsync(tombstone, isDeleted: true, cancellationToken);
        return true;
    }

    private async Task InsertChannelVersionAsync(NotificationChannel channel, bool isDeleted, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", channel.Id);
        parameters.AddParameter("name", channel.Name);
        parameters.AddParameter("description", channel.Description);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        parameters.AddParameter("type", channel.Type.ToString());
        parameters.AddParameter("webhookUrl", channel.WebhookUrl);
        parameters.AddParameter("telegramBotToken", channel.TelegramBotToken);
        parameters.AddParameter("telegramChatId", channel.TelegramChatId);
        parameters.AddParameter("emailTo", channel.EmailTo);
        parameters.AddParameter("pagerDutyRoutingKey", channel.PagerDutyRoutingKey);
        parameters.AddParameter("createdAt", channel.CreatedAt.UtcDateTime);
        parameters.AddParameter("updatedAt", channel.UpdatedAt.UtcDateTime);

        const string sql = """
            INSERT INTO notification_channels
                (Id, Name, Description, IsDeleted, Type, WebhookUrl, TelegramBotToken, TelegramChatId, EmailTo, PagerDutyRoutingKey, CreatedAt, UpdatedAt)
            VALUES
                ({id:UUID}, {name:String}, {description:String}, {isDeleted:UInt8}, {type:String}, {webhookUrl:String}, {telegramBotToken:String}, {telegramChatId:String}, {emailTo:String}, {pagerDutyRoutingKey:String}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    private static List<NotificationChannel> ReadChannels(ClickHouseDataReader reader)
    {
        var channels = new List<NotificationChannel>();
        while (reader.Read())
        {
            channels.Add(ReadChannel(reader));
        }

        return channels;
    }

    private static NotificationChannel ReadChannel(ClickHouseDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Name = reader.GetString(1),
        Description = reader.GetString(2),
        Type = Enum.Parse<NotificationChannelType>(reader.GetString(3)),
        WebhookUrl = reader.GetString(4),
        TelegramBotToken = reader.GetString(5),
        TelegramChatId = reader.GetString(6),
        EmailTo = reader.GetString(7),
        PagerDutyRoutingKey = reader.GetString(8),
        CreatedAt = ReadUtc(reader, 9),
        UpdatedAt = ReadUtc(reader, 10),
    };

    /// <summary>See <see cref="LogQueryService"/>'s identical helper's remarks - same <c>DateTime64</c>/<c>Kind=Unspecified</c> driver behavior applies here.</summary>
    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    /// <summary>Same query-safety rationale as <see cref="LogQueryService.SafetyOptions"/>, used here for channel CRUD.</summary>
    private static QueryOptions SafetyOptions() => new()
    {
        CustomSettings = new Dictionary<string, object>
        {
            ["max_execution_time"] = 30,
            ["timeout_before_checking_execution_speed"] = 0,
            ["max_rows_to_read"] = 1_000_000_000,
            ["max_result_rows"] = 10_000,
            ["result_overflow_mode"] = "break",
        },
    };
}
