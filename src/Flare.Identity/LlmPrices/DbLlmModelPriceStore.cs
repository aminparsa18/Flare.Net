using System.Data.Common;

namespace Flare.Identity.LlmPrices;

public sealed class DbLlmModelPriceStore(IdentityDbConnectionFactory connectionFactory, TimeProvider timeProvider) : ILlmModelPriceStore
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
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Delete-then-insert rather than ON CONFLICT(Model): the model key is matched
        // case-insensitively, which SQLite gets from the column's COLLATE NOCASE and
        // Postgres has no column-level equivalent for - LOWER() on both sides behaves the
        // same on either provider.
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM LlmModelPrices WHERE LOWER(Model) = LOWER(@model)";
            delete.AddParameter("@model", price.Model);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO LlmModelPrices (Model, InputPerMillion, OutputPerMillion, UpdatedAt)
                VALUES (@model, @input, @output, @updatedAt)
                """;
            insert.AddParameter("@model", price.Model);
            insert.AddParameter("@input", price.InputPerMillion);
            insert.AddParameter("@output", price.OutputPerMillion);
            insert.AddParameter("@updatedAt", timeProvider.GetUtcNow().ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ResetAsync(string model, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM LlmModelPrices WHERE LOWER(Model) = LOWER(@model)";
        command.AddParameter("@model", model);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
