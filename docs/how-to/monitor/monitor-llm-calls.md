# How to monitor LLM calls

See how your services use language models on Flare's **LLM** page. For
each provider and model you get call rate, error rate, latency and
input and output tokens, and one click opens the matching traces.

The page uses spans your applications already send. Flare needs no agent
or ingest change, and it works on spans stored before you opened the
page.

## Prerequisites

- A running Flare instance receiving traces from your applications.
- Model calls instrumented with OpenTelemetry's GenAI conventions. Each
  call must be a span with `gen_ai.operation.name` set to `chat`,
  `text_completion`, `generate_content` or `embeddings`, or with no
  operation but a `gen_ai.request.model`.

## Send model-call spans

### Microsoft.Extensions.AI

Wrap your chat client with `UseOpenTelemetry()` and subscribe the tracer to
the source name you give it:

```csharp
IChatClient client = new ChatClientBuilder(innerClient)
    .UseOpenTelemetry(loggerFactory, sourceName: "Microsoft.Extensions.AI")
    .Build();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Microsoft.Extensions.AI")
        .AddOtlpExporter());
```

Embedding generators work the same way through
`EmbeddingGeneratorBuilder.UseOpenTelemetry()`.

### Semantic Kernel

Semantic Kernel's connectors emit these spans only when you turn on its
experimental diagnostics switch, and your tracer must subscribe to its
sources:

```csharp
AppContext.SetSwitch("Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnostics", true);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Microsoft.SemanticKernel*")
        .AddOtlpExporter());
```

The older `ITextEmbeddingGenerationService` doesn't emit spans. Use
`IEmbeddingGenerator` from Microsoft.Extensions.AI for embeddings.

Point the exporter at Flare's OTLP endpoint as usual.

## Read the LLM page

Open the **⋯** menu at the top right and pick **LLM**. Each row is one
provider and model:

| Column | Meaning |
|---|---|
| Model | `gen_ai.request.model`, else `gen_ai.response.model`. |
| Provider | `gen_ai.provider.name`, else the older `gen_ai.system`. |
| Rate | Calls per second over the window. Hover for the total. |
| Error rate | Share of calls whose span status is `Error`. |
| p95 / p99 | Call duration percentiles, as the caller measured them. |
| Input tokens / Output tokens | Sums of `gen_ai.usage.input_tokens` and `gen_ai.usage.output_tokens`. The older `prompt_tokens` and `completion_tokens` names are read too. |
| Last seen | When the latest call in the window started. |
| Services | How many of your services called the model. |

Use **Calling service** to show only one service's calls, and the window
picker to choose 5 minutes to 24 hours. The page loads on demand; select
**Refresh** to update it. **View traces** opens the trace explorer
filtered to calls to that model.

## What the page leaves out

- **Agent and tool spans.** `invoke_agent`, `create_agent` and
  `execute_tool` spans aren't counted. Agent spans can repeat the tokens of
  the model calls inside them, so counting both would double the totals.
- **Billed cost.** **Est. cost** multiplies the token counts by a price per
  million tokens: a built-in list price for common OpenAI, Anthropic and
  Gemini models (dated snapshots such as `gpt-4o-2024-08-06` take their
  family's price), or one an admin sets with the pencil icon next to a cost.
  Models with neither show **No price**. Cached and discounted tokens aren't
  visible to Flare, so treat the figure as an upper-bound estimate.
- **Request-model matches.** **View traces** filters on
  `gen_ai.request.model`, so a call that set only `gen_ai.response.model`
  appears in the table but not in that trace list.
- **Prompts and responses.** Their content isn't aggregated. Open a trace to
  see whatever your instrumentation recorded on the span.
