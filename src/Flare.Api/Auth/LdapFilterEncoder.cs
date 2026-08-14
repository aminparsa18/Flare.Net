using System.Text;

namespace Flare.Api.Auth;

/// <summary>
/// Escapes a value for safe interpolation into an LDAP search filter, per RFC 4515 -
/// the LDAP-injection equivalent of this repo's parameterized SQL/ClickHouse queries
/// elsewhere. Without this, a submitted username containing a filter metacharacter
/// (e.g. <c>*</c>) could widen <see cref="Identity.Auth.LdapSettings.UserSearchFilter"/>'s
/// match into an unintended wildcard search, or - with <c>)</c>/<c>(</c> - inject
/// additional filter clauses outright.
/// </summary>
public static class LdapFilterEncoder
{
    public static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    builder.Append(@"\5c");
                    break;
                case '*':
                    builder.Append(@"\2a");
                    break;
                case '(':
                    builder.Append(@"\28");
                    break;
                case ')':
                    builder.Append(@"\29");
                    break;
                case '\0':
                    builder.Append(@"\00");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }
        return builder.ToString();
    }
}
