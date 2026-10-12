# 如何监控 LLM 调用

在 Flare 的 **LLM** 页面查看你的服务如何使用语言模型。每个提供方和模型都有
调用速率、错误率、延迟以及输入和输出 token，一次点击即可打开对应的链路。

该页面使用你的应用已经发送的 span。Flare 不需要代理，也不需要修改摄取，并且
对在你打开页面之前存储的 span 同样有效。

## 前提条件

- 一个正在运行、并接收你应用链路数据的 Flare 实例。
- 按 OpenTelemetry GenAI 约定埋点的模型调用。每次调用必须是一个 span，其
  `gen_ai.operation.name` 为 `chat`、`text_completion`、`generate_content` 或
  `embeddings`，或者没有操作名但带有 `gen_ai.request.model`。

## 发送模型调用 span

### Microsoft.Extensions.AI

用 `UseOpenTelemetry()` 包装你的聊天客户端，并让 tracer 订阅你指定的来源名：

```csharp
IChatClient client = new ChatClientBuilder(innerClient)
    .UseOpenTelemetry(loggerFactory, sourceName: "Microsoft.Extensions.AI")
    .Build();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Microsoft.Extensions.AI")
        .AddOtlpExporter());
```

嵌入生成器的用法相同，通过 `EmbeddingGeneratorBuilder.UseOpenTelemetry()`。

### Semantic Kernel

只有开启其实验性诊断开关，Semantic Kernel 的连接器才会产生这些 span，并且你的
tracer 必须订阅它的来源：

```csharp
AppContext.SetSwitch("Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnostics", true);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Microsoft.SemanticKernel*")
        .AddOtlpExporter());
```

较旧的 `ITextEmbeddingGenerationService` 不产生 span。嵌入请使用
Microsoft.Extensions.AI 的 `IEmbeddingGenerator`。

像往常一样，把导出器指向 Flare 的 OTLP 端点。

## 阅读 LLM 页面

打开右上角的 **⋯** 菜单并选择 **LLM**。每一行是一个提供方和模型：

| 列 | 含义 |
|---|---|
| Model | `gen_ai.request.model`，否则为 `gen_ai.response.model`。 |
| Provider | `gen_ai.provider.name`，否则为较旧的 `gen_ai.system`。 |
| Rate | 时间窗口内每秒调用次数。悬停可查看总数。 |
| Error rate | span 状态为 `Error` 的调用占比。 |
| p95 / p99 | 调用耗时百分位，按调用方的测量值。 |
| Input tokens / Output tokens | `gen_ai.usage.input_tokens` 与 `gen_ai.usage.output_tokens` 之和。也会读取较旧的 `prompt_tokens` 和 `completion_tokens`。 |
| Last seen | 窗口内最近一次调用的开始时间。 |
| Services | 有多少个你的服务调用了该模型。 |

使用 **Calling service** 只显示某个服务的调用，并用时间窗口选择器选择 5 分钟到
24 小时。页面按需加载；选择 **Refresh** 进行更新。**View traces** 会打开按该
模型的调用过滤的链路浏览器。

## 页面不包含的内容

- **智能体和工具 span。** `invoke_agent`、`create_agent` 和 `execute_tool` span
  不计入。智能体 span 可能重复其内部模型调用的 token，两者都计会使总数翻倍。
- **实际账单费用。** **Est. cost** 用 token 数乘以每百万 token 的价格：常见 OpenAI、
  Anthropic 和 Gemini 模型使用内置标价（`gpt-4o-2024-08-06` 这类带日期的快照沿用其
  系列价格），也可以由管理员通过费用旁的铅笔图标设置。两者都没有的模型显示
  **No price**。Flare 看不到缓存和折扣的 token，因此请把该数字当作上限估算。
- **按请求模型匹配。** **View traces** 按 `gen_ai.request.model` 过滤，因此只设置了
  `gen_ai.response.model` 的调用会出现在表中，但不会出现在该链路列表里。
- **提示词和响应。** 它们的内容不做聚合。打开链路即可查看你的埋点记录在 span
  上的内容。
