# 如何将浏览器遥测发送到 Flare

使用 OpenTelemetry JavaScript SDK，将 Web 应用的页面加载、`fetch` span 和 JavaScript
错误发送到 Flare。浏览器 span 与 .NET 后端共用同一个 trace ID，因此可以从一次缓慢的点击
一路追踪到 API 调用及其数据库查询。

Flare 的 OTLP/HTTP 接收器可直接接收 SDK 的 JSON 导出。唯一额外的步骤是告诉它哪些来源
（origin）可以向它发送数据。

## 前提条件

- 一个正在运行的 Flare 实例，且用户浏览器可以访问其 OTLP/HTTP 端口（`4318`）。
- 一个可以添加 npm 包的 Web 应用。

## 允许你的站点来源

除非接收器响应 CORS 预检请求，否则浏览器会拒绝跨域请求。Flare 默认不发送任何 CORS 头。
请在 Flare.Ingest 中列出你站点的来源：

```bash
Otlp__AllowedOrigins__0=https://app.example.com
Otlp__AllowedOrigins__1=http://localhost:5173
```

重启 Flare.Ingest。仅允许 `POST`，允许的请求头为 `Content-Type`、`Content-Encoding`
和 `Authorization`。`*` 表示允许任意来源。

## 安装并配置 SDK

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

你的 API 还必须在自己的 CORS 策略中允许 `traceparent` 头，否则浏览器会拦截该调用。

## 摄取密钥

如果要求使用[摄取 API 密钥](../admin/configure-authentication.zh-CN.md#摄取-api-密钥)，导出器需要设置
`headers: { Authorization: 'Bearer <密钥>' }`。该密钥会随 JavaScript 一起发布，每位访问者都能看到。
请为浏览器创建专用密钥，并将其限制在你站点的来源：

在仪表板中打开 **Settings > Ingest keys**，点击密钥上的地球图标即可编辑其允许的来源和服务；终端中可使用 `flare apikey scope`。对应的 API 调用：

```bash
curl -X PUT http://localhost:8080/api/ingest-keys/$KEY_ID/origins \
  -H "Authorization: Bearer $FLARE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "origins": ["https://app.example.com"] }'
```

受限密钥只接受 `Origin` 在列表中的浏览器请求；curl 和服务器端导出器会收到 `403`。列表中的来源
也会通过 CORS，因此使用受限密钥时无需设置 `Otlp__AllowedOrigins`。更改在 30 秒内生效。
空列表表示取消限制。

还可以将密钥限定为只能为指定服务写入数据。包含其他 `service.name`（或没有 `service.name`）的导出
会被拒绝并返回 `403`：

```bash
curl -X PUT http://localhost:8080/api/ingest-keys/$KEY_ID/services \
  -H "Authorization: Bearer $FLARE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "services": ["my-web-app"] }'
```

这并不会让密钥变成机密：在浏览器之外任何人都可以伪造 `Origin` 头。请设置
[按密钥的限额](../admin/configure-authentication.zh-CN.md#摄取-api-密钥)，以限制泄露的密钥能发送的数据量。

## 上报 JavaScript 错误

Flare 的 **Errors** 页面会对记录在 span 上的异常进行分组。把每个未捕获的错误作为带异常事件的短 span 上报：

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

错误会按类型和消息分组显示在 **Errors** 页面。添加资源过滤条件 `telemetry.sdk.language = webjs` 即可只看浏览器错误。请抛出 `Error` 对象：抛出的字符串没有类型，因此不会被分组。压缩后的 bundle 的堆栈跟踪目前尚未符号化。

## 上报 Web vitals

把每个 Core Web Vital 记录为名为 `browser.web_vital.<name>`（`lcp`、`inp`、`cls`、`fcp`、`ttfb`）的直方图，并以 `rating`（`good`、`needs-improvement`、`poor`）作为属性：

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

在 **Dashboards** 页面选择 **Create from template > Web vitals**。面板展示各指标的百分位数；Google 的目标值按 p75 衡量（LCP 低于 2.5 秒，INP 低于 200 毫秒，CLS 低于 0.1）。

## 按路由拆分 vitals

在传给 `histogram.record` 的属性中加入 `url.template`(如 `/orders/:id` 这样的路由模式,而不是原始 URL)。在**前端**页面展开服务即可看到各路由的 vitals。**页面加载**列统计 FCP 测量次数,每次页面加载一次。

## 还原压缩后的堆栈跟踪

生产环境的 bundle 是压缩过的，因此错误堆栈看起来像 `at o (…/app-abc123.js:1:30)`。上传构建产生的 source map 后，Flare 会在 **Errors** 页面显示原始的函数、文件、行和列；堆栈被改写的记录会带有 **Source map applied** 标记。

先让应用上报其构建所对应的版本，值要与上传时使用的一致：

```ts
resourceFromAttributes({ 'service.name': 'my-web-app', 'service.version': '1.4.2' })
```

然后在 CI 步骤中从构建输出上传 map，并在 `FLARE_API_TOKEN` 中提供管理员的[个人访问令牌](../admin/configure-authentication.md)：

```bash
flare sourcemaps upload ./dist --url https://flare.example.com \
  --service my-web-app --release 1.4.2
```

每个 `dist/**/*.map` 文件会以去掉 `.map` 的路径存储，例如 `dist/assets/app-abc123.js.map` 存为 `assets/app-abc123.js`。堆栈帧会匹配其脚本 URL 路径所能以之结尾的最长已存储路径。`flare sourcemaps list` 显示已存储的内容，`flare sourcemaps delete --service my-web-app --release 1.4.2` 删除某个版本。原始 API 为 `PUT /api/source-maps?service=&version=&bundle=`，请求体为 map 的 JSON。

还原在打开某条记录时进行，因此错误发生之后才上传的 map 同样适用。map 通过 `service.name` 加 `service.version`（或 `vcs.revision` 资源属性）匹配，所以只要旧版本的错误仍在出现，就请保留它们的 map。接受最大 50 MB 的 map；索引 map（`sections`）会被拒绝。如果不想公开源码，请不要公开提供 `.map` 文件，也不要把它们部署到 bundle 里。

## 在“前端”页面查看

打开**前端**(用户菜单的**更多**列表中)。页面按服务显示所选时段内 LCP、INP、CLS、FCP 和 TTFB 的第 75 百分位(按 Google 阈值着色)及浏览器评为良好的测量占比,并列出浏览器端的主要 JavaScript 错误。这些错误是带有 `telemetry.sdk.language = webjs` 的资源产生的异常,点击**在错误页中打开**可继续查看。**设置 > Source maps** 按版本列出已上传的映射并可删除。

## 录制会话回放

Flare 可以回放用户在浏览器中看到的内容：页面加载、点击、滚动和页面上的文字，并与该会话的 span 和错误并排播放。录制器是
`@apardev/flare-browser-replay`，它是对 [rrweb](https://github.com/rrweb-io/rrweb) 的薄封装，会把录制内容发送到
Flare.Ingest 的 `POST /v1/replay`。它是可选的，只有在你启动之后才会录制。

```bash
npm install @apardev/flare-browser-replay
```

```ts
import { startReplay } from '@apardev/flare-browser-replay';

const replay = startReplay({
  endpoint: 'https://flare.example.com:4318',
  service: 'my-web-app',
  ingestKey: '<限定浏览器使用的摄取密钥>',
  sampleRate: 0.25 // 可选：每四个会话录制一个
});
```

来源需要像 OTLP 一样允许（见[允许你的站点来源](#允许你的站点来源)）；可以使用同一个摄取密钥，限定了服务的密钥必须包含
`my-web-app`。已有的 ClickHouse 需要执行[迁移 0090](../../../db/clickhouse/0090_web_replay_chunks.sql)。

### 将录制与会话的 span 关联

录制以会话 ID 存储，Flare 在该会话的页面上显示它。当 span 以 `session.id` 属性携带同一个 ID 时，它们会出现在同一页面。
`replay.sessionId` 是当前标签页的 ID，请把它复制到你的 span 上：

```ts
import type { Context } from '@opentelemetry/api';
import type { ReadableSpan, Span, SpanProcessor } from '@opentelemetry/sdk-trace-web';

class SessionIdProcessor implements SpanProcessor {
  onStart(span: Span, _context: Context) { span.setAttribute('session.id', replay.sessionId); }
  onEnd(_span: ReadableSpan) {}
  forceFlush() { return Promise.resolve(); }
  shutdown() { return Promise.resolve(); }
}
// 加入 WebTracerProvider 的 spanProcessors，放在 BatchSpanProcessor 之前
```

然后打开 **Sessions**，选择该会话并点击 **观看录制**。span 行上的 **在回放中显示** 会把播放器跳到该 span 的开始时间。
有录制但没有 span 的会话仍可通过 `/sessions/<id>` 访问。

### 哪些内容会被遮盖

在表单字段中输入的所有内容都会被替换为星号，密码字段始终如此。此外：

| 目的 | 做法 |
| --- | --- |
| 完全排除某个元素（回放时是一个空框） | 添加属性 `data-flare-block` 或类 `flare-block` |
| 隐藏某个元素的文字 | 添加 `data-flare-mask` 或类 `flare-mask` |
| 隐藏页面上的所有文字 | `startReplay({ ..., maskAllText: true })` |
| 按输入原样显示表单字段 | `startReplay({ ..., maskAllInputs: false })`（不推荐） |

遮盖在浏览器中、发送之前完成，因此效果取决于你的标记：Flare 无法知道录制的事件里有什么，任何持有摄取密钥的人都可以发送任意事件。
在对所有用户开启回放之前，请先查看几段录制，并为所有不是表单字段的个人信息（账户页、渲染出来的地址文字）添加标记。样式表会内嵌到录制中，
但图片和字体在观看时从你的站点加载，因此观看者的浏览器必须能访问它。

### 选项

| 选项 | 默认值 | 含义 |
| --- | --- | --- |
| `sessionId` | 每个标签页一个，保存在 `sessionStorage` | 录制存储所用的 ID。设置它即可复用已有的 ID。 |
| `sampleRate` | `1` | 录制的会话比例，每个会话只决定一次。 |
| `flushIntervalMs` | `5000` | 发送一批数据的间隔。 |
| `maxEventsPerChunk` | `500` | 达到该事件数时提前发送。 |
| `maxDurationMs` | 一小时 | 超过该时长后停止录制；仪表板列出一小时。 |
| `checkoutEveryMs` | 关闭 | 按此间隔重新拍摄页面快照，使回放可以从长会话的中途开始。 |
| `blockSelector`、`maskTextSelector` | `[data-flare-block]`、`[data-flare-mask]` | 上述两个属性对应的选择器。 |
| `onStop` | 无 | 录制器自行停止时，带着原因调用。 |

`replay.stop()` 结束录制并发送剩余内容；`replay.flush()` 发送目前已录制的内容。

### 预期行为

- 每隔几秒以及标签页被隐藏时发送一批。发送失败的批次会被丢弃而不会重试：回放可能出现空缺，但不会重复播放同一事件。
  关闭标签页时，超过约 60 KB 的最后一批可能丢失。
- 录制在一小时后或密钥被拒绝（`401` 或 `403`）时自行停止，并调用 `onStop`。
- 录制计入 Traces 的保留策略，DOM 变化很多的长页面体积较大。访问量大的站点请使用 `sampleRate`。回放目前不计入按密钥的摄取限制。
- 已存储的录制超过 40 MB 时，播放器会在加载到标签页之前给出提示。

## 验证是否生效

打开 **Traces** 页面，按服务 `my-web-app` 过滤。一次页面加载会显示为 `documentLoad` trace，
其子 span 为 `documentFetch` 和 `resourceFetch`。对传播 `traceparent` 的 API 的调用会显示为
横跨两个服务的同一条 trace。

## 限制

- 关闭标签页时，浏览器可能会丢失最后一批数据。
