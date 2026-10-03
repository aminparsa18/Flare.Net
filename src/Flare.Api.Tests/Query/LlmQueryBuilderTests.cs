using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class LlmQueryBuilderTests
{
    private static readonly DateTimeOffset End = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, LlmQueryBuilder.DefaultWindowMinutes)]
    [InlineData(0, LlmQueryBuilder.DefaultWindowMinutes)]
    [InlineData(1, LlmQueryBuilder.MinWindowMinutes)]
    [InlineData(15, 15)]
    [InlineData(100_000, LlmQueryBuilder.MaxWindowMinutes)]
    public void ClampWindowMinutes_ClampsAndDefaults(int? requested, int expected)
    {
        Assert.Equal(expected, LlmQueryBuilder.ClampWindowMinutes(requested));
    }

    [Fact]
    public void AttributeExprs_PreferCurrentNames_ThenFallBackToOlder()
    {
        Assert.Equal(
            "if(SpanAttributes['gen_ai.provider.name'] != '', SpanAttributes['gen_ai.provider.name'], SpanAttributes['gen_ai.system'])",
            LlmQueryBuilder.ProviderExpr);
        Assert.Equal(
            "if(SpanAttributes['gen_ai.request.model'] != '', SpanAttributes['gen_ai.request.model'], SpanAttributes['gen_ai.response.model'])",
            LlmQueryBuilder.ModelExpr);
        Assert.Contains("'gen_ai.usage.input_tokens'], SpanAttributes['gen_ai.usage.prompt_tokens']", LlmQueryBuilder.InputTokensExpr);
        Assert.Contains("'gen_ai.usage.output_tokens'], SpanAttributes['gen_ai.usage.completion_tokens']", LlmQueryBuilder.OutputTokensExpr);
        Assert.StartsWith("toUInt64OrZero(", LlmQueryBuilder.InputTokensExpr);
    }

    [Fact]
    public void ModelCallCondition_CountsModelOperations_ButNotAgentOrToolSpans()
    {
        var condition = LlmQueryBuilder.ModelCallCondition;

        Assert.Contains("IN ('chat', 'text_completion', 'generate_content', 'embeddings')", condition);
        Assert.DoesNotContain("invoke_agent", condition);
        Assert.DoesNotContain("execute_tool", condition);
        // No operation set: a model is enough.
        Assert.Contains("SpanAttributes['gen_ai.operation.name'] = '' AND " + LlmQueryBuilder.ModelExpr + " != ''", condition);
    }

    [Fact]
    public void BuildModels_GroupsByProviderAndModel_GuardedByAttributeKey()
    {
        var built = LlmQueryBuilder.BuildModels(new LlmModelsRequest(), 60, End);

        Assert.Contains("FROM spans", built.Sql);
        Assert.Contains("mapContains(SpanAttributes, 'gen_ai.operation.name')", built.Sql);
        Assert.Contains(LlmQueryBuilder.ModelCallCondition, built.Sql);
        Assert.Contains("GROUP BY LlmProvider, LlmModel", built.Sql);
        Assert.Contains("sum(InputTokens) AS InputTokens", built.Sql);
        Assert.Contains("LIMIT {limit:UInt32}", built.Sql);
        Assert.DoesNotContain("{service:String}", built.Sql);
    }

    [Fact]
    public void BuildModels_ServiceFilter_IsABoundParameter()
    {
        var built = LlmQueryBuilder.BuildModels(new LlmModelsRequest { Service = "checkout" }, 60, End);

        Assert.Contains("ServiceName = {service:String}", built.Sql);
        Assert.DoesNotContain("checkout", built.Sql);
    }

    [Fact]
    public void BuildFacets_IgnoresTheServiceFilter()
    {
        var built = LlmQueryBuilder.BuildFacets(60, End);

        Assert.Contains("groupUniqArray(1000)(ServiceName)", built.Sql);
        Assert.DoesNotContain("{service:String}", built.Sql);
    }
}
