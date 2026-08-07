using ClickHouse.Driver.ADO.Parameters;

namespace Flare.Api.Tests.Query;

/// <summary>
/// <see cref="ClickHouseParameterCollection"/> only exposes non-generic
/// <see cref="System.Collections.IEnumerable"/> - this flattens it into a plain
/// dictionary so tests can assert on parameter names/values without repeating that
/// enumeration dance everywhere.
/// </summary>
internal static class ParameterAssertions
{
    public static Dictionary<string, object?> ToDictionary(this ClickHouseParameterCollection parameters)
    {
        var result = new Dictionary<string, object?>();
        foreach (ClickHouseDbParameter parameter in parameters)
        {
            result[parameter.ParameterName] = parameter.Value;
        }

        return result;
    }
}
