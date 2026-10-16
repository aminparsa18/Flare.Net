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
| Встроенные поля Tempo с областью, как их пишет документация Tempo: `span:duration`, `span:name`, `span:status`, `span:kind`, `trace:duration`, `trace:rootName`, `trace:rootService`, `event:name` | `{ trace:rootService = "gateway" && span:duration > 100ms }` |
| Встроенные поля Tempo с областью для идентификаторов, области инструментирования, ссылок и событий: `span:id`, `span:parentID`, `trace:id`, `instrumentation:name`, `instrumentation:version`, `link:spanID`, `link:traceID` (`=`, `!=`, `=~`, `!~`, `= nil`; короткий шестнадцатеричный идентификатор дополняется нулями слева, а проверка по ссылкам истинна, если ей удовлетворяет какая-нибудь ссылка) и `event:timeSinceStart` (длительность, сравнивается через `=`, `!=`, `>`, `>=`, `<`, `<=`) | `{ trace:id = "5b8efff798038103d269b633813fc60c" && link:spanID != nil }` |
| Несколько значений одного поля, `(f = a \|\| f = b)`, которые вкладка Search в Grafana пишет, когда выбрано больше одного: сервис, имя спана, статус, вид спана (от двух до четырёх), атрибут, `rootName`, `rootServiceName`, идентификатор (и `f != a && f != b`) | `{ (resource.service.name = "api" \|\| resource.service.name = "web") && (name != "GET /health" && name != "GET /ready") }` |
| Объединение спансетов: `&&`, `\|\|`, дочерний `>`, потомок `>>`. Пустой `{ }` рядом с `>`, `>>` или `&>>` означает любой спан | `{ resource.service.name = "checkout" } >> { status = error }` |
| `&>>` — спаны обеих сторон: спан справа, у которого есть подходящий предок, и спан слева, у которого есть подходящий потомок | `{ nestedSetParent < 0 } &>> { kind = server }` |
| Атрибуты без указания области | `{ .service.name = "api" }` |
| Корневые спаны: `nestedSetParent < 0` (`> 0` — спаны с родителем) | `{ nestedSetParent < 0 && status = error }` |
| Атрибут задан, `!= nil`, или не задан, `= nil` | `{ resource.k8s.pod.name != nil }` |
| Одиночный `true`, который ничего не меняет | `{ nestedSetParent < 0 && true }` |
| Атрибуты событий, `event.x`: спан подходит, если подходит одно из его событий | `{ status = error && event.exception.type = "TimeoutException" }` |
| `\| select(...)` после запроса, чтобы добавить поля каждому показанному спану: `name`, `status`, `statusMessage`, `kind`, `duration`, `rootName`, `rootServiceName`, `traceDuration`, `nestedSetParent`, `nestedSetLeft`, `nestedSetRight`, `span.x`, `resource.x`, `.x`, `event.x` (не больше 20) и `with(most_recent=true)` | `{ status = error } \| select(resource.service.name, event.exception.message)` |

Не поддерживаются в поиске: конвейеры `|`, кроме `select()` (`count()`, `avg()`, `by()` и т. д.; запрос метрик принимает один, см. ниже), `||` внутри одного спансета между чем-либо, кроме проверок `=` одного поля, `statusMessage` как фильтр, `&>>` в запросе метрик и сравнение двух полей.

`nestedSetLeft`, `nestedSetRight` и `nestedSetParent` нумеруют спаны трейса так же, как Tempo: обход в глубину, где левое число спана — следующее число при входе в него, а правое — следующее при выходе, поэтому один спан находится под другим ровно тогда, когда его числа лежат внутри чисел другого. Родитель — это левое число родителя или -1 для корня. Спан, чей родитель не был принят, нумеруется как корень. Для нумерации читается не больше 2 000 спанов трейса.

### Метрики TraceQL

`/tempo/api/metrics` принимает один спансет и одну функцию: `{ status = error } | rate() by (resource.service.name)`. Спансет — всё, что поиск принимает для одного спана, либо два спансета, связанных `>` или `>>` (см. ниже), но не спансеты, объединённые через `&&` или `||`. В Grafana используйте тип запроса **TraceQL** с запросом, оканчивающимся функцией, — результат будет временным рядом; **Type** в параметрах Metrics запроса выбирает диапазонный или мгновенный (одно значение) запрос.

| Функция | Значение на шаг |
|---|---|
| `rate()` | Спанов в секунду |
| `count_over_time()` | Спанов |
| `quantile_over_time(duration, .5, .95)` | Квантили длительности в секундах, один ряд на квантиль, с меткой `p` |
| `avg_over_time(duration)`, `min_over_time(duration)`, `max_over_time(duration)`, `sum_over_time(duration)` | Длительность в секундах |
| `histogram_over_time(duration)` | Спанов на шаг в каждой корзине длительности по степеням двойки, один ряд на корзину, с меткой `__bucket`, в которой верхняя граница корзины в секундах. Grafana рисует это тепловой картой |

`by (...)` принимает до пяти полей из `name`, `status`, `kind`, `span.x`, `resource.x` и `.x` и создаёт ряд для каждой комбинации: `{ kind = server } | quantile_over_time(duration, .95) by (resource.service.name, span.http.route)`.

`quantile_over_time`, `avg_over_time`, `min_over_time`, `max_over_time`, `sum_over_time` и `histogram_over_time` принимают и числовой атрибут вместо `duration`: `{ kind = server } | avg_over_time(span.http.response.body.size)`, где `span.x`, `resource.x` или `.x`. Значения отдаются как есть, не в секундах. Спан, у которого значения нет или оно не число, исключается.

После функции этапы выполняются слева направо. `| topk(5)` или `| bottomk(5)` на каждом шаге оставляет пять рядов с наибольшим (наименьшим) значением: `{ kind = server } | rate() by (span.http.route) | topk(5)`. Сравнение (`=`, `!=`, `<`, `<=`, `>`, `>=` и обычное число) отбрасывает отсчёты, которые его не проходят, и ряд, у которого не осталось ни одного: `| rate() by (name) > 5`. Число задаётся в единицах функции, то есть в секундах для длительности. Порядок важен: `| rate() > 0 | topk(5)` ранжирует только ненулевые отсчёты, `| topk(5) > 10` отбрасывает победителей со значением ниже 10.

**Связи.** `{ a } >> { b } | rate()` считает спаны `b`, над которыми есть спан `a`, а `{ a } > { b }` — те, чей родитель — спан `a`. `{ resource.service.name = "web" } >> { name = "db.query" } | quantile_over_time(duration, .95)` — задержка запросов к базе, порождённых спаном `web`.

**Поля трейса.** `rootName`, `rootServiceName` и `traceDuration` ограничивают запрос спанами тех трейсов, чей корневой спан подходит: `{ rootName = "GET /checkout" && name = "db.query" } | rate()`. Со связью их сочетать нельзя.

**Exemplars** связывают точку на графике с трейсом. Включите **Exemplars** в параметрах Metrics (Grafana отправляет `exemplars=100`) или добавьте в запрос `with(exemplars=true)`. Тогда каждый шаг каждого ряда несёт самый медленный спан (или спан с наибольшим значением атрибута) с идентификатором трейса, и Grafana показывает его точкой, которую можно открыть. Только запросы по диапазону.

**`compare()`** делит спаны запроса на выборку и базовую группу (baseline) и считает каждое значение каждого атрибута в обеих: `{ nestedSetParent < 0 } | compare({ status = error }, 10)`. Второй аргумент — сколько значений оставлять на атрибут (по умолчанию 10, не больше 1 000), а два unix-времени в наносекундах после него ограничивают выборку спанами, начавшимися в этом окне. В ответе для каждого значения есть ряд `{ __meta_type="selection", span.http.route="/cart" }` (и `baseline`), по ряду `selection_total` и `baseline_total` на атрибут, считающему все спаны с этим атрибутом, и, для атрибута с большим числом значений, чем оставлено, ряд с меткой `__meta_error="__too_many_values__"`. Атрибуты — `name`, `status`, `kind` и все атрибуты `span.` и `resource.`. `by` и этапы после неё не принимаются, а запрос ограничен 200 шагами.

`with(sample=true)` принимается и игнорируется (ответы Flare точные); его можно сочетать с `exemplars`: `with(exemplars=true, sample=true)`.

Не поддерживаются: `&&` или `||` между спансетами в запросе метрик.

## Grafana Traces Drilldown

Приложение **Traces Drilldown** (Drilldown > Traces в Grafana 12 и новее) работает с источником данных Tempo, настроенным выше, в таких частях:

- **Частота спанов, частота ошибок и длительность**, тепловая карта длительностей и **разбивка** (Breakdown) каждого показателя по любому атрибуту.
- **Comparison** — ранжирует атрибуты по тому, насколько по-разному распределены выбранные спаны (например, ошибки) и остальные. Выполняет `compare()`.
- **Traces** — список подходящих трейсов.

- **Service structure**, **Root cause errors** и **Root cause latency** — дерево сервисов и операций, через которые проходят подходящие трейсы. Используют `&>>`, `select()` и вложенные номера спанов.
- **Exceptions** — исключения, записанные как события подходящих спанов, сгруппированные по типу и сообщению, с фильтром, который открывает одно из них. Используют `select()` и атрибуты `event.`.

Все вкладки приложения загружаются без ошибки запроса. Drilldown также отправляет один запрос с атрибутом `undefined` (ошибка приложения); Flare отклоняет его, как и Tempo, и приложение продолжает работать. Вкладка Exceptions показывает исключения спанов, о которых спрашивает приложение, — в виде корневых спанов это спаны корня. См. [ADR-0226](../../../docs-internal/adr/0226-traces-drilldown-compatibility-and-compare.md) и [ADR-0227](../../../docs-internal/adr/0227-traceql-select-union-descendant-and-nested-sets.md).

## Чем это отличается от Tempo

- **Регулярные выражения привязаны к границам значения**, как в Tempo: `=~ "api-.*"` должно совпасть со всем значением.
- **Область атрибута без префикса определяется эвристикой.** В Tempo `.foo` означает «спан или ресурс». Flare читает его из ресурса, если имя входит в пространство имён ресурсов OpenTelemetry (`service.`, `k8s.`, `host.`, `cloud.`, `deployment.`, `process.`, `container.`, `os.`, ...), и из спана в остальных случаях. Чтобы не гадать, пишите `span.foo` или `resource.foo`.
- **`!=` совпадает и со спанами без этого атрибута.** В TraceQL атрибут обязан существовать.
- **Для поиска нужен корневой спан.** Трейс, корень которого не был принят, в списке не появится, хотя по идентификатору открывается.
- **У совпавших спанов показаны атрибуты, по которым сравнивал запрос.** У каждого показанного спана есть идентификатор, имя, начало, длительность, `service.name` и атрибуты, названные в запросе (не больше 20), плюс поля `select()`. Значение атрибута спана, ресурса или события, которое однозначно является целым числом, десятичным числом или `true` / `false`, записывается как `intValue`, `doubleValue` или `boolValue`, как и метка `compare()`; всё остальное, например `0123`, `5.0`, `+5` или `1e5`, остаётся строкой, потому что Flare хранит текст и тип, заданный источником, потерян. Вложенные номера — целые числа. Атрибут называется без области видимости, как в Tempo. `rootName`, `rootServiceName` и `traceDuration` принимаются в `select()` и уже есть в каждом результате (`rootTraceName`, `rootServiceName`, `durationMs`), как у Tempo; `statusMessage` показывается, если он есть у спана. Атрибуты поля `event.` показываются для каждого события спана (не больше 50), одно за другим, атрибуты одного события вместе: `exception.type`, `exception.message` и `exception.stacktrace` одного исключения остаются рядом, а ключ встречается один раз на событие. Атрибут со старым и новым написанием (`http.status_code`, `http.response.status_code`) показан в том написании, которое есть у спана. В запросе из нескольких спансетов (`{ a } >> { b }`) спаны всех спансетов показываются вместе одной группой, а не отдельной группой на каждый спансет.
- **Метрики группируются по шагу.** Отсчёт получает метку времени начала своего шага, а `start` и `end` округляются наружу до кратного шагу, поэтому, если диапазон заканчивается сейчас, последний шаг неполный. `rate()` и `count_over_time()` равны нулю на шаге без спанов, как и `histogram_over_time()`; у остальных функций на таком шаге отсчёта нет.
- **Метрики группируют только спаны, у которых есть атрибут.** В `by (span.http.route)` спан без `http.route` исключается, а не считается под пустым значением. Атрибут со старым и новым написанием (`http.status_code`, `http.response.status_code`) группируется по тому, которое есть у спана.
- **`histogram_over_time()` оставляет не более 20 групп,** в каждой до 63 рядов-корзин, а значение меньше 2 (длительность менее 2 нс) не попадает ни в одну корзину, как в Tempo.
- **`topk()` ранжирует ряды, которые вычислил Flare.** Как и ограничение в 200 рядов ниже, он работает с группами, где больше всего спанов, поэтому группа с наибольшим средним могла быть отброшена (ответ содержит `PARTIAL`).
- **Связи и поля трейса заглядывают на час назад.** Предок спана и корень трейса ищутся начиная с часа до диапазона, поэтому спан, чей предок начался раньше, не учитывается. Оба варианта читают спаны трейсов-кандидатов, а это дороже обычного спансета.
- **`compare()` считает строки и смотрит на короткие значения.** Целочисленный атрибут — это строковая метка, а значение длиннее 256 символов (URL со строкой запроса, стек вызовов) не учитывается. Она читает все атрибуты всех спанов диапазона, поэтому это самый дорогой запрос метрик; сузьте диапазон фильтром в спансете.
- **Exemplars — для диапазонных запросов.** В мгновенном ответе Tempo для них тоже нет места.
- **Exemplar — самый медленный спан,** а не случайный, как в Tempo, и он лежит на ряду своей группы (для `quantile_over_time` — на ряду наибольшего квантиля, для гистограммы — в корзине, куда попадает его значение).
- **Запрос метрик возвращает не более 200 рядов.** Если групп больше, остаются 200 с наибольшим числом спанов, а ответ помечается `PARTIAL`. Счётчики и квантили взвешивают каждый сохранённый спан на то, что он представляет при [семплировании трейсов](../../explanation/sampling-clustering-and-cost.ru.md), поэтому семплируемый сервис по-прежнему показывает истинную частоту.
- **Списки тегов строятся по выборке.** Имена берутся максимум из 50 000 спанов окна, самые частые первыми. Сузьте `start` и `end`, чтобы получить полный список.

Заметки о проектировании: [ADR-0217](../../../docs-internal/adr/0217-tempo-compatible-trace-api.md), [ADR-0222](../../../docs-internal/adr/0222-traceql-metrics-over-an-attribute.md), [ADR-0223](../../../docs-internal/adr/0223-traceql-histogram-and-exemplars.md), [ADR-0224](../../../docs-internal/adr/0224-traceql-metrics-topk-bottomk.md), [ADR-0225](../../../docs-internal/adr/0225-traceql-metrics-comparison-and-span-relations.md) и [ADR-0226](../../../docs-internal/adr/0226-traces-drilldown-compatibility-and-compare.md), [ADR-0227](../../../docs-internal/adr/0227-traceql-select-union-descendant-and-nested-sets.md).
