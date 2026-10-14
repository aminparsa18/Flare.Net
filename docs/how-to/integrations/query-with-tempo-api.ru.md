# Как читать трейсы Flare из Grafana через API Tempo

Flare предоставляет **доступный только для чтения поднабор HTTP API Grafana Tempo** поверх ваших трейсов OTel, поэтому источник данных **Tempo** в Grafana может найти трейс по идентификатору, выполнить поиск трейсов и заполнить списки тегов и значений в конструкторе запросов. Поиск понимает небольшую часть TraceQL. Всё остальное отклоняется ошибкой с названием неподдерживаемой конструкции и никогда не вычисляется частично.

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

# То же самое в старом синтаксисе тегов
curl -H "Authorization: Bearer $TOKEN" -G $FLARE/tempo/api/search \
  --data-urlencode 'tags=service.name=payment http.status_code=500' --data-urlencode minDuration=500ms
```

## Что поддерживается

| Эндпоинт | Примечания |
|---|---|
| `GET /tempo/api/echo` | Проверка подключения в Grafana |
| `GET /tempo/api/traces/{id}`, `GET /tempo/api/v2/traces/{id}` | JSON или protobuf в зависимости от `Accept`. Короткие идентификаторы дополняются нулями слева. v2 сообщает `PARTIAL`, если Flare вернул трейс не целиком |
| `GET /tempo/api/search` | `q` (TraceQL) или `tags`, а также `minDuration`, `maxDuration`, `limit` (по умолчанию 20, не более 200), `start`, `end` (unix-секунды; по умолчанию последний час) |
| `GET /tempo/api/search/tags`, `GET /tempo/api/v2/search/tags` | Имена атрибутов, встреченные в окне; v2 группирует их по `span`, `resource` и `intrinsic`; `scope` сужает выборку |
| `GET /tempo/api/search/tag/{tag}/values`, `GET /tempo/api/v2/search/tag/{tag}/values` | Значения, встреченные в окне |

Поиск возвращает одну строку на трейс: сервис и имя корневого спана, его начало и длительность.

### Поднабор TraceQL

| Конструкция | Пример |
|---|---|
| Атрибуты спана с указанием области | `{ span.http.method = "GET" && resource.k8s.namespace.name = "prod" }` |
| Операторы `=`, `!=`, `=~`, `!~`, `>`, `>=`, `<`, `<=` | `{ span.http.status_code >= 500 }` |
| Встроенные поля спана: `name` (только `=`), `status` (только `=`: `error`, `ok`, `unset`), `duration` | `{ name = "POST /pay" && duration >= 100ms }` |
| Встроенные поля трейса в запросе из одного спансета: `rootName`, `rootServiceName`, `traceDuration` | `{ rootServiceName = "gateway" && traceDuration > 1s }` |
| Объединение спансетов: `&&`, `\|\|`, дочерний `>`, потомок `>>` | `{ resource.service.name = "checkout" } >> { status = error }` |
| Атрибуты без указания области | `{ .service.name = "api" }` |

Не поддерживаются: конвейеры `|` (`count()`, `avg()`, `select()`, `by()` и т. д.), `||` внутри одного спансета, `kind`, `statusMessage`, `name` с любым оператором, кроме `=`, `status !=`, сравнение двух полей и эндпоинты метрик TraceQL в Tempo.

## Чем это отличается от Tempo

- **Регулярные выражения привязаны к границам значения**, как в Tempo: `=~ "api-.*"` должно совпасть со всем значением.
- **Область атрибута без префикса определяется эвристикой.** В Tempo `.foo` означает «спан или ресурс». Flare читает его из ресурса, если имя входит в пространство имён ресурсов OpenTelemetry (`service.`, `k8s.`, `host.`, `cloud.`, `deployment.`, `process.`, `container.`, `os.`, ...), и из спана в остальных случаях. Чтобы не гадать, пишите `span.foo` или `resource.foo`.
- **`!=` совпадает и со спанами без этого атрибута.** В TraceQL атрибут обязан существовать.
- **Для поиска нужен корневой спан.** Трейс, корень которого не был принят, в списке не появится, хотя по идентификатору открывается.
- **Нет подробностей о совпавших спанах.** Результаты перечисляют трейсы, а не спаны внутри них, которые совпали.
- **Списки тегов строятся по выборке.** Имена берутся максимум из 50 000 спанов окна, самые частые первыми. Сузьте `start` и `end`, чтобы получить полный список.

Заметки о проектировании: [ADR-0217](../../../docs-internal/adr/0217-tempo-compatible-trace-api.md).
