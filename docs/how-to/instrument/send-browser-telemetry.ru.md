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

Если [ключи API приёма](../admin/configure-authentication.ru.md#ключи-api-приёма-данных)
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
`Origin`. Задайте [лимиты на ключ](../admin/configure-authentication.ru.md#ключи-api-приёма-данных),
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

Затем загрузите карты из результата сборки шагом CI, указав в `FLARE_API_TOKEN` [персональный токен доступа](../admin/configure-authentication.md) администратора:

```bash
flare sourcemaps upload ./dist --url https://flare.example.com \
  --service my-web-app --release 1.4.2
```

Каждый файл `dist/**/*.map` сохраняется под своим путём без `.map`: `dist/assets/app-abc123.js.map` сохраняется как `assets/app-abc123.js`. Кадр сопоставляется с самым длинным сохранённым путём, которым заканчивается URL скрипта. `flare sourcemaps list` показывает сохранённое, а `flare sourcemaps delete --service my-web-app --release 1.4.2` удаляет релиз. Сам API: `PUT /api/source-maps?service=&version=&bundle=` с JSON карты в теле.

Символизация выполняется при открытии вхождения, поэтому карты, загруженные после ошибки, тоже применяются. Карта сопоставляется по `service.name` и `service.version` (или атрибуту ресурса `vcs.revision`), поэтому храните карты старых релизов, пока их ошибки ещё встречаются. Принимаются карты до 50 МБ; индексные карты (`sections`) отклоняются. Не раздавайте файлы `.map` публично, если не хотите открывать исходный код, и не включайте их в развёрнутый бандл.

## Страница «Фронтенд»

Откройте **Фронтенд** (в списке **Ещё** меню пользователя). Страница показывает 75-й перцентиль LCP, INP, CLS, FCP и TTFB по каждому сервису за выбранный период с цветом по порогам Google и долей измерений, которые браузер оценил как хорошие, а также частые ошибки JavaScript в браузере. Это исключения ресурсов с `telemetry.sdk.language = webjs`; кнопка **Открыть в ошибках** ведёт на страницу ошибок. В **Настройки > Source maps** загруженные карты перечислены по релизам, там же их можно удалить.

## Запись сессии (session replay)

Flare может воспроизвести то, что пользователь видел в браузере: загрузки страниц, клики, прокрутку и текст на странице, рядом
со спанами и ошибками сессии. Запись делает `@apardev/flare-browser-replay` — тонкая обёртка над
[rrweb](https://github.com/rrweb-io/rrweb), отправляющая запись на `POST /v1/replay` Flare.Ingest. Она необязательна и
ничего не записывает, пока вы её не запустите.

```bash
npm install @apardev/flare-browser-replay
```

```ts
import { startReplay } from '@apardev/flare-browser-replay';

const replay = startReplay({
  endpoint: 'https://flare.example.com:4318',
  service: 'my-web-app',
  ingestKey: '<ключ приёма, ограниченный браузером>',
  sampleRate: 0.25 // необязательно: записывать одну сессию из четырёх
});
```

Origin нужно разрешить так же, как для OTLP (см. [Разрешите origin вашего сайта](#разрешите-origin-вашего-сайта)); подходит
тот же ключ приёма, а ключ, ограниченный сервисами, должен содержать `my-web-app`. Для уже работающего ClickHouse нужна
[миграция 0090](../../../db/clickhouse/0090_web_replay_chunks.sql).

### Связь записи со спанами сессии

Запись хранится под идентификатором сессии, и Flare показывает её на странице этой сессии. Спаны попадают на ту же страницу,
если несут этот идентификатор в атрибуте `session.id`. `replay.sessionId` — идентификатор этой вкладки, скопируйте его в
свои спаны:

```ts
import type { Context } from '@opentelemetry/api';
import type { ReadableSpan, Span, SpanProcessor } from '@opentelemetry/sdk-trace-web';

class SessionIdProcessor implements SpanProcessor {
  onStart(span: Span, _context: Context) { span.setAttribute('session.id', replay.sessionId); }
  onEnd(_span: ReadableSpan) {}
  forceFlush() { return Promise.resolve(); }
  shutdown() { return Promise.resolve(); }
}
// добавьте в spanProcessors у WebTracerProvider перед BatchSpanProcessor
```

Затем откройте **Sessions**, выберите сессию и нажмите **Смотреть запись**. Кнопка **Показать в записи** в строке спана
переводит плеер к началу этого спана. Сессия с записью, но без спанов, по-прежнему доступна по адресу `/sessions/<id>`.

### Что скрывается

Всё, что введено в поля форм, заменяется звёздочками, а поле пароля — всегда. Дальше:

| Чтобы | Сделайте |
| --- | --- |
| Полностью исключить элемент (в записи он будет пустым блоком) | добавьте атрибут `data-flare-block` или класс `flare-block` |
| Скрыть текст элемента | добавьте `data-flare-mask` или класс `flare-mask` |
| Скрыть весь текст на странице | `startReplay({ ..., maskAllText: true })` |
| Показывать поля форм как введено | `startReplay({ ..., maskAllInputs: false })` (не рекомендуется) |

Скрытие происходит в браузере до отправки, поэтому оно настолько надёжно, насколько надёжна разметка: Flare не знает, что
внутри записанного события, а любой владелец ключа приёма может отправить любые события. Посмотрите несколько записей, прежде
чем включать запись для всех пользователей, и пометьте всё личное, что не является полем формы (страница аккаунта, адрес в
тексте). Таблицы стилей встраиваются в запись, а изображения и шрифты загружаются с вашего сайта при просмотре, поэтому
браузер зрителя должен до него дотягиваться.

### Параметры

| Параметр | По умолчанию | Значение |
| --- | --- | --- |
| `sessionId` | один на вкладку, хранится в `sessionStorage` | Идентификатор, под которым хранится запись. Задайте, чтобы использовать уже имеющийся. |
| `sampleRate` | `1` | Доля записываемых сессий, решается один раз на сессию. |
| `flushIntervalMs` | `5000` | Как часто отправляется пакет. |
| `maxEventsPerChunk` | `500` | Пакет уходит раньше при таком числе событий. |
| `maxDurationMs` | один час | Запись останавливается через это время; дашборд показывает час. |
| `checkoutEveryMs` | выключено | Делать новый снимок страницы с таким интервалом, чтобы запись можно было начать позже в долгой сессии. |
| `mode` | `'session'` | `'session'` записывает всё; `'onError'` записывает только вокруг ошибок (см. ниже). |
| `bufferMs` | `30000` | Только `onError`: насколько далеко назад уходит запись ошибки. |
| `afterErrorMs` | `10000` | Только `onError`: сколько запись продолжается после ошибки. |
| `captureErrors` | `true` | Только `onError`: считать необработанные ошибки и необработанные отклонения промисов ошибками, которые нужно сохранить. |
| `captureConsole` | выкл. | Записывать вывод консоли: `true` для `warn` и `error` или список уровней (`'log'`, `'info'`, `'warn'`, `'error'`, `'debug'`). |
| `captureNetwork` | `false` | Записывать запросы `fetch`, `XMLHttpRequest` и beacon (URL, статус, время). |
| `networkKeepQuery` | `false` | Сохранять строку запроса и фрагмент в URL записанных запросов. |
| `networkDetail` | выкл. | С `captureNetwork`: записывать метод (`true`) и, при желании, `{ headers: [имена], bodies: true, bodyLimit }`. См. ниже. |
| `captureCanvas` | выкл. | Записывать содержимое элементов `<canvas>`: `true` для 1 кадра в секунду или число от 1 до 10. |
| `blockSelector`, `maskTextSelector` | `[data-flare-block]`, `[data-flare-mask]` | Селекторы для двух атрибутов выше. |
| `onStop` | нет | Вызывается с причиной, когда запись останавливается сама. |

`replay.stop()` завершает запись и отправляет остаток; `replay.flush()` отправляет записанное на данный момент.

### Записывать только вокруг ошибки

Запись каждой сессии занимает больше всего места и больше всего времени на просмотр. С `mode: 'onError'` запись хранит в
памяти только последние секунды и ничего не отправляет, пока что-то не пойдёт не так:

```ts
const replay = startReplay({ endpoint, service: 'my-web-app', ingestKey, mode: 'onError', bufferMs: 30_000, afterErrorMs: 10_000 });

try { await checkout(); } catch (err) { report(err); replay.keep(); }   // ошибка, которую вы обработали сами
```

Необработанная ошибка или необработанное отклонение промиса делают то же самое без кода (отключается через
`captureErrors: false`). При ошибке запись отправляет накопленное (от `bufferMs` до удвоенного `bufferMs` до ошибки,
начиная со снимка страницы) и продолжается ещё `afterErrorMs`. Затем события отбрасываются до следующего снимка, поэтому
сессия с несколькими ошибками даёт несколько записей, а сессия без ошибок не оставляет ничего. До ошибки ничего не уходит
из браузера, но страница всё это время записывается в память, поэтому правила маскирования выше действуют в обоих режимах.

### Показать вывод консоли и запросы

Запись может показывать рядом с видео, что страница выводила в консоль и какие запросы делала. Оба варианта выключены по
умолчанию, потому что ни то, ни другое не скрывается так, как поля форм:

```ts
const replay = startReplay({ endpoint, service: 'my-web-app', ingestKey, captureConsole: ['warn', 'error'], captureNetwork: true });
```

- **Консоль**: выбранные уровни, а также необработанные ошибки и отклонения промисов как записи `error`. Каждый аргумент
  обрезается до 500 символов и 3 уровней вложенности. Всё, что страница выводит, сохраняется как есть, поэтому не выводите то,
  что не стали бы хранить.
- **Сеть**: запросы `fetch`, `XMLHttpRequest` и beacon в виде URL, статуса и длительности. Заголовки и тела не
  записываются, если их не запросит `networkDetail`. Строка запроса и фрагмент удаляются из URL, если не задан `networkKeepQuery`; собственные отправки
  рекордера исключены. Браузер, не сообщающий статус ответа, статуса не покажет.
- **Подробности запросов** (`networkDetail`, по умолчанию выключено): метод запроса и, при желании, заголовки и тела. При этом
  рекордер оборачивает `fetch` и `XMLHttpRequest`, чего простой список не делает. `networkDetail: true` записывает только
  метод. `networkDetail: { headers: ['content-type', 'x-request-id'], bodies: true }` дополнительно сохраняет перечисленные
  заголовки запроса и ответа, а также тела запроса и ответа для неудачных запросов (статус 400 и выше), только JSON. Имена
  заголовков, похожие на учётные данные (authorization, cookie, всё с token, secret, key или session), отклоняются, даже если
  вы их указали, а значения обрезаются до 200 символов. В теле значения ключей вроде `password`, `token`, `secret`, `apiKey` и
  `card` заменяются на `[redacted]`, а текст обрезается до `bodyLimit` символов (по умолчанию 2048, от 256 до 8192). Остальные
  значения сохраняются так, как их отправила страница, поэтому не включайте `bodies`, если ответы с ошибками содержат
  персональные данные. У строки с записанными подробностями есть стрелка, которая их раскрывает.

Под плеером **Консоль** и **Сеть** перечисляют записи. Строка открывает запись в этот момент, а строки после текущей позиции
затемнены.

### Показать содержимое canvas

`<canvas>` (график, карта, рисунок, игра) не входит в разметку страницы, поэтому в обычной записи он пустой. Включите
`captureCanvas`, чтобы записывать то, что он рисует:

```ts
const replay = startReplay({ endpoint, service: 'my-web-app', ingestKey, captureCanvas: 2 });   // 2 кадра в секунду
```

- По умолчанию выключено. Пиксели нельзя скрыть так, как текст, поэтому canvas, рисующий персональные данные, сохраняется как
  нарисован. Canvas внутри элемента, подходящего под `blockSelector` или `maskTextSelector`, не записывается никогда, а
  `maskAllText: true` отключает запись canvas.
- Canvas без изменений ничего не отправляет. Каждый кадр уменьшается до 800 пикселей по длинной стороне, сжимается в WebP
  (в PNG, если браузер не умеет WebP) и отбрасывается, если всё равно больше 150 КБ. После примерно 25 МБ кадров сессия
  перестаёт записывать canvas.
- Работают и 2D-, и WebGL-canvas. Canvas, на котором нарисовано кросс-доменное изображение без CORS, браузер прочитать не
  может, он остаётся пустым.
- В воспроизведении кадры рисуются на canvas по ходу записи, а при перемотке показывается последний кадр до этого момента.
  Canvas, меняющийся быстрее частоты кадров, выглядит как слайд-шоу.

### Как найти запись

В **Sessions** у сессии с записью рядом с идентификатором стоит значок воспроизведения, а переключатель **Только с записью**
скрывает остальные. В **Errors** у вхождения, чей спан несёт атрибут `session.id`, есть ссылка **Открыть запись сеанса**:
она открывает сессию в момент ошибки, на несколько секунд раньше.

### Чего ожидать

- Пакет отправляется каждые несколько секунд и при скрытии вкладки. Неудавшаяся отправка отбрасывается, а не повторяется:
  в записи может быть пропуск, но событие никогда не воспроизведётся дважды. При закрытии вкладки последний пакет размером
  больше примерно 60 КБ может потеряться.
- Запись сама останавливается через час или когда ключ отклонён (`401` или `403`), при этом вызывается `onStop`. Если ключ
  превысил лимит (`429`), запись отбрасывает пакеты на время из `Retry-After` и затем продолжает.
- Записи учитываются в хранении трассировок, а длинная страница с большим числом изменений DOM занимает много места. На
  загруженных сайтах используйте `sampleRate` или `mode: 'onError'`. Запись учитывается в лимитах ключа по байтам
  (лимиты **Bytes** в минуту и в сутки в **Limits** ключа, те же, что у OTLP), но не в лимитах по событиям, потому что событие записи не
  является записью телеметрии.
  Чтобы ограничить только записи, задайте лимиты **Replay bytes** в том же диалоге: они действуют лишь на записи, сверх
  лимитов **Bytes**, поэтому журналы, трассировки и метрики не отклоняются из-за того, что запись исчерпала свой бюджет.
- Если сохранённая запись больше 40 МБ, плеер предупредит перед загрузкой её во вкладку.

## Проверка

Откройте страницу **Traces** и отфильтруйте по сервису `my-web-app`. Загрузка
страницы отображается как трасса `documentLoad` с дочерними спанами
`documentFetch` и `resourceFetch`. Вызовы API, передающего `traceparent`,
отображаются как одна трасса через оба сервиса.

## Ограничения

- Браузер может потерять последний пакет при закрытии вкладки.
