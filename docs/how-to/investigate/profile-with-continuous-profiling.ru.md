# Как изучать непрерывные профили

Профили показывают, в каких функциях ваш код тратит процессорное время или память. Flare принимает сигнал OpenTelemetry **profiles** на тех же OTLP-портах, что и логи, трассы и метрики, сохраняет каждую выборку вместе со стеком вызовов и рисует объединённый результат в виде flame graph. Выборки, снятые во время трассируемого спана, несут его идентификаторы, поэтому от медленного спана можно перейти к коду, который в тот момент выполнялся.

OTLP profiles в OpenTelemetry пока в статусе **Alpha**. Формат передачи ещё может меняться между релизами, поэтому после обновления отправителя перепроверьте его.

## Отправка профилей во Flare

Направьте отправитель OTLP-профилей на те же адреса, которые вы уже используете:

| Транспорт | Адрес |
| --- | --- |
| gRPC | `localhost:4317` (`ProfilesService/Export`) |
| HTTP | `POST http://localhost:4318/v1development/profiles` (protobuf или JSON) |

Профили проходят ту же проверку ключа приёма, то же ограничение размера запроса и те же лимиты на ключ, что и остальные сигналы. Страницы **Ingestion** и **Pipeline** показывают Profiles рядом с Logs, Traces и Metrics.

OTLP profiles — новый сигнал, поэтому отправителей пока мало. OpenTelemetry Collector может пересылать получаемые профили: запустите его с `--feature-gates=service.profilesSupport` и задайте `profiles_endpoint: http://localhost:4318/v1development/profiles` у экспортёра `otlp_http`. Если вы включили [API-ключи приёма](../admin/configure-authentication.ru.md#ключи-api-приёма-данных), добавьте ключ в `headers` экспортёра. Сжатые (gzip) тела запросов принимаются.

Приёмник `pprof` в Collector (v0.162, Alpha) читает pprof-файлы и Go-эндпоинты `/debug/pprof`, но пока отдаёт выборки без стеков вызовов, поэтому flame graph по ним состоит только из корня. Flare сообщает об этом.

### Профилирование всего хоста через eBPF-профайлер

Практичный отправитель сегодня — **eBPF-профайлер** OpenTelemetry. Он профилирует каждый процесс на Linux-хосте без изменений в коде и отправляет стеки вызовов, включая кадры Python, Java, .NET, Go и нативные. Flare проверен с образом `otel/opentelemetry-collector-ebpf-profiler` версии **0.163.2**. Так как profiles пока в Alpha, берите версию, которую вы проверили, и перепроверяйте после каждого обновления.

Контейнеру нужны `--privileged` и `--pid=host`, а также ядро с BTF (любой свежий дистрибутив). Профайлер не знает имён ваших сервисов, поэтому конфигурация ниже копирует имя исполняемого файла в `service.name`; без неё профили попадут в **(без сервиса)**.

Сохраните как `ebpf-profiler.yaml`:

```yaml
receivers:
  profiling:
    samples_per_second: 20
processors:
  transform/service:
    profile_statements:
      - context: resource
        statements:
          - set(attributes["service.name"], attributes["process.executable.name"]) where attributes["service.name"] == nil and attributes["process.executable.name"] != nil
exporters:
  otlp_grpc:
    endpoint: localhost:4317
    tls:
      insecure: true
    # headers:
    #   Authorization: Bearer <ingest key>
service:
  pipelines:
    profiles:
      receivers: [profiling]
      processors: [transform/service]
      exporters: [otlp_grpc]
```

Запустите рядом с Flare (укажите в `endpoint` хост Flare.Ingest; если используете [ключи API приёма](../admin/configure-authentication.md#ingest-api-keys), раскомментируйте `headers`):

```bash
docker run -d --name flare-ebpf --privileged --pid=host --network host \
  -v /sys/kernel/debug:/sys/kernel/debug -v /sys/kernel/tracing:/sys/kernel/tracing \
  -v $PWD/ebpf-profiler.yaml:/etc/otel/config.yaml:ro \
  otel/opentelemetry-collector-ebpf-profiler:0.163.2 \
  --config=/etc/otel/config.yaml --feature-gates=+service.profilesSupport
```

В **Docker Desktop** виртуальная машина Linux не монтирует debugfs и tracefs, и профайлер падает с `neither debugfs nor tracefs are mounted`. Смонтируйте их один раз после каждого запуска ВМ:

```bash
docker run --rm --privileged --pid=host alpine nsenter -t 1 -m -- sh -c \
  'mount -t debugfs none /sys/kernel/debug; mount -t tracefs none /sys/kernel/tracing'
```

Откройте **Profiles**, выберите сервис, например `python3.12`, и тип `samples`. Кадры интерпретаторов (Python, Java, .NET и другие) показываются по именам; нативные кадры остаются в виде `module+0xaddress`.

### Попробуйте с curl

Запрос отправляет один профиль с двумя стеками. Каждая выборка ссылается на стек по индексу, а стек перечисляет кадры начиная с листа. Индекс 0 каждой таблицы — пустая запись.

```bash
curl -s -X POST http://localhost:4318/v1development/profiles \
  -H 'Content-Type: application/json' \
  -d '{
  "resourceProfiles": [{
    "resource": {"attributes": [{"key": "service.name", "value": {"stringValue": "checkout"}}]},
    "scopeProfiles": [{
      "profiles": [{
        "sampleType": {"typeStrindex": 1, "unitStrindex": 2},
        "timeUnixNano": "'"$(date +%s)"'000000000",
        "durationNano": "10000000000",
        "samples": [
          {"stackIndex": 1, "values": ["70000000"]},
          {"stackIndex": 2, "values": ["30000000"]}
        ]
      }]
    }]
  }],
  "dictionary": {
    "stringTable": ["", "cpu", "nanoseconds", "main", "handle", "db.Exec"],
    "functionTable": [{}, {"nameStrindex": 3}, {"nameStrindex": 4}, {"nameStrindex": 5}],
    "locationTable": [{}, {"lines": [{"functionIndex": 1}]}, {"lines": [{"functionIndex": 2}]}, {"lines": [{"functionIndex": 3}]}],
    "stackTable": [{}, {"locationIndices": [3, 2, 1]}, {"locationIndices": [2, 1]}]
  }
}'
```

Откройте **Profiles**, выберите сервис `checkout` и тип выборки `cpu` — вы увидите `main` > `handle` > `db.Exec`.

## Открытие страницы Profiles

1. Откройте **Profiles** в меню **More**.
2. Выберите **сервис**, **тип выборки** (например, `cpu` или `alloc_space`) и временное окно. Ряды перечислены по сервису и типу выборки, потому что значения разных типов нельзя складывать.
3. Flame graph объединяет все выборки окна. Ширина кадра — его доля от общего значения. Наведите курсор на кадр, чтобы увидеть его итог, процент и значение **self** — часть, потраченную в самом кадре, а не в вызываемых им функциях.
4. Нажмите на кадр, чтобы приблизить его. **Reset zoom** возвращает весь граф.

Flare объединяет до 5 000 различных стеков. Если их больше, страница показывает **Truncated** и пропускает самые лёгкие стеки.

## Профиль одного спана

1. Откройте трассу и нажмите на спан.
2. Нажмите **View profile** на панели спана.

Страница Profiles откроется только с выборками, снятыми во время этого спана. Для этого отправитель должен записывать активный спан в каждую выборку (связь профиля с `trace_id` и `span_id`). Без этого граф пуст; нажмите **Clear**, чтобы вернуться к графу сервиса.

## Запросы к API

```bash
# Какие ряды есть за последний час?
curl -s -X POST http://localhost:8080/api/profiles/types \
  -H 'Content-Type: application/json' -d '{"windowMinutes":60}'

# Объединённое дерево вызовов одного ряда, при желании только для спана
curl -s -X POST http://localhost:8080/api/profiles/flamegraph \
  -H 'Content-Type: application/json' \
  -d '{"service":"checkout","sampleType":"cpu","windowMinutes":60,"traceId":"<hex>","spanId":"<hex>"}'
```

Ответ flame graph — дерево `{ name, total, self, children }` под синтетическим корнем `all`. `sampleUnit` показывает, в чём значения: наносекунды, байты или просто счётчик.

## Ограничения

- У данных профилей пока нет собственной политики хранения, как и у спанов. Хранение для всех сигналов отслеживается одним пунктом дорожной карты.
- Неразобранные нативные кадры отображаются как `module+0xадрес`.
