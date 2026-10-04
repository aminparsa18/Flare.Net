# Как запрашивать Flare из Grafana или через API Prometheus

Flare предоставляет **доступный только для чтения поднабор HTTP API Prometheus** поверх ваших метрик OTel, поэтому Grafana, `promtool` и `prometheus-adapter` (HPA в Kubernetes по пользовательским метрикам) могут использовать его как источник данных Prometheus. Поддерживаются селекторы, `rate`/`increase`, `sum|avg|min|max|count` и `histogram_quantile`. Всё остальное отклоняется ошибкой с названием неподдерживаемой конструкции и никогда не вычисляется частично.

## Подключение Grafana

1. Создайте [персональный токен доступа](configure-authentication.ru.md#персональные-токены-доступа).
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
| `rate`, `increase` | `rate(requests_total[5m])` |
| `sum`, `avg`, `min`, `max`, `count` с `by`/`without` | `sum by (route) (rate(requests_total[5m]))` |
| `histogram_quantile` над `rate`/`increase`, при необходимости внутри `sum by (...)` | `histogram_quantile(0.99, sum by (le, route) (rate(d_bucket[5m])))` |
| `_sum` и `_count` гистограммы под `rate`/`increase` | `rate(d_seconds_count[1m])` |
| Арифметика над числами | `1+1` |

Эндпоинты: `query`, `query_range`, `series`, `labels`, `label/<name>/values`, `status/buildinfo`.

Не поддерживается: операторы между рядами (`a / b`), `offset`, `@`, подзапросы, другие функции, `topk`, `quantile`, правила записи и запись данных. Для доли ошибок задайте [SLO](define-slos.ru.md).

## Отличия от Prometheus

- **Для счётчиков нужен `rate()` или `increase()`.** Flare хранит приращения за интервал, поэтому «голый» счётчик вроде `requests_total` отклоняется с подсказкой про `rate()`. Gauge под `rate()` читается как счётчик - так приходят метрики Prometheus `*_total` без типа.
- **Метки времени - это начала бакетов.** Точки лежат на кратных шагу моментах, а не на `start + k*step`. Окно `[5m]` скользит по `round(5m / шаг)` бакетам, минимум по одному.
- **200 рядов на селектор.** Если селектору соответствует больше рядов, возвращаются 200 крупнейших и добавляется запись в `warnings`. Сузьте выборку матчерами меток. Матчеры `!=` и регулярные выражения применяются после этого ограничения.
- **Мгновенные запросы** смотрят на последние пять минут.
- **`labels` и значения меток без `match[]`** берут выборку из десяти метрик с наибольшим числом рядов. Передайте `match[]`, чтобы получить полный ответ.

Заметки о дизайне: [ADR-0109](../../docs-internal/adr/0109-prometheus-query-api.md).
