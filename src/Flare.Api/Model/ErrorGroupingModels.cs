using MemoryPack;

namespace Flare.Api.Model;

public enum ErrorGroupingKind
{
    /// <summary>Rewrite part of every message with a regular expression (<see cref="ErrorGroupingRule.Pattern"/> to <see cref="ErrorGroupingRule.Replacement"/>).</summary>
    Replace,

    /// <summary>Fold the group whose message is <see cref="ErrorGroupingRule.Pattern"/> into the one whose message is <see cref="ErrorGroupingRule.Replacement"/>, for one exception type.</summary>
    Merge,
}

/// <summary>
/// One custom grouping rule (ADR-0259). <see cref="ErrorGroupingKind.Replace"/> rules run in list order over the message
/// (after the built-in normalization when that is on); <see cref="ErrorGroupingKind.Merge"/> rules then rename whole group messages.
/// </summary>
[MemoryPackable]
public sealed partial record ErrorGroupingRule
{
    public required ErrorGroupingKind Kind { get; init; }

    /// <summary>Merge only: the exact <c>exception.type</c> both groups belong to. Empty for Replace.</summary>
    public string ExceptionType { get; init; } = "";

    /// <summary>Replace: an RE2 regular expression. Merge: the group message being merged away.</summary>
    public required string Pattern { get; init; }

    /// <summary>Replace: the text each match becomes (<c>\1</c> for a capture group; may be empty). Merge: the group message it joins.</summary>
    public string Replacement { get; init; } = "";
}

/// <summary>Body of <c>GET</c> and <c>PUT /api/errors/grouping-rules</c>: the whole configuration, replaced as one.</summary>
[MemoryPackable]
public sealed partial record ErrorGroupingDocument
{
    public const int MaxRules = 200;
    public const int MaxFieldLength = 1000;

    /// <summary>The built-in normalization (GUIDs, numbers, quoted values; ADR-0258). On unless an Admin-level caller switches it off.</summary>
    public bool NormalizeMessages { get; init; } = true;

    public IReadOnlyList<ErrorGroupingRule> Rules { get; init; } = [];

    /// <summary>
    /// The document with every optional field non-null: <c>System.Text.Json</c> leaves an omitted init-only property
    /// <c>null</c> rather than at its initializer (the caveat <c>MetricAlertConditionQueryBuilder</c> also guards against),
    /// and a client may leave out <c>exceptionType</c> or <c>replacement</c>. Call before <see cref="Validate"/>.
    /// </summary>
    public ErrorGroupingDocument Sanitized() => this with
    {
        Rules = (Rules ?? []).Select(r => r with
        {
            ExceptionType = (r.ExceptionType ?? "").Trim(),
            Pattern = r.Pattern ?? "",
            Replacement = r.Replacement ?? "",
        }).ToList(),
    };

    /// <summary>Returns an error message, or null when the structure is valid. Regular expressions are checked separately, by ClickHouse (<c>IErrorGroupingQueryService.ValidateAsync</c>).</summary>
    public string? Validate()
    {
        if (Rules.Count > MaxRules)
        {
            return $"at most {MaxRules} grouping rules are allowed.";
        }

        for (var i = 0; i < Rules.Count; i++)
        {
            var rule = Rules[i];
            var n = i + 1;
            if (!Enum.IsDefined(rule.Kind))
            {
                return $"rule {n}: unknown kind.";
            }

            if (rule.Pattern.Length is 0 || rule.Pattern.Length > MaxFieldLength || rule.Replacement.Length > MaxFieldLength || rule.ExceptionType.Length > MaxFieldLength)
            {
                return $"rule {n}: pattern is required and every field must be at most {MaxFieldLength} characters.";
            }

            if (rule.Kind == ErrorGroupingKind.Merge)
            {
                if (rule.ExceptionType.Length == 0 || rule.Replacement.Length == 0)
                {
                    return $"rule {n}: a merge needs an exception type and a target message.";
                }

                if (rule.Pattern == rule.Replacement)
                {
                    return $"rule {n}: a group cannot be merged into itself.";
                }
            }
        }

        return null;
    }
}
