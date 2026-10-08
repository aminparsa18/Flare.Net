# Как отправлять телеметрию браузера во Flare

Отправляйте загрузки страниц, спаны `fetch` и ошибки JavaScript из веб-приложения
во Flare с помощью OpenTelemetry JavaScript SDK. Спаны браузера используют общий
идентификатор трассы с вашим .NET-бэкендом, поэтому медленный клик можно
проследить до вызова API и его запросов к базе данных.

OTLP/HTTP-приёмник Flare напрямую принимает JSON-экспорт SDK. Единственный
дополнительный шаг — указать, каким источникам (origin) разрешено отправлять данные.

## Предварительные требования

- Работающий экземпляр Flare, порт OTLP/HTTP (`4318`) которого доступен из
  браузеров ваших пользователей.
- Веб-приложение, в которое можно добавлять npm-пакеты.

## Разрешите origin вашего сайта

Браузеры отклоняют кросс-доменные запросы, если приёмник не отвечает на
предварительный CORS-запрос. По умолчанию Flare не отправляет CORS-заголовков.
Укажите origin вашего сайта в Flare.Ingest:

```bash
Otlp__AllowedOrigins__0=https://app.example.com
Otlp__AllowedOrigins__1=http://localhost:5173
```

Перезапустите Flare.Ingest. Разрешён только `POST` с заголовками
`Content-Type`, `Content-Encoding` и `Authorization`. `*` разрешает любой origin.

## Установка и настройка SDK

```bash
npm install @opentelemetry/api @opentelemetry/sdk-trace-web \
  @opentelemetry/exporter-trace-otlp-http @opentelemetry/resources \
  @opentelemetry/instrumentation-document-load \
  @opentelemetry/instrumentation-fetch
```

```ts
import { WebTracerProvider, BatchSpanProcessor } from '@opentelemetry/sdk-trace-web';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http';
import { resourceFromAttributes } from '@opentelemetry/resources';
import { registerInstrumentations } from '@opentelemetry/instrumentation';
import { DocumentLoadInstrumentation } from '@opentelemetry/instrumentation-document-load';
import { FetchInstrumentation } from '@opentelemetry/instrumentation-fetch';

const provider = new WebTracerProvider({
  resource: resourceFromAttributes({ 'service.name': 'my-web-app' }),
  spanProcessors: [
    new BatchSpanProcessor(
      new OTLPTraceExporter({ url: 'https://flare.example.com:4318/v1/traces' })
    )
  ]
});
provider.register();

registerInstrumentations({
  instrumentations: [
    new DocumentLoadInstrumentation(),
    new FetchInstrumentation({
      // Send the traceparent header to your own API so the trace continues server side.
      propagateTraceHeaderCorsUrls: [/api\.example\.com/]
    })
  ]
});
```

Ваш API также должен разрешать заголовок `traceparent` в собственной
CORS-политике, иначе браузер заблокирует вызов.

## Ключи приёма

Если [ключи API приёма](configure-authentication.ru.md#ключи-api-приёма-данных)
обязательны, экспортёру нужен `headers: { Authorization: 'Bearer <ключ>' }`. Этот
ключ попадает в ваш JavaScript, и его видит каждый посетитель. Создайте отдельный
ключ для браузера и ограничьте его origin вашего сайта:

В дашборде откройте **Settings > Ingest keys** и нажмите кнопку с глобусом у ключа, чтобы изменить разрешённые источники и сервисы; `flare apikey scope` делает то же из терминала. Соответствующие вызовы API:

```bash
curl -X PUT http://localhost:8080/api/ingest-keys/$KEY_ID/origins \
  -H "Authorization: Bearer $FLARE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "origins": ["https://app.example.com"] }'
```

Ограниченный ключ принимается только от браузерного запроса, у которого `Origin`
есть в списке; curl и серверные экспортёры получают `403`. Origin из списка также
разрешён в CORS, поэтому с ограниченным ключом `Otlp__AllowedOrigins` не нужен.
Изменения применяются в течение 30 секунд. Пустой список снимает ограничение.

Ключ также можно привязать к сервисам, для которых ему разрешена запись. Экспорт с
любым другим `service.name` (или без него) отклоняется с `403`:

```bash
curl -X PUT http://localhost:8080/api/ingest-keys/$KEY_ID/services \
  -H "Authorization: Bearer $FLARE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "services": ["my-web-app"] }'
```

Это не делает ключ секретным: вне браузера любой может подделать заголовок
`Origin`. Задайте [лимиты на ключ](configure-authentication.ru.md#ключи-api-приёма-данных),
чтобы ограничить объём, который может отправить утёкший ключ.

## Отправка ошибок JavaScript

Страница **Errors** в Flare группирует исключения, записанные в спанах. Отправляйте каждую необработанную ошибку как короткий спан с событием исключения:

```ts
import { trace, SpanStatusCode } from '@opentelemetry/api';

const tracer = trace.getTracer('browser-errors');

function report(error: unknown) {
  const err = error instanceof Error ? error : new Error(String(error));
  const span = tracer.startSpan('js.error');
  span.recordException(err);
  span.setStatus({ code: SpanStatusCode.ERROR, message: err.message });
  span.end();
}

window.addEventListener('error', (e) => report(e.error ?? e.message));
window.addEventListener('unhandledrejection', (e) => report(e.reason));
```

Ошибки появятся на странице **Errors**, сгруппированные по типу и сообщению. Добавьте фильтр по атрибуту ресурса `telemetry.sdk.language = webjs`, чтобы видеть только ошибки браузера. Бросайте объекты `Error`: у брошенной строки нет типа, поэтому она не группируется. Стеки из минифицированных бандлов пока не символизируются.

## Отправка web vitals

Записывайте каждый Core Web Vital как гистограмму `browser.web_vital.<name>` (`lcp`, `inp`, `cls`, `fcp`, `ttfb`) с атрибутом `rating` (`good`, `needs-improvement`, `poor`):

```bash
npm install web-vitals @opentelemetry/sdk-metrics \
  @opentelemetry/exporter-metrics-otlp-http
```

```ts
import { metrics } from '@opentelemetry/api';
import { MeterProvider, PeriodicExportingMetricReader } from '@opentelemetry/sdk-metrics';
import { OTLPMetricExporter } from '@opentelemetry/exporter-metrics-otlp-http';
import { resourceFromAttributes } from '@opentelemetry/resources';
import { onCLS, onFCP, onINP, onLCP, onTTFB } from 'web-vitals';

const meterProvider = new MeterProvider({
  resource: resourceFromAttributes({ 'service.name': 'my-web-app' }),
  readers: [
    new PeriodicExportingMetricReader({
      exporter: new OTLPMetricExporter({ url: 'https://flare.example.com:4318/v1/metrics' }),
      exportIntervalMillis: 30_000
    })
  ]
});
metrics.setGlobalMeterProvider(meterProvider);
const meter = metrics.getMeter('web-vitals');

const MS = [100, 200, 500, 1000, 2000, 2500, 4000, 6000, 10000];
const SCORE = [0.01, 0.05, 0.1, 0.15, 0.25, 0.5, 1];

function track(name: string, unit: string, buckets: number[], subscribe: typeof onLCP) {
  const histogram = meter.createHistogram(`browser.web_vital.${name}`, {
    unit,
    advice: { explicitBucketBoundaries: buckets }
  });
  subscribe(({ value, rating }) => histogram.record(value, { rating }));
}

track('lcp', 'ms', MS, onLCP);
track('inp', 'ms', MS, onINP);
track('fcp', 'ms', MS, onFCP);
track('ttfb', 'ms', MS, onTTFB);
track('cls', '1', SCORE, onCLS);

// INP and CLS settle when the page is hidden; flush before the tab goes away.
document.addEventListener('visibilitychange', () => {
  if (document.visibilityState === 'hidden') void meterProvider.forceFlush();
});
```

На странице **Dashboards** выберите **Create from template > Web vitals**. Панели показывают перцентили каждой метрики; целевые значения Google измеряются по p75 (LCP менее 2,5 с, INP менее 200 мс, CLS менее 0,1).

## Разбивка vitals по маршрутам

Добавьте `url.template` (шаблон маршрута вроде `/orders/:id`, а не сам URL) в атрибуты `histogram.record`. Тогда раскрытие сервиса на странице **Фронтенд** покажет vitals по каждому маршруту. Столбец **Загрузки страниц** считает измерения FCP — по одному на загрузку.

## Символизация минифицированных стеков

Продакшен-бандлы минифицированы, поэтому стек ошибки выглядит как `at o (…/app-abc123.js:1:30)`. Загрузите source map сборки, и на странице **Errors** Flare покажет исходные функцию, файл, строку и столбец; у вхождения с переписанным стеком появляется метка **Source map applied**.

Сначала приложение должно сообщать релиз, из которого оно собрано, с тем же значением, с каким вы загружаете карты:

```ts
resourceFromAttributes({ 'service.name': 'my-web-app', 'service.version': '1.4.2' })
```

Затем загрузите карты из результата сборки шагом CI, указав в `FLARE_API_TOKEN` [персональный токен доступа](configure-authentication.md) администратора:

```bash
flare sourcemaps upload ./dist --url https://flare.example.com \
  --service my-web-app --release 1.4.2
```

Каждый файл `dist/**/*.map` сохраняется под своим путём без `.map`: `dist/assets/app-abc123.js.map` сохраняется как `assets/app-abc123.js`. Кадр сопоставляется с самым длинным сохранённым путём, которым заканчивается URL скрипта. `flare sourcemaps list` показывает сохранённое, а `flare sourcemaps delete --service my-web-app --release 1.4.2` удаляет релиз. Сам API: `PUT /api/source-maps?service=&version=&bundle=` с JSON карты в теле.

Символизация выполняется при открытии вхождения, поэтому карты, загруженные после ошибки, тоже применяются. Карта сопоставляется по `service.name` и `service.version` (или атрибуту ресурса `vcs.revision`), поэтому храните карты старых релизов, пока их ошибки ещё встречаются. Принимаются карты до 50 МБ; индексные карты (`sections`) отклоняются. Не раздавайте файлы `.map` публично, если не хотите открывать исходный код, и не включайте их в развёрнутый бандл.

## Страница «Фронтенд»

Откройте **Фронтенд** (в списке **Ещё** меню пользователя). Страница показывает 75-й перцентиль LCP, INP, CLS, FCP и TTFB по каждому сервису за выбранный период с цветом по порогам Google и долей измерений, которые браузер оценил как хорошие, а также частые ошибки JavaScript в браузере. Это исключения ресурсов с `telemetry.sdk.language = webjs`; кнопка **Открыть в ошибках** ведёт на страницу ошибок. В **Настройки > Source maps** загруженные карты перечислены по релизам, там же их можно удалить.

## Проверка

Откройте страницу **Traces** и отфильтруйте по сервису `my-web-app`. Загрузка
страницы отображается как трасса `documentLoad` с дочерними спанами
`documentFetch` и `resourceFetch`. Вызовы API, передающего `traceparent`,
отображаются как одна трасса через оба сервиса.

## Ограничения

- Браузер может потерять последний пакет при закрытии вкладки.
