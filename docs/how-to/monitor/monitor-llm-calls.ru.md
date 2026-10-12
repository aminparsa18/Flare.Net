# Как отслеживать вызовы LLM

На странице **LLM** в Flare видно, как ваши сервисы используют языковые
модели. Для каждого провайдера и модели доступны частота вызовов, доля ошибок,
задержка и входные и выходные токены, а один клик открывает соответствующие
трейсы.

Страница использует спаны, которые ваши приложения уже отправляют. Flare не
требует ни агента, ни изменений приёма данных и работает со спанами,
сохранёнными до того, как вы открыли страницу.

## Требования

- Запущенный экземпляр Flare, который получает трейсы ваших приложений.
- Вызовы моделей, инструментированные по соглашениям OpenTelemetry GenAI.
  Каждый вызов должен быть спаном, у которого `gen_ai.operation.name` равен
  `chat`, `text_completion`, `generate_content` или `embeddings`, либо без
  операции, но с `gen_ai.request.model`.

## Отправка спанов вызовов моделей

### Microsoft.Extensions.AI

Оберните чат-клиент в `UseOpenTelemetry()` и подпишите трейсер на заданное
имя источника:

```csharp
IChatClient client = new ChatClientBuilder(innerClient)
    .UseOpenTelemetry(loggerFactory, sourceName: "Microsoft.Extensions.AI")
    .Build();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Microsoft.Extensions.AI")
        .AddOtlpExporter());
```

Генераторы эмбеддингов работают так же, через
`EmbeddingGeneratorBuilder.UseOpenTelemetry()`.

### Semantic Kernel

Коннекторы Semantic Kernel выдают эти спаны, только если включить его
экспериментальный переключатель диагностики, а трейсер должен быть подписан на
его источники:

```csharp
AppContext.SetSwitch("Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnostics", true);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Microsoft.SemanticKernel*")
        .AddOtlpExporter());
```

Старый `ITextEmbeddingGenerationService` спаны не выдаёт. Для эмбеддингов
используйте `IEmbeddingGenerator` из Microsoft.Extensions.AI.

Направьте экспортёр на OTLP-конечную точку Flare, как обычно.

## Страница LLM

Откройте меню **⋯** в правом верхнем углу и выберите **LLM**. Каждая строка —
один провайдер и модель:

| Столбец | Значение |
|---|---|
| Model | `gen_ai.request.model`, иначе `gen_ai.response.model`. |
| Provider | `gen_ai.provider.name`, иначе старый `gen_ai.system`. |
| Rate | Вызовов в секунду за окно. Наведите курсор, чтобы увидеть общее число. |
| Error rate | Доля вызовов со статусом спана `Error`. |
| p95 / p99 | Перцентили длительности вызовов в замере вызывающей стороны. |
| Input tokens / Output tokens | Суммы `gen_ai.usage.input_tokens` и `gen_ai.usage.output_tokens`. Старые имена `prompt_tokens` и `completion_tokens` тоже читаются. |
| Last seen | Когда начался последний вызов в окне. |
| Services | Сколько ваших сервисов вызывали модель. |

Фильтр **Calling service** оставляет вызовы одного сервиса, а селектор окна
задаёт период от 5 минут до 24 часов. Страница загружается по запросу; нажмите
**Refresh**, чтобы обновить её. **View traces** открывает обозреватель трейсов,
отфильтрованный по вызовам этой модели.

## Что страница не учитывает

- **Спаны агентов и инструментов.** Спаны `invoke_agent`, `create_agent` и
  `execute_tool` не учитываются. Спан агента может повторять токены вызовов
  модели внутри него, и подсчёт обоих удвоил бы итоги.
- **Фактические расходы.** **Est. cost** умножает число токенов на цену за
  миллион токенов: встроенный прайс-лист для распространённых моделей OpenAI,
  Anthropic и Gemini (датированные снимки вроде `gpt-4o-2024-08-06` берут цену
  своего семейства) или цену, заданную администратором через карандаш рядом со
  стоимостью. У моделей без цены показано **No price**. Flare не видит
  кэшированные токены и скидки, поэтому считайте цифру верхней оценкой.
- **Совпадение по запрошенной модели.** **View traces** фильтрует по
  `gen_ai.request.model`, поэтому вызов, у которого задан только
  `gen_ai.response.model`, есть в таблице, но не попадает в этот список
  трейсов.
- **Промпты и ответы.** Их содержимое не агрегируется. Откройте трейс, чтобы
  увидеть то, что ваша инструментация записала в спан.
