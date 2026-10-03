using Flare.Api.Model;
using Flare.Identity.LlmPrices;

namespace Flare.Api.Query;

/// <summary>
/// Model → price-per-token resolution and the cost estimate for the <c>/llm</c> page - see
/// docs-internal/adr/0101-llm-estimated-cost.md. Pure, so the matching rules are unit-tested once.
/// </summary>
/// <remarks>
/// Built-in defaults are list prices in USD per million tokens, matched by longest prefix so a
/// dated snapshot (<c>gpt-4o-mini-2024-07-18</c>) takes its family's price. They are estimates:
/// providers change prices and discount cached tokens, which Flare doesn't see. An admin override
/// (<see cref="ILlmModelPriceStore"/>) wins and matches the exact model name only.
/// </remarks>
public static class LlmPricing
{
    public const double MaxPricePerMillion = 1_000_000;

    private static readonly (string Prefix, double Input, double Output)[] Defaults =
    [
        ("gpt-4o", 2.50, 10.00),
        ("gpt-4o-mini", 0.15, 0.60),
        ("gpt-4.1", 2.00, 8.00),
        ("gpt-4.1-mini", 0.40, 1.60),
        ("gpt-4.1-nano", 0.10, 0.40),
        ("gpt-5", 1.25, 10.00),
        ("gpt-5-mini", 0.25, 2.00),
        ("gpt-5-nano", 0.05, 0.40),
        ("o3", 2.00, 8.00),
        ("o3-mini", 1.10, 4.40),
        ("o4-mini", 1.10, 4.40),
        ("claude-3-5-haiku", 0.80, 4.00),
        ("claude-3-5-sonnet", 3.00, 15.00),
        ("claude-3-7-sonnet", 3.00, 15.00),
        ("claude-haiku-4-5", 1.00, 5.00),
        ("claude-sonnet-4", 3.00, 15.00),
        ("claude-opus-4", 15.00, 75.00),
        ("text-embedding-3-small", 0.02, 0.00),
        ("text-embedding-3-large", 0.13, 0.00),
        ("gemini-2.0-flash", 0.10, 0.40),
        ("gemini-2.5-flash", 0.30, 2.50),
        ("gemini-2.5-pro", 1.25, 10.00),
    ];

    /// <summary>The price used for <paramref name="model"/>, or null when there is none to estimate with.</summary>
    public static ResolvedPrice? Resolve(string model, IReadOnlyDictionary<string, LlmModelPrice> overrides)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        if (overrides.TryGetValue(model.Trim(), out var custom))
        {
            return new ResolvedPrice(custom.InputPerMillion, custom.OutputPerMillion, IsCustom: true);
        }

        // "openai/gpt-4o" and "models/gemini-2.5-pro" are the same models under a routing prefix.
        var name = model.Trim();
        name = name[(name.LastIndexOf('/') + 1)..];

        (string Prefix, double Input, double Output)? best = null;
        foreach (var candidate in Defaults)
        {
            if (name.StartsWith(candidate.Prefix, StringComparison.OrdinalIgnoreCase) && (best is null || candidate.Prefix.Length > best.Value.Prefix.Length))
            {
                best = candidate;
            }
        }

        return best is { } b ? new ResolvedPrice(b.Input, b.Output, IsCustom: false) : null;
    }

    public static double Cost(ResolvedPrice price, ulong inputTokens, ulong outputTokens) =>
        (inputTokens * price.InputPerMillion + outputTokens * price.OutputPerMillion) / 1_000_000.0;

    /// <summary>Fills each row's price fields. A row with no resolved price keeps them null rather than showing a misleading $0.</summary>
    public static IReadOnlyList<LlmModel> Apply(IReadOnlyList<LlmModel> models, IReadOnlyDictionary<string, LlmModelPrice> overrides) =>
        models
            .Select(m => Resolve(m.Model, overrides) is { } price
                ? m with
                {
                    InputPricePerMillion = price.InputPerMillion,
                    OutputPricePerMillion = price.OutputPerMillion,
                    PriceIsCustom = price.IsCustom,
                    EstimatedCost = Cost(price, m.InputTokens, m.OutputTokens),
                }
                : m)
            .ToList();

    /// <summary>Validates an incoming price. Returns an error message, or null when fine.</summary>
    public static string? Validate(SetLlmModelPriceRequest request, out LlmModelPrice normalized)
    {
        normalized = new LlmModelPrice(request.Model?.Trim() ?? "", request.InputPerMillion, request.OutputPerMillion);

        if (normalized.Model.Length == 0)
        {
            return "model is required.";
        }

        return IsValidPrice(request.InputPerMillion) && IsValidPrice(request.OutputPerMillion)
            ? null
            : $"Prices must be between 0 and {MaxPricePerMillion:N0} USD per million tokens.";
    }

    private static bool IsValidPrice(double value) => double.IsFinite(value) && value >= 0 && value <= MaxPricePerMillion;
}

/// <param name="IsCustom">True for an admin override, false for a built-in default.</param>
public sealed record ResolvedPrice(double InputPerMillion, double OutputPerMillion, bool IsCustom);
