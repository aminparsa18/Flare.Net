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

```bash
curl -X PUT http://localhost:8080/api/ingest-keys/$KEY_ID/origins \
  -H "Authorization: Bearer $FLARE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "origins": ["https://app.example.com"] }'
```

受限密钥只接受 `Origin` 在列表中的浏览器请求；curl 和服务器端导出器会收到 `403`。列表中的来源
也会通过 CORS，因此使用受限密钥时无需设置 `Otlp__AllowedOrigins`。更改在 30 秒内生效。
空列表表示取消限制。

这并不会让密钥变成机密：在浏览器之外任何人都可以伪造 `Origin` 头。请设置
[按密钥的限额](configure-authentication.zh-CN.md#摄取-api-密钥)，以限制泄露的密钥能发送的数据量。

## 验证是否生效

打开 **Traces** 页面，按服务 `my-web-app` 过滤。一次页面加载会显示为 `documentLoad` trace，
其子 span 为 `documentFetch` 和 `resourceFetch`。对传播 `traceparent` 的 API 的调用会显示为
横跨两个服务的同一条 trace。

## 限制

- 这里只涉及 trace。Web vitals 和 JavaScript 错误需要额外的插桩，压缩后的 bundle 的堆栈跟踪
  目前尚未符号化。
- 关闭标签页时，浏览器可能会丢失最后一批数据。
