namespace Flare.Api.Query.LogQl;

/// <summary>
/// Thrown by <see cref="LogQlLexer"/>/<see cref="LogQlParser"/> for anything invalid in a
/// SQL-query-row query. <see cref="Exception.Message"/> is written to be shown to the user
/// as-is (see <c>Endpoints.LogsEndpoints.HandleQlQueryAsync</c>) - never a raw parser
/// internals dump.
/// </summary>
public sealed class LogQlParseException(string message) : Exception(message);
