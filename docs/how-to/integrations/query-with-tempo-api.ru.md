# Как читать трейсы Flare из Grafana через API Tempo

Flare предоставляет **доступный только для чтения поднабор HTTP API Grafana Tempo** поверх ваших трейсов OTel, поэтому источник данных **Tempo** в Grafana может найти трейс по идентификатору, выполнить поиск трейсов, заполнить списки тегов и значений в конструкторе запросов и строить метрики TraceQL (частоту запросов и перцентили задержки) в виде временных рядов. Поиск понимает небольшую часть TraceQL. Всё остальное отклоняется ошибкой с названием неподдерживаемой конструкции и никогда не вычисляется частично.

## Подключение Grafana

1. Создайте [персональный токен доступа](../admin/configure-authentication.ru.md#персональные-токены-доступа).
2. В Grafana добавьте источник данных **Tempo**.
3. В поле **URL** укажите базовый адрес API Flare с суффиксом `/tempo`: `http://localhost:8080/tempo` в автономном стеке Docker. Flare отдаёт API по пути `/tempo/api`, потому что `/api/traces` уже занят собственным маршрутом Flare.
4. В разделе **Authentication** добавьте пользовательский HTTP-заголовок `Authorization` со значением `Bearer flr_pat_...`.
5. Нажмите **Save & test**.

## Проверка через curl

```bash
export FLARE=http://localhost:8080 TOKEN=flr_pat_...

# Трейс по идентификатору (OTLP JSON; для protobuf, который использует Grafana, передайте "Accept: application/protobuf")
curl -H "Authorization: Bearer $TOKEN" $FLARE/tempo/api/traces/0af7651916cd43dd8448eb211c80319c

# Поиск TraceQL: трейсы, где у сервиса payment есть спан с ошибкой дольше 500 мс
curl -H "Authorization: Bearer $TOKEN" -G $FLARE/tempo/api/search \
  --data-urlencode 'q={ resource.service.name = "payment" && status = error && duration > 500ms }' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" --data-urlencode limit=20

# Метрики TraceQL: частота запросов каждого сервиса, один отсчёт в минуту
curl -H "Authorization: Bearer $TOKEN" -G $FLARE/tempo/api/metrics/query_range \
  --data-urlencode 'q={ kind = server } | rate() by (resource.service.name)' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" --data-urlencode step=60s

# То же самое в старом синтаксисе тегов
curl -H "Authorization: Bearer $TOKEN" -G $FLARE/tempo/api/search \
  --data-urlencode 'tags=service.name=payment http.status_code=500' --data-urlencode minDuration=500ms
```

## Связь спана с метриками

Параметр **Trace to metrics** источника данных Tempo добавляет к спану кнопку, которая открывает запрос Prometheus для сервиса и маршрута этого спана. Укажите в нём источник данных Prometheus, который [читает метрики Flare](query-with-prometheus-api.ru.md): тогда медленный спан в трассе — в одном клике от частоты запросов, доли ошибок и задержки сервиса, в котором он выполнялся. Имена лейблов метрик Flare отличаются от имён атрибутов OTel, поэтому сопоставление ниже важно.

В настройках источника данных Tempo, в разделе **Trace to metrics**:

1. **Data source**: источник данных Prometheus, указывающий на Flare.
2. **Span start time shift** `-5m`, **Span end time shift** `5m`. Приведённые ниже запросы смотрят назад на `[5m]`, поэтому диапазон, начинающийся со спана, оставил бы первые минуты пустыми.
3. **Tags**: сопоставьте каждый атрибут спана с лейблом Flare справа.

   | Атрибут спана | Лейбл |
   |---|---|
   | `service.name` | `service_name` |
   | `http.route` | `http_route` |
   | `http.request.method` | `http_request_method` |

   Всегда заполняйте лейбл. Лейблы Flare — это имена атрибутов с `_` вместо `.`; если поле пустое, Grafana берёт имя самого атрибута, а Flare отклоняет `service.name="checkout"` как селектор лейбла.
4. **Queries**: `$__tags` раскрывается в сопоставленные теги, которые есть у спана.

   | Название | Запрос |
   |---|---|
   | Request rate | `sum(rate(http_server_request_duration_seconds_count{$__tags}[5m]))` |
   | Error rate | `sum(rate(http_server_request_duration_seconds_count{$__tags, http_response_status_code=~"5.."}[5m]))` |
   | p95 latency | `histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket{$__tags}[5m])))` |

Те же настройки в виде провизионированного источника данных (`$$` не даёт Grafana раскрыть `$__tags` как переменную окружения):

```yaml
apiVersion: 1
datasources:
  - name: Flare Traces
    type: tempo
    url: http://localhost:8080/tempo
    jsonData:
      tracesToMetrics:
        datasourceUid: flare-prometheus   # uid вашего источника данных Prometheus для Flare
        spanStartTimeShift: "-5m"
        spanEndTimeShift: "5m"
        tags:
          - { key: service.name, value: service_name }
          - { key: http.route, value: http_route }
          - { key: http.request.method, value: http_request_method }
        queries:
          - name: Request rate
            query: 'sum(rate(http_server_request_duration_seconds_count{$$__tags}[5m]))'
```

Что ожидать:

- **Сервис должен отправлять метрики во Flare под тем же `service.name`.** Запросы читают гистограмму `http.server.request.duration`, которую записывают ASP.NET Core и другие HTTP-серверные инструментации OpenTelemetry. Для другой метрики найдите её имя в Prometheus через `GET /api/v1/label/__name__/values`.
- **Grafana пропускает тег, которого нет у спана.** У дочернего спана базы данных есть `service.name`, но нет `http.route`, поэтому его ссылка — запрос по всему сервису; ссылка серверного спана сужена до его маршрута.
- **Тег, которого нет у метрики, ничего не находит.** Если у `http.server.request.duration` приложения нет лейбла `http.route` (проверьте через `GET /api/v1/labels?match[]=http_server_request_duration_seconds_count`), уберите этот тег.
- **Trace to logs недоступен.** Ему нужен Loki-совместимый API логов, которого Flare пока не предоставляет.

## Что поддерживается

| Эндпоинт | Примечания |
|---|---|
| `GET /tempo/api/echo` | Проверка подключения в Grafana |
| `GET /tempo/api/traces/{id}`, `GET /tempo/api/v2/traces/{id}` | JSON или protobuf в зависимости от `Accept`. Короткие идентификаторы дополняются нулями слева. v2 сообщает `PARTIAL`, если Flare вернул трейс не целиком |
| `GET /tempo/api/search` | `q` (TraceQL) или `tags`, а также `minDuration`, `maxDuration`, `limit` (по умолчанию 20, не более 200), `spss` (сколько совпавших спанов показывать у трейса, по умолчанию 3, не более 100), `start`, `end` (unix-секунды; по умолчанию последний час) |
| `GET /tempo/api/search/tags`, `GET /tempo/api/v2/search/tags` | Имена атрибутов, встреченные в окне; v2 группирует их по `span`, `resource` и `intrinsic`; `scope` сужает выборку |
| `GET /tempo/api/search/tag/{tag}/values`, `GET /tempo/api/v2/search/tag/{tag}/values` | Значения, встреченные в окне |
| `GET /tempo/api/metrics/query_range` | `q` ([запрос метрик TraceQL](#метрики-traceql)), `start` и `end` (секунды или наносекунды Unix, либо RFC3339) или `since` (длительность; по умолчанию последний час), `step` (длительность или целые секунды; по умолчанию диапазон в 200 шагов, не более 1 500 шагов). Один ряд на группу |
| `GET /tempo/api/metrics/query` | То же без `step`: одно значение на группу за весь диапазон |

Поиск возвращает одну строку на трейс: сервис и имя корневого спана, его начало и длительность. Если запрос описывает спаны, каждая строка также содержит до `spss` совпавших спанов (`spanSets`) с атрибутами, по которым запрос сравнивал; Grafana показывает их вложенными строками таблицы результатов TraceQL.

### Поднабор TraceQL

| Конструкция | Пример |
|---|---|
| Атрибуты спана с указанием области | `{ span.http.method = "GET" && resource.k8s.namespace.name = "prod" }` |
| Операторы `=`, `!=`, `=~`, `!~`, `>`, `>=`, `<`, `<=` | `{ span.http.status_code >= 500 }` |
| Встроенные поля спана: `name` (`=`, `!=`, `=~`, `!~`), `status` (`=`, `!=`: `error`, `ok`, `unset`), `kind` (`=`, `!=`: `unspecified`, `internal`, `server`, `client`, `producer`, `consumer`), `duration` | `{ kind = server && name =~ "POST .*" && status != ok }` |
| Встроенные поля трейса в запросе из одного спансета: `rootName`, `rootServiceName`, `traceDuration` | `{ rootServiceName = "gateway" && traceDuration > 1s }` |
| Объединение спансетов: `&&`, `\|\|`, дочерний `>`, потомок `>>` | `{ resource.service.name = "checkout" } >> { status = error }` |
| Атрибуты без указания области | `{ .service.name = "api" }` |

Не поддерживаются в поиске: конвейеры `|` (`count()`, `avg()`, `select()`, `by()` и т. д.; запрос метрик принимает один, см. ниже), `||` внутри одного спансета, `statusMessage` и сравнение двух полей.

### Метрики TraceQL

`/tempo/api/metrics` принимает один спансет и одну функцию: `{ status = error } | rate() by (resource.service.name)`. Спансет — всё, что поиск принимает для одного спана, кроме объединённых спансетов и встроенных полей трейса (`rootName`, `rootServiceName`, `traceDuration`). В Grafana используйте тип запроса **TraceQL** с запросом, оканчивающимся функцией, — результат будет временным рядом; **Type** в параметрах Metrics запроса выбирает диапазонный или мгновенный (одно значение) запрос.

| Функция | Значение на шаг |
|---|---|
| `rate()` | Спанов в секунду |
| `count_over_time()` | Спанов |
| `quantile_over_time(duration, .5, .95)` | Квантили длительности в секундах, один ряд на квантиль, с меткой `p` |
| `avg_over_time(duration)`, `min_over_time(duration)`, `max_over_time(duration)`, `sum_over_time(duration)` | Длительность в секундах |

`by (...)` принимает до пяти полей из `name`, `status`, `kind`, `span.x`, `resource.x` и `.x` и создаёт ряд для каждой комбинации: `{ kind = server } | quantile_over_time(duration, .95) by (resource.service.name, span.http.route)`.

Не поддерживаются: `histogram_over_time()`, `compare()`, вторая стадия вроде `topk()`, агрегирование чего-либо кроме `duration` и экземпляры (exemplars): `with(exemplars=true)` принимается и игнорируется.

## Чем это отличается от Tempo

- **Регулярные выражения привязаны к границам значения**, как в Tempo: `=~ "api-.*"` должно совпасть со всем значением.
- **Область атрибута без префикса определяется эвристикой.** В Tempo `.foo` означает «спан или ресурс». Flare читает его из ресурса, если имя входит в пространство имён ресурсов OpenTelemetry (`service.`, `k8s.`, `host.`, `cloud.`, `deployment.`, `process.`, `container.`, `os.`, ...), и из спана в остальных случаях. Чтобы не гадать, пишите `span.foo` или `resource.foo`.
- **`!=` совпадает и со спанами без этого атрибута.** В TraceQL атрибут обязан существовать.
- **Для поиска нужен корневой спан.** Трейс, корень которого не был принят, в списке не появится, хотя по идентификатору открывается.
- **У совпавших спанов показаны атрибуты, по которым сравнивал запрос.** У каждого показанного спана есть идентификатор, имя, начало, длительность, `service.name` и атрибуты, названные в запросе (не больше 20), в виде строк. Атрибут со старым и новым написанием (`http.status_code`, `http.response.status_code`) показан в том написании, которое есть у спана. В запросе из нескольких спансетов (`{ a } >> { b }`) спаны всех спансетов показываются вместе одной группой, а не отдельной группой на каждый спансет.
- **Метрики группируются по шагу.** Отсчёт получает метку времени начала своего шага, а `start` и `end` округляются наружу до кратного шагу, поэтому, если диапазон заканчивается сейчас, последний шаг неполный. `rate()` и `count_over_time()` равны нулю на шаге без спанов; у остальных функций на таком шаге отсчёта нет.
- **Метрики группируют только спаны, у которых есть атрибут.** В `by (span.http.route)` спан без `http.route` исключается, а не считается под пустым значением. Атрибут со старым и новым написанием (`http.status_code`, `http.response.status_code`) группируется по тому, которое есть у спана.
- **Запрос метрик возвращает не более 200 рядов.** Если групп больше, остаются 200 с наибольшим числом спанов, а ответ помечается `PARTIAL`. Счётчики и квантили взвешивают каждый сохранённый спан на то, что он представляет при [семплировании трейсов](../../explanation/sampling-clustering-and-cost.ru.md), поэтому семплируемый сервис по-прежнему показывает истинную частоту.
- **Списки тегов строятся по выборке.** Имена берутся максимум из 50 000 спанов окна, самые частые первыми. Сузьте `start` и `end`, чтобы получить полный список.

Заметки о проектировании: [ADR-0217](../../../docs-internal/adr/0217-tempo-compatible-trace-api.md).
