using Microsoft.Data.Sqlite;

namespace Flare.Identity.LlmPrices;

public sealed class SqliteLlmModelPriceStore(IdentityDbConnectionFactory connectionFactory, TimeProvider timeProvider) : ILlmModelPriceStore
{
    public async Task<IReadOnlyDictionary<string, LlmModelPrice>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Model, InputPerMillion, OutputPerMillion FROM LlmModelPrices";

        var result = new Dictionary<string, LlmModelPrice>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var model = reader.GetString(0);
            result[model] = new LlmModelPrice(model, reader.GetDouble(1), reader.GetDouble(2));
        }

        return result;
    }

    public async Task SetAsync(LlmModelPrice price, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO LlmModelPrices (Model, InputPerMillion, OutputPerMillion, UpdatedAt)
            VALUES ($model, $input, $output, $updatedAt)
            ON CONFLICT(Model) DO UPDATE SET
                InputPerMillion = excluded.InputPerMillion,
                OutputPerMillion = excluded.OutputPerMillion,
                UpdatedAt = excluded.UpdatedAt
            """;
        command.Parameters.AddWithValue("$model", price.Model);
        command.Parameters.AddWithValue("$input", price.InputPerMillion);
        command.Parameters.AddWithValue("$output", price.OutputPerMillion);
        command.Parameters.AddWithValue("$updatedAt", timeProvider.GetUtcNow().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ResetAsync(string model, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM LlmModelPrices WHERE Model = $model";
        command.Parameters.AddWithValue("$model", model);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
