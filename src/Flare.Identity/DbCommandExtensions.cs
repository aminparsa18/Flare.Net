using System.Data.Common;

namespace Flare.Identity;

/// <summary>
/// Provider-neutral parameter binding for the identity stores. <c>DbParameterCollection</c>
/// has no <c>AddWithValue</c> (that is a <c>Microsoft.Data.Sqlite</c>/<c>Npgsql</c>
/// convenience, not part of <c>System.Data.Common</c>), so the stores go through this
/// instead and stay free of any provider's types. Both SQLite and Postgres accept the
/// <c>@name</c> parameter prefix used throughout.
/// </summary>
internal static class DbCommandExtensions
{
    public static void AddParameter(this DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
