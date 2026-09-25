# Как отслеживать хосты с помощью OpenTelemetry Collector

Отправляйте метрики CPU, памяти, диска и нагрузки с ваших машин во Flare с
помощью приёмника `hostmetrics` из OpenTelemetry Collector и просматривайте их
на странице **Hosts**: одна строка на хост и подробный график по каждой
метрике.

Страница **Hosts** отличается от **Resources**. Resources показывает
инфраструктуру, которую Flare обнаруживает сам (контейнеры Docker, объекты
Kubernetes и машину, на которой работает Flare). Hosts показывает машины,
которые сами отправляют свои метрики во Flare по OTLP.

## Предварительные требования

- Работающий экземпляр Flare ([автономно](run-standalone.ru.md),
  [Aspire](run-with-aspire.ru.md) или [CLI](run-with-cli.ru.md)), порт OTLP
  которого (`4317` gRPC или `4318` HTTP) доступен с отслеживаемых хостов.
- Дистрибутив [OpenTelemetry Collector Contrib](https://github.com/open-telemetry/opentelemetry-collector-contrib)
  (`otelcol-contrib`) на каждом хосте. Базовый дистрибутив не включает
  процессор `resourcedetection`.

## Настройка коллектора

На каждом хосте направьте коллектор во Flare с такой конфигурацией:

```yaml
receivers:
  hostmetrics:
    collection_interval: 60s
    scrapers:
      cpu: {}
      memory: {}
      load: {}
      filesystem: {}

processors:
  resourcedetection:
    detectors: [system]

exporters:
  otlp:
    endpoint: flare.example.internal:4317   # ваш хост Flare.Ingest
    tls:
      insecure: true                        # или настройте TLS

service:
  pipelines:
    metrics:
      receivers: [hostmetrics]
      processors: [resourcedetection]
      exporters: [otlp]
```

Процессор `resourcedetection` обязателен. Он задаёт атрибуты ресурса
`host.name` и `os.type`, а Flare определяет хосты по `host.name`. Метрики без
него на странице Hosts не отображаются.

Если вы включили [ключи API приёма данных](configure-authentication.ru.md#ключи-api-приёма-данных),
добавьте ключ в `headers` экспортёра.

### Запуск коллектора в контейнере

Коллектор в контейнере сообщает о файловых системах самого контейнера, если не
смонтировать корень хоста и не задать `root_path`:

```yaml
receivers:
  hostmetrics:
    root_path: /hostfs
```

```bash
docker run -v /:/hostfs:ro --hostname "$(hostname)" ... otel/opentelemetry-collector-contrib
```

Без этого столбец **Disk** остаётся пустым (—). Также передайте `--hostname`,
иначе `host.name` будет идентификатором контейнера.

## Чтение страницы Hosts

Откройте **Hosts** в верхней навигации. Хосты появляются в течение одного
интервала сбора.

| Столбец | Исходная метрика | Значение |
|---|---|---|
| CPU | `system.cpu.time` | Доля времени CPU вне простоя за окно |
| Memory | `system.memory.usage` | Доля `used` от общей памяти, усреднённая за окно |
| Disk | `system.filesystem.usage` | Доля `used` от общей ёмкости, суммированная по всем файловым системам |
| Load (15m) | `system.cpu.load_average.15m` | Средняя нагрузка за 15 минут, усреднённая за окно |
| Last seen | любая метрика `system.*` | Когда хост последний раз отправлял данные |

**—** означает, что хост не отправлял данные по этой метрике в окне, например
потому что скрейпер `filesystem` отключён. Это никогда не отображается как
0 %. Хост, не отправлявший данные более пяти минут, помечается как **stale**.

- **Фильтруйте** по имени хоста (подстрока, без учёта регистра) или по типу ОС.
- **Сортируйте**, щёлкая по заголовку столбца.
- **Меняйте окно** селектором времени (от 5 минут до 24 часов).
- **Детализация**: щёлкните по имени хоста, чтобы открыть графики всех
  четырёх метрик за выбранное окно.

Страница показывает до 500 хостов. Если совпадений больше, появится
уведомление с просьбой сузить фильтр.

## Устранение неполадок

**Хост не появляется.** Проверьте журналы коллектора на ошибки экспорта, затем
убедитесь, что его метрики содержат `host.name`. Хост появляется, только если
отправил хотя бы одну метрику `system.*` в выбранном окне.

**CPU, Memory или Load всегда пусты.** Читаются только метрики приёмника по
умолчанию. Flare не использует необязательные датчики
`system.cpu.utilization`, `system.memory.utilization` и
`system.filesystem.utilization`, поэтому убедитесь, что скрейперы `cpu`,
`memory` и `load` включены.
