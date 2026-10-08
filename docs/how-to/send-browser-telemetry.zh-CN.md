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

如果要求使用[摄取 API 密钥](configure-authentication.zh-CN.md#摄取-api-密钥)，导出器需要设置
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
[按密钥的限额](configure-authentication.zh-CN.md#摄取-api-密钥)，以限制泄露的密钥能发送的数据量。

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

## 还原压缩后的堆栈跟踪

生产环境的 bundle 是压缩过的，因此错误堆栈看起来像 `at o (…/app-abc123.js:1:30)`。上传构建产生的 source map 后，Flare 会在 **Errors** 页面显示原始的函数、文件、行和列；堆栈被改写的记录会带有 **Source map applied** 标记。

先让应用上报其构建所对应的版本，值要与上传时使用的一致：

```ts
resourceFromAttributes({ 'service.name': 'my-web-app', 'service.version': '1.4.2' })
```

然后在 CI 步骤中从构建输出上传 map，并在 `FLARE_API_TOKEN` 中提供管理员的[个人访问令牌](configure-authentication.md)：

```bash
flare sourcemaps upload ./dist --url https://flare.example.com \
  --service my-web-app --release 1.4.2
```

每个 `dist/**/*.map` 文件会以去掉 `.map` 的路径存储，例如 `dist/assets/app-abc123.js.map` 存为 `assets/app-abc123.js`。堆栈帧会匹配其脚本 URL 路径所能以之结尾的最长已存储路径。`flare sourcemaps list` 显示已存储的内容，`flare sourcemaps delete --service my-web-app --release 1.4.2` 删除某个版本。原始 API 为 `PUT /api/source-maps?service=&version=&bundle=`，请求体为 map 的 JSON。

还原在打开某条记录时进行，因此错误发生之后才上传的 map 同样适用。map 通过 `service.name` 加 `service.version`（或 `vcs.revision` 资源属性）匹配，所以只要旧版本的错误仍在出现，就请保留它们的 map。接受最大 50 MB 的 map；索引 map（`sections`）会被拒绝。如果不想公开源码，请不要公开提供 `.map` 文件，也不要把它们部署到 bundle 里。

## 验证是否生效

打开 **Traces** 页面，按服务 `my-web-app` 过滤。一次页面加载会显示为 `documentLoad` trace，
其子 span 为 `documentFetch` 和 `resourceFetch`。对传播 `traceparent` 的 API 的调用会显示为
横跨两个服务的同一条 trace。

## 限制

- 关闭标签页时，浏览器可能会丢失最后一批数据。
