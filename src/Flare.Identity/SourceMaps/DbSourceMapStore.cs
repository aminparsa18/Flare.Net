using System.Globalization;

namespace Flare.Identity.SourceMaps;

public sealed class DbSourceMapStore(IdentityDbConnectionFactory connectionFactory, TimeProvider timeProvider) : ISourceMapStore
{
    public async Task UpsertAsync(string serviceName, string version, string bundle, byte[] gzipContent, long sizeBytes, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO SourceMaps (ServiceName, Version, Bundle, Content, SizeBytes, UploadedAt)
            VALUES (@serviceName, @version, @bundle, @content, @sizeBytes, @uploadedAt)
            ON CONFLICT(ServiceName, Version, Bundle) DO UPDATE SET
                Content = excluded.Content,
                SizeBytes = excluded.SizeBytes,
                UploadedAt = excluded.UploadedAt
            """;
        command.AddParameter("@serviceName", serviceName);
        command.AddParameter("@version", version);
        command.AddParameter("@bundle", bundle);
        command.AddParameter("@content", gzipContent);
        command.AddParameter("@sizeBytes", sizeBytes);
        command.AddParameter("@uploadedAt", timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SourceMapInfo>> ListAsync(string? serviceName, string? version, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // Conditions are appended rather than `@p IS NULL OR ...`: Postgres can't infer the type of an untyped NULL parameter.
        var where = new List<string>();
        if (serviceName is not null)
        {
            where.Add("ServiceName = @serviceName");
            command.AddParameter("@serviceName", serviceName);
        }

        if (version is not null)
        {
            where.Add("Version = @version");
            command.AddParameter("@version", version);
        }

        command.CommandText =
            "SELECT ServiceName, Version, Bundle, SizeBytes, UploadedAt FROM SourceMaps" +
            (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "") +
            " ORDER BY ServiceName, UploadedAt DESC, Bundle";

        var result = new List<SourceMapInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new SourceMapInfo(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3),
                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return result;
    }

    public async Task<int> DeleteAsync(string serviceName, string version, string? bundle, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM SourceMaps WHERE ServiceName = @serviceName AND Version = @version" + (bundle is null ? "" : " AND Bundle = @bundle");
        command.AddParameter("@serviceName", serviceName);
        command.AddParameter("@version", version);
        if (bundle is not null)
        {
            command.AddParameter("@bundle", bundle);
        }
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListBundlesAsync(string serviceName, string version, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Bundle FROM SourceMaps WHERE ServiceName = @serviceName AND Version = @version";
        command.AddParameter("@serviceName", serviceName);
        command.AddParameter("@version", version);

        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    public async Task<(byte[] GzipContent, DateTimeOffset UploadedAt)?> GetAsync(string serviceName, string version, string bundle, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Content, UploadedAt FROM SourceMaps WHERE ServiceName = @serviceName AND Version = @version AND Bundle = @bundle";
        command.AddParameter("@serviceName", serviceName);
        command.AddParameter("@version", version);
        command.AddParameter("@bundle", bundle);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ((byte[])reader.GetValue(0), DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }
}
