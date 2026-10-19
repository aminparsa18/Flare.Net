# Как запрашивать Flare из Grafana или через API Prometheus

Flare предоставляет **доступный только для чтения поднабор HTTP API Prometheus** поверх ваших метрик OTel, поэтому Grafana, `promtool` и `prometheus-adapter` (HPA в Kubernetes по пользовательским метрикам) могут использовать его как источник данных Prometheus. Поддерживаются селекторы с `offset`, `rate`/`increase`, агрегации, включая `topk` и `quantile`, `histogram_quantile`, операторы между рядами (`a / b`) и набор математических функций. Всё остальное отклоняется ошибкой с названием неподдерживаемой конструкции и никогда не вычисляется частично.

## Подключение Grafana

1. Создайте [персональный токен доступа](../admin/configure-authentication.ru.md#персональные-токены-доступа).
2. В Grafana добавьте источник данных **Prometheus**.
3. В поле **URL** укажите базовый адрес API Flare (`http://localhost:8080` в автономном стеке Docker). Flare отдаёт API по пути `/api/v1`, где его ожидает Grafana.
4. В разделе **Authentication** добавьте пользовательский HTTP-заголовок `Authorization` со значением `Bearer flr_pat_...`.
5. Нажмите **Save & test**.

## Проверка через curl

```bash
export FLARE=http://localhost:8080 TOKEN=flr_pat_...

# Мгновенный запрос
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query=sum by (service_name) (rate(http_server_requests_total[5m]))' \
  $FLARE/api/v1/query

# Запрос по диапазону
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query=histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket[5m])))' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" --data-urlencode step=60 \
  $FLARE/api/v1/query_range
```

## Имена метрик и меток

Flare переводит имена OTel в соглашения Prometheus:

| OTel | Prometheus |
|---|---|
| `http.server.request.duration` (гистограмма, единица `s`) | `http_server_request_duration_seconds_bucket`, `_sum`, `_count` |
| `http.server.requests` (sum) | `http_server_requests_total` |
| `process.memory` (gauge, единица `By`) | `process_memory_bytes` |
| атрибут `http.route` | метка `http_route` |
| ресурс `service.name` | метка `service_name` |

Точные имена можно получить через `GET /api/v1/label/__name__/values`.

## Что поддерживается

| Конструкция | Пример |
|---|---|
| Селекторы с `=`, `!=`, `=~`, `!~` | `up{service_name="api", code!="200"}` |
| Шаблоны имён метрик по `__name__` | `{__name__=~"http_server_.*"}`, `rate({__name__=~"jobs_.*_total"}[5m])` |
| `offset` для селектора или диапазона | `requests_total offset 1w`, `rate(requests_total[5m] offset 1d)` |
| `rate`, `increase`, `irate` | `rate(requests_total[5m])` |
| `avg_over_time`, `min_over_time`, `max_over_time`, `sum_over_time`, `count_over_time`, `last_over_time` (gauge) | `max_over_time(queue_depth[10m])` |
| `delta`, `idelta`, `changes`, `resets`, `deriv`, `predict_linear`, `holt_winters` (gauge) | `predict_linear(free_bytes[6h], 4 * 3600)`, `resets(uptime_seconds[1d])` |
| `time()`, `timestamp(v)`, `scalar(v)` и арифметика над ними; `vector(time())` | `time() - timestamp(heartbeat)` |
| `@` для селектора, диапазона или подзапроса | `requests_total @ 1700000000`, `queue_depth / queue_depth @ start()` |
| Подзапросы внутри функции `*_over_time`, `delta`, `idelta`, `changes`, `resets`, `deriv`, `predict_linear` или `holt_winters` | `max_over_time(rate(requests_total[5m])[1h:1m])` |
| `sum`, `avg`, `min`, `max`, `count`, `group`, `stddev`, `stdvar` с `by`/`without` | `sum by (route) (rate(requests_total[5m]))` |
| `topk`, `bottomk`, `quantile` с `by`/`without` | `topk(5, sum by (host) (rate(cpu_seconds_total[5m])))` |
| `count_values` с `by`/`without` | `count_values("version", build_info)` |
| `sort`, `sort_desc`, `sort_by_label`, `sort_by_label_desc` | `sort_desc(sum by (host) (rate(cpu_seconds_total[5m])))` |
| `histogram_quantile` и `histogram_fraction` над `rate`/`increase`, при необходимости внутри `sum by (...)` | `histogram_quantile(0.99, sum by (le, route) (rate(d_bucket[5m])))`, `histogram_fraction(0, 0.3, sum by (le) (rate(d_bucket[5m])))` |
| `_sum` и `_count` гистограммы под `rate`/`increase` | `rate(d_seconds_count[1m])` |
| Арифметика `+ - * / % ^` между рядами и числами | `100 * sum(rate(errors_total[5m])) / sum(rate(requests_total[5m]))` |
| Сравнения `== != > < >= <=` с фильтрацией или `bool` для 1/0 | `queue_depth > 100`, `up > bool 0` |
| `and`, `or`, `unless` | `rate(a_total[5m]) > 1 and on(job) rate(b_total[5m]) > 1` |
| `on(...)`, `ignoring(...)`, `group_left`, `group_right` | `errors / on(job) group_left(team) owners` |
| `abs`, `ceil`, `floor`, `round`, `sqrt`, `exp`, `ln`, `log2`, `log10`, `sgn`, `clamp`, `clamp_min`, `clamp_max`, `vector` | `clamp_min(free_ratio, 0)` |
| `absent` | `absent(up{job="api"})` |
| `label_replace`, `label_join` | `label_replace(x, "host", "$1", "instance", "(.*):\\d+")` |

Эндпоинты: `query`, `query_range`, `query_exemplars`, `series`, `labels`, `label/<name>/values`, `status/buildinfo`.

Не поддерживается: `rate`/`increase`/`irate` над подзапросом, функции нативных гистограмм (`histogram_count`, `histogram_sum`, `histogram_avg`; используйте ряды `_count` и `_sum` гистограммы), правила записи и запись данных.

## Release health

Flare отдаёт release health мобильных приложений (см. [Мониторинг MAUI-приложения](../../tutorials/monitor-a-maui-app.md)) как четыре
встроенные метрики, поэтому панель или правило алерта в Grafana может использовать долю сессий без падений через тот же
источник данных. У них есть метки `service_name` и `service_version`, и они видны в `label/__name__/values`.

| Метрика | Тип | Значение |
|---|---|---|
| `flare_release_sessions_total` | counter | Начатые сессии |
| `flare_release_crashed_sessions_total` | counter | Сессии, закончившиеся фатальным необработанным исключением |
| `flare_release_users` | gauge | Уникальные пользователи с сессией, начатой в этом шаге |
| `flare_release_crashed_users` | gauge | Уникальные пользователи с упавшей сессией, начатой в этом шаге |
| `flare_release_users_24h` | gauge | Уникальные пользователи с сессией, начатой за 24 часа до этого шага |
| `flare_release_crashed_users_24h` | gauge | Уникальные пользователи с упавшей сессией, начатой за 24 часа до этого шага |

Доля сессий без падений по версиям за последние 24 часа, для панели или правила алерта ниже `0.99`:

```promql
1 - sum by (service_version) (increase(flare_release_crashed_sessions_total[24h]))
  / sum by (service_version) (increase(flare_release_sessions_total[24h]))
```

Добавьте `{service_name="shop-app"}`, чтобы ограничить одним приложением. Чтобы версия с тремя запусками никого не будила,
добавьте `and sum by (service_version) (increase(flare_release_sessions_total[24h])) > 50`. Доля пользователей без
падений за последние сутки: `1 - flare_release_crashed_users_24h / flare_release_users_24h`. Каждый шаг показывает
24 часа до себя, поэтому и мгновенный запрос, и панель с любым шагом читают полные сутки. Обычные gauge
`flare_release_users` считают на шаг (уникальные пользователи не складываются между шагами), что подходит диапазонному
запросу с шагом нужного периода. Gauge `_24h` сканируют сутки спанов до запрошенного диапазона, поэтому дороже всего
длинный диапазон с мелким шагом. Пользователи появляются, только если приложение задаёт `user.id`. Префикс имени
`flare.release.` зарезервирован за этими метриками.

## Exemplars

Точка гистограммы или счётчика, записанная внутри трейса, несёт **exemplar**: пример значения с трейсом и спаном, которые его породили. Flare сохраняет те, у которых есть идентификатор трейса, и отдаёт их через `query_exemplars`, чтобы Grafana рисовала их на графике задержки и открывала медленный запрос.

1. В источнике данных Prometheus включите **Exemplars** и добавьте внутреннюю ссылку: **Label name** `trace_id`, **Data source** — источник Tempo, указывающий на [Tempo-совместимый API](query-with-tempo-api.ru.md) Flare.
2. Запросите гистограмму, например `histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket[5m])))`. Точки появятся на графике.

```bash
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query=http_server_request_duration_seconds_bucket{service_name="api"}' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" \
  $FLARE/api/v1/query_exemplars
```

Каждый результат — серия со своими `exemplars`, у каждого есть `trace_id` (и `span_id`), значение и метка времени. Возвращается до 1000 штук, сначала новые. Exemplars несут только точки, принятые после обновления. Они приходят по OTLP или от цели скрейпинга Prometheus с включённым `Exemplars` (Flare запрашивает у цели OpenMetrics и сохраняет exemplars с `trace_id` из 32 шестнадцатеричных цифр).

Собственная страница Metrics в Flare и панели Metrics на дашбордах тоже рисуют их: каждый exemplar — кольцо, по щелчку открывающее его трассировку на спане, который его записал. На графике гистограммы кольцо стоит на наблюдаемом значении (exemplar выше диапазона графика ложится на верхнюю границу; наведите курсор, чтобы увидеть настоящее значение). На графике счётчика, gauge или формулы оно ложится на линию серии, на которой записано, в корзину, которая его содержит, потому что у приращения нет высоты на оси скорости. Дашборд показывает до 100 самых новых за период; на графиках с накоплением и в наложении сравнения их нет.

## Отличия от Prometheus

- **Ошибки сопоставления настоящие.** `a / b` завершается ошибкой «found duplicate series for the match group», если набор меток совпадает с двумя рядами с одной стороны. Сначала агрегируйте, сузьте через `on(...)`/`ignoring(...)` или явно разрешите `group_left`/`group_right`, как и в Prometheus.
- **`offset` округляется до целого числа шагов.** `offset 90s` при шаге 60 с читается как 120 с, чтобы сдвинутый ряд остался на тех же отметках времени, что и остальной запрос.
- **`irate` и `avg_over_time` работают по бакетам.** `irate` — прирост последнего бакета в секунду, а `avg_over_time` усредняет средние значения бакетов. `min_over_time`, `max_over_time`, `sum_over_time` и `count_over_time` сворачивают исходные точки каждого бакета, поэтому точны с точностью до шага.
- **`delta`, `changes` и `resets` сравнивают каждую точку с предыдущей.** Сравнение относится к окну, в котором находится более поздняя точка, поэтому окно включает шаг от значения непосредственно перед ним. `delta(x[5m])` — значение в конце окна минус значение прямо перед его началом; Prometheus вместо этого экстраполирует «последнее минус первое» и считает только пары, обе точки которых внутри окна. Функции применяются к gauge (счётчик, собранный без метаданных о типе, — это gauge).
- **`deriv` и `predict_linear` аппроксимируют средние значения бакетов.** Прямая проходит через среднее каждого бакета, помещённое в его середину, поэтому берите окно из нескольких шагов. `predict_linear` читает прямую в конце последнего бакета.
- **`idelta` — это самая новая точка минус предыдущая**, даже если предыдущая старше окна. **`holt_winters`** (также `double_exponential_smoothing`) сглаживает средние значения бакетов, поэтому ему нужны два бакета.
- **`timestamp(x)` — время самой новой точки** за каждым значением, поэтому `time() - timestamp(x)` — настоящий возраст метрики.
- **Шаблон `__name__`** вычисляется для каждой подходящей метрики, но не более чем для 50 (по алфавиту), а ряды объединяются. Метрика, которую выражение не может прочитать (например, счётчик вне `rate()`), пропускается с предупреждением. `histogram_quantile` и `histogram_fraction` требуют одного имени.
- **`time()` — отметка времени шага**, а в мгновенном запросе — точное время запроса. **`sort` и `sort_desc`** заметны только в мгновенном запросе, как и в Prometheus.
- **`@` фиксирует время вычисления.** `x @ 1700000000` вычисляется один раз, как мгновенный запрос в этот момент (последнее значение в пятиминутном окне), и повторяется на каждом шаге. В мгновенном запросе `start()` и `end()` оба означают время запроса.
- **Подзапросы** `expr[1h:1m]` вычисляют внутреннее выражение каждую `1m` (шаг запроса, если разрешение не указано) и сворачивают значения из `(t - 1h, t]`. Вокруг них нужна функция `*_over_time`.
- **Для счётчиков нужен `rate()` или `increase()`.** Flare хранит приращения за интервал, поэтому «голый» счётчик вроде `requests_total` отклоняется с подсказкой про `rate()`. Gauge под `rate()` читается как счётчик - так приходят метрики Prometheus `*_total` без типа.
- **Метки времени - это начала бакетов.** Точки лежат на кратных шагу моментах, а не на `start + k*step`. Окно `[5m]` скользит по `round(5m / шаг)` бакетам, минимум по одному.
- **200 рядов на селектор.** Если селектору соответствует больше рядов, возвращаются 200 крупнейших и добавляется запись в `warnings`. Сузьте выборку матчерами меток. Матчеры `!=` и регулярные выражения применяются после этого ограничения.
- **Мгновенные запросы** смотрят на последние пять минут.
- **`labels` и значения меток без `match[]`** берут выборку из десяти метрик с наибольшим числом рядов. Передайте `match[]`, чтобы получить полный ответ.

Заметки о дизайне: [ADR-0109](../../../docs-internal/adr/0109-prometheus-query-api.md), [ADR-0239](../../../docs-internal/adr/0239-prometheus-api-binary-operators-offset-topk-functions.md), [ADR-0245](../../../docs-internal/adr/0245-prometheus-api-at-modifier-subqueries-min-max-over-time.md), [ADR-0246](../../../docs-internal/adr/0246-prometheus-api-time-scalar-delta-changes-resets-deriv-sort-count-values.md), [ADR-0247](../../../docs-internal/adr/0247-prometheus-api-idelta-holt-winters-timestamp-histogram-fraction-name-patterns.md), [ADR-0241](../../../docs-internal/adr/0241-metric-exemplars.md), [ADR-0250](../../../docs-internal/adr/0250-release-health-prometheus-metrics.md).
