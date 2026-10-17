# Как читать логи Flare из Grafana через API Loki

Flare предоставляет **доступный только для чтения поднабор HTTP API Loki** поверх ваших логов, поэтому источник данных Loki в Grafana может выполнять LogQL к Flare: Explore, панели логов, объём логов, выбор лейблов, live tail, ссылки из трейса в логи и приложение Logs Drilldown. Поддерживаются селекторы потоков, фильтры строк, `| json`, `| logfmt`, `| line_format`, фильтры по лейблам, функции, превращающие логи в метрики (`rate`, `count_over_time`, `bytes_rate`, `bytes_over_time`), `absent_over_time` и функции с `unwrap` (`avg_over_time`, `quantile_over_time`, `rate_counter` и другие), вместе с `sum`, `topk` и арифметикой. Всё остальное отклоняется ошибкой с названием неподдерживаемой конструкции и никогда не вычисляется частично.

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

## Просмотр логов в Logs Drilldown

Приложение Grafana **Logs Drilldown** (плагин `grafana-lokiexplore-app`; *Drilldown > Logs* в Grafana 12 и новее) работает с тем же источником данных без дополнительной настройки: список сервисов, обзор с объёмом по уровням, вкладка **Fields** (числовые поля рисуются через `unwrap`) и вкладка **Patterns** читают данные Flare. Приложение использует такие конечные точки:

| Конечная точка | Что возвращает Flare |
|---|---|
| `index/volume` | байты и записи по сервисам (или по `targetLabels`) |
| `detected_labels` | лейблы потока в окне и число значений у каждого |
| `detected_fields` | поля из 1000 самых новых строк, подходящих под запрос: структурированные метаданные, поля `\| json` и `\| logfmt`, с типом (`int`, `float`, `boolean`, `duration`, `bytes`, `string`) и числом различных значений |
| `detected_field/<name>/values` | значения одного поля |
| `patterns` | кластеры [шаблонов логов](../../explanation/clustering.ru.md) Flare: число строк во времени по уровням, с `<_>` на месте переменных частей |

Выбор шаблона на вкладке Patterns добавляет в запрос фильтр строк по шаблону (`|> "<_> failed to connect <_>"`); Flare его поддерживает. Поля берутся из выборки, поэтому поле, которое есть только в более старых или более редких строках, в списке не появится.

## Просмотр логов в реальном времени

Включите **Live** в Grafana Explore (или вызовите `GET /loki/api/v1/tail?query=...` по WebSocket), чтобы получать записи, подходящие под запрос логов, по мере их приёма. Запрос выполняется в памяти для каждого нового лога, с теми же лейблами, `| json`, `| logfmt` и `| line_format`, что и обычный запрос. Передайте `start` (в наносекундах), чтобы сначала получить до `limit` более старых записей; без него приходят только новые.

```bash
websocat -H "Authorization: Bearer $TOKEN" \
  'ws://localhost:8080/loki/api/v1/tail?query=%7Bservice_name%3D%22shop-api%22%7D%20%7C%3D%20%22error%22'
```

Если соединение не успевает, следующий кадр перечисляет пропущенное как `dropped_entries` Loki (`[{"labels": {...}, "timestamp": "<ns>"}]`) — для записей, которые подошли бы под запрос. Flare помнит выборку не более чем из 100 потерянных записей за раз, поэтому большой всплеск сообщает о меньшем числе, чем потеряно.

У live tail два ограничения. Регулярные выражения должны выполняться за линейное время (без опережающих проверок и обратных ссылок), как и в live tail дашборда; запрос, нарушающий это, отклоняется с указанием шаблона. И соединение закрывается через несколько минут (`LiveTail:MaxSocketLifetime`, по умолчанию пять), чтобы токен проверялся заново; live-режим Grafana переподключается сам. Если Grafana открыта с origin, которого нет в `Cors:AllowedOrigins`, менять ничего не нужно: запрос с заголовком `Authorization` пропускает проверку origin.

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
| `\| logfmt`, без аргументов или с выражениями; несколько парсеров подряд | `\| logfmt \| level="error"`, `\| json \| logfmt` |
| `\| logfmt --strict`, `--keep-empty` (см. ниже) | `\| logfmt --strict \| __error__=""` |
| `\| line_format` (см. ниже) | `\| line_format "{{.method}} {{.path}} {{.status \| default \"-\"}}"` |
| Фильтры строк по шаблону `\|>` и `!>` | `\|> "<_> failed to connect <_>"` |
| Фильтры лейблов: `=`, `!=`, `=~`, `!~`, `>`, `>=`, `<`, `<=`, связанные `and`, `,`, `or` и скобками | `\| json \| status >= 500 and method="POST"` |
| `\| drop __error__` | принимается; сбрасывает ошибку преобразования, возникшую в предшествующем `unwrap` |
| `count_over_time`, `rate`, `bytes_over_time`, `bytes_rate` | `rate({service_name="api"} \|= "error" [5m])` |
| `absent_over_time` | `absent_over_time({service_name="worker"} \|= "heartbeat" [5m])` |
| `\| unwrap label`, `unwrap duration(label)`, `unwrap bytes(label)` с `sum_over_time`, `avg_over_time`, `min_over_time`, `max_over_time`, `first_over_time`, `last_over_time`, `stddev_over_time`, `stdvar_over_time`, `quantile_over_time`, у каждой необязательные `by`/`without` | `quantile_over_time(0.95, {service_name="api"} \| json \| unwrap latency_ms \| __error__="" [5m]) by (k8s_pod_name)` |
| `rate_counter`, с необязательными `by`/`without` | `rate_counter({service_name="api"} \| logfmt \| unwrap bytes_total \| __error__="" [5m])` |
| `sum`, `avg`, `min`, `max`, `count` с `by`/`without` | `sum by (service_name) (rate({service_name=~".+"}[5m]))` |
| `topk`, `bottomk` | `topk(5, sum by (service_name) (rate({service_name=~".+"}[5m])))` |
| `+ - * / %` между выражениями и числами, `vector(n)` | `sum(rate({...} \|= "error" [5m])) / sum(rate({...}[5m]))` |

Конечные точки: `query_range`, `query` (метрические запросы), `labels`, `label/<name>/values`, `series`, `index/stats`, `index/volume`, `detected_labels`, `detected_fields`, `detected_field/<name>/values`, `patterns`, `tail` (WebSocket), `status/buildinfo`. Работают GET и POST с формой.

Не поддерживаются: `label_format`, `pattern`, `regexp`, `unpack`, `offset`, операторы сравнения и множеств (`>`, `and`, `or`, `unless`) между метриками, сопоставление `on`/`ignoring`, а также `index/volume_range`.

## Отличия от Loki

- **`| json` склеивает вложенные ключи через `_`**, как и Loki (`{"request":{"method":"GET"}}` даёт `request_method`). Фильтр по склеенному имени пробует все способы разбить его на ключи, поэтому `request_user_id` находит и `request.user.id`, и ключ с именем `request_user_id`. В выражениях `| json label="path"` путь можно записывать через точки (`request.method`), в скобках (`["request"]["method"]`, так пишет Logs Drilldown) или с индексами массивов (`servers[0].name`).
- **После `| json` поле `level` приоритетнее** уровня, выведенного из важности, а если в строке такого поля нет, берётся уровень из важности.
- **Регулярные выражения лейблов привязаны к границам** (`=~"web"` совпадает только с `web`), как в Loki; **регулярные выражения строк — нет** (`|~"5\d\d"` совпадает в любом месте строки). `|=` учитывает регистр.
- **Окна метрик строятся по корзинам.** Агрегация по диапазону читает корзины шириной в наибольший общий делитель диапазона и шага (в шаг, если корзин получилось бы слишком много), выровненные по шагам запроса. У шага без подходящих строк нет значения.
- **`| line_format` переписывает только возвращаемую строку.** Шаблоны поддерживают `{{.label}}`, `{{ __line__ }}`, `{{ __timestamp__ }}`, строковые и числовые литералы, конвейеры и функции `lower`, `upper`, `title`, `trim`, `trimAll`, `trimPrefix`, `trimSuffix`, `replace`, `trunc` и `default` (но не `if`/`range`). Фильтрация выполняется до переписывания строки, поэтому фильтр строк, парсер или второй `line_format` *после* `line_format` отклоняются, как и `bytes_over_time` / `bytes_rate` над переписанной строкой. Фильтры по лейблам после него допустимы.
- **`| logfmt --strict` останавливается на первой ошибке.** Строка корректна, если состоит только из элементов `ключ`, `ключ=`, `ключ=значение` или `ключ="значение в кавычках"` (простое значение не содержит `=` и `"`, значение в кавычках — только экранирование, принимаемое Go). Некорректная строка, а также любая строка, являющаяся JSON-объектом, сохраняет лейблы до ошибки и получает `__error__="LogfmtParserErr"`; текст `__error_details__` не предоставляется. `--keep-empty` принимается и ничего не меняет, потому что пустой лейбл равнозначен отсутствующему везде, где Flare их вычисляет. Все этапы logfmt в запросе должны быть согласованы по `--strict`.
- **`rate_counter` и `absent_over_time`.** `rate_counter` рассматривает значение из `unwrap` как счётчик: прирост от первого до последнего отсчёта с учётом сбросов, растянутый к краям окна так же, как это делает `rate` в Prometheus, в секунду; окно менее чем с двумя отсчётами даёт 0. `absent_over_time` возвращает ряд из единиц на шагах без подходящих строк, с лейблами из селекторов равенства, и не возвращает ряда, если пустых шагов нет.
- **Ключи `| logfmt` становятся лейблами**, причём каждый символ вне `[a-zA-Z0-9_]` заменяется на `_`; ключ без значения или с пустым значением лейблом не становится; у строки, являющейся JSON-объектом, нет лейблов logfmt. Лейбл, совпадающий с лейблом потока или метаданных, переименовывается в `<имя>_extracted`, как в Loki.
- **Ошибки `unwrap` подчиняются правилу Loki.** Строка без лейбла не является отсчётом. Строка, значение которой не число (или не длительность либо размер для `duration()` и `bytes()`), — это отсчёт 0 с `__error__="SampleExtractionErr"`. Фильтр после `unwrap`, отклоняющий её (`| __error__=""`, его пишет Logs Drilldown), убирает её; фильтр, принимающий её (`| __error__!=""`), сохраняет её отдельным рядом; а запрос, не делающий ни того ни другого, отклоняется сообщением Loki `pipeline error: 'SampleExtractionErr' ...` — добавьте `| __error__=""` после `unwrap`. В отличие от Loki, у ряда нет `__error_details__`. `quantile_over_time` вычисляется точно, для каждого шага, поэтому его диапазон не может быть шире 120 шагов (для `[5m]` нужен шаг не меньше 2,5 секунды), а любая функция с `unwrap` на очень длинном диапазоне при крошечном шаге отклоняется с просьбой увеличить шаг.
- **Ограничения.** Запрос логов возвращает не более 5000 строк (`limit` по умолчанию 100); метрический запрос завершается ошибкой, а не усечением, при более чем 500 рядах или 11 000 точках на ряд.
- **Некорректные регулярные выражения** обнаруживаются при выполнении запроса к данным, в синтаксисе RE2.
- **Мгновенные запросы** работают только для метрик; для селектора логов нужен `query_range`.

Заметки о дизайне: [ADR-0236](../../../docs-internal/adr/0236-loki-compatible-log-api.md), [ADR-0237](../../../docs-internal/adr/0237-loki-api-logfmt-line-format-unwrap-tail-and-drilldown.md) и [ADR-0238](../../../docs-internal/adr/0238-loki-api-rate-counter-absent-strict-logfmt-unwrap-errors-dropped-entries.md).
