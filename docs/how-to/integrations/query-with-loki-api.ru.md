# Как читать логи Flare из Grafana через API Loki

Flare предоставляет **доступный только для чтения поднабор HTTP API Loki** поверх ваших логов, поэтому источник данных Loki в Grafana может выполнять LogQL к Flare: Explore, панели логов, объём логов, выбор лейблов и ссылки из трейса в логи. Поддерживаются селекторы потоков, фильтры строк, `| json`, фильтры по лейблам и функции, превращающие логи в метрики (`rate`, `count_over_time`, `bytes_rate`, `bytes_over_time`), вместе с `sum`, `topk` и арифметикой. Всё остальное отклоняется ошибкой с названием неподдерживаемой конструкции и никогда не вычисляется частично.

## Подключение Grafana

1. Создайте [персональный токен доступа](../admin/configure-authentication.ru.md#персональные-токены-доступа).
2. В Grafana добавьте источник данных **Loki**.
3. В поле **URL** укажите базовый адрес API Flare (`http://localhost:8080` в автономном стеке Docker). Flare отдаёт API по пути `/loki/api/v1`, где его ожидает Grafana.
4. В разделе **Authentication** добавьте пользовательский HTTP-заголовок `Authorization` со значением `Bearer flr_pat_...`.
5. Нажмите **Save & test**.

## Ссылка из трейса на его логи

Когда [источник данных Tempo](query-with-tempo-api.ru.md) настроен, откройте его параметры **Trace to logs**, выберите источник Loki и сопоставьте тег `service.name` с лейблом `service_name`. Включите **Use custom query** и укажите

```
{${__tags}} | trace_id="${__trace.traceId}"
```

Flare хранит идентификаторы трейса и спана каждой записи как `trace_id` и `span_id`, поэтому совпадение точное. Параметр по умолчанию **Filter by trace ID** ищет идентификатор в тексте лога (`|= "<id>"`) и находит только те логи, которые его печатают.

## Пример с curl

```bash
export FLARE=http://localhost:8080 TOKEN=flr_pat_...

# Строки логов: ошибки одного сервиса, где упоминается таймаут
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query={service_name="shop-api"} |= "timeout" | detected_level="error"' \
  --data-urlencode limit=20 \
  $FLARE/loki/api/v1/query_range

# Объём логов по уровням, одна точка в минуту
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query=sum by (detected_level) (count_over_time({service_name="shop-api"}[1m]))' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" --data-urlencode step=60 \
  $FLARE/loki/api/v1/query_range
```

`start` и `end` принимают наносекунды Unix (так присылает Grafana), секунды или RFC 3339. По умолчанию это последний час.

## Лейблы

Flare сопоставляет свои поля с лейблами Loki так же, как собственный приём OTLP в Loki:

| Flare | Лейбл Loki |
|---|---|
| сервис | `service_name` |
| уровень важности (сведён к trace, debug, info, warn, error, fatal, unknown) | `detected_level` (также `level` до этапа `\| json`) |
| текст уровня важности | `severity_text` |
| область инструментирования | `scope_name` |
| идентификатор трейса, спана | `trace_id`, `span_id` |
| атрибут ресурса `k8s.pod.name` | `k8s_pod_name` (точки становятся подчёркиваниями) |
| атрибут лога `http.method` | `http_method` |

В ответе **поток** — это `service_name` плюс атрибуты ресурса, которые Loki индексирует по умолчанию (`k8s.namespace.name`, `k8s.pod.name`, `deployment.environment` и остальные из списка Loki). Всё остальное — **структурированные метаданные** каждой записи: Grafana показывает их в подробностях лога, не дробя поток.

В отличие от Loki, в селекторе потока можно использовать любой лейбл, включая атрибуты логов и перечисленные идентификаторы. `GET /loki/api/v1/labels` перечисляет атрибуты ресурса, встречавшиеся в окне.

## Что поддерживается

| Конструкция | Пример |
|---|---|
| Селектор потока с `=`, `!=`, `=~`, `!~` | `{service_name="api", k8s_namespace_name=~"prod-.*"}` |
| Фильтры строк: `\|=`, `!=`, `\|~`, `!~`, с `or` | `{service_name="api"} \|= "timeout" or "refused" != "health"` |
| `\| json`, без аргументов или с выражениями | `\| json uid="user.id"` |
| Фильтры лейблов: `=`, `!=`, `=~`, `!~`, `>`, `>=`, `<`, `<=`, связанные `and`, `,`, `or` и скобками | `\| json \| status >= 500 and method="POST"` |
| `\| drop __error__` | принимается, ничего не делает |
| `count_over_time`, `rate`, `bytes_over_time`, `bytes_rate` | `rate({service_name="api"} \|= "error" [5m])` |
| `sum`, `avg`, `min`, `max`, `count` с `by`/`without` | `sum by (service_name) (rate({service_name=~".+"}[5m]))` |
| `topk`, `bottomk` | `topk(5, sum by (service_name) (rate({service_name=~".+"}[5m])))` |
| `+ - * / %` между выражениями и числами, `vector(n)` | `sum(rate({...} \|= "error" [5m])) / sum(rate({...}[5m]))` |

Конечные точки: `query_range`, `query` (метрические запросы), `labels`, `label/<name>/values`, `series`, `index/stats`, `status/buildinfo`. Работают GET и POST с формой.

Не поддерживаются: `logfmt`, `line_format`, `label_format`, `pattern`, `regexp`, `unpack`, `unwrap` и требующие его функции `*_over_time`, `offset`, операторы сравнения и множеств (`>`, `and`, `or`, `unless`) между метриками, сопоставление `on`/`ignoring`, live tail (`/tail`), а также `detected_fields` и `patterns` из Logs Drilldown.

## Отличия от Loki

- **`| json` склеивает вложенные ключи через `_`**, как и Loki (`{"request":{"method":"GET"}}` даёт `request_method`). Фильтр по склеенному имени пробует все способы разбить его на ключи, поэтому `request_user_id` находит и `request.user.id`, и ключ с именем `request_user_id`. В выражениях `| json label="path"` путь записывается через точки; индексы массивов не поддерживаются.
- **После `| json` поле `level` приоритетнее** уровня, выведенного из важности, а если в строке такого поля нет, берётся уровень из важности.
- **Регулярные выражения лейблов привязаны к границам** (`=~"web"` совпадает только с `web`), как в Loki; **регулярные выражения строк — нет** (`|~"5\d\d"` совпадает в любом месте строки). `|=` учитывает регистр.
- **Окна метрик строятся по корзинам.** Агрегация по диапазону читает корзины шириной в наибольший общий делитель диапазона и шага (в шаг, если корзин получилось бы слишком много), выровненные по шагам запроса. У шага без подходящих строк нет значения.
- **Ограничения.** Запрос логов возвращает не более 5000 строк (`limit` по умолчанию 100); метрический запрос завершается ошибкой, а не усечением, при более чем 500 рядах или 11 000 точках на ряд.
- **Некорректные регулярные выражения** обнаруживаются при выполнении запроса к данным, в синтаксисе RE2.
- **Мгновенные запросы** работают только для метрик; для селектора логов нужен `query_range`.

Заметки о дизайне: [ADR-0236](../../../docs-internal/adr/0236-loki-compatible-log-api.md).
