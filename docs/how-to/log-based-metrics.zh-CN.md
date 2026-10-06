# 如何把日志搜索变成指标

在日志到达时统计符合过滤条件的日志数，并将结果存为指标。基于该指标的图表和告警规则读取的是一个小型时间序列，而不是每次都扫描日志表，因此在繁忙的实例上，“checkout 每分钟的错误数”依然开销很小。

目前还没有对应的仪表板页面，日志指标通过 API 管理。

## 前提条件

- Member 或 Admin 账户，或该账户的[个人访问令牌](configure-authentication.zh-CN.md#个人访问令牌)。Viewer 不能创建日志指标。
- Flare 的 API 地址，示例中为 `http://localhost:8080`。

## 创建日志指标

下面的示例统计 `checkout` 服务的错误日志（严重级别 17 及以上），并按 `http.route` 属性拆分：

```bash
curl -X POST http://localhost:8080/api/log-metrics \
  -H "Authorization: Bearer $FLARE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Checkout errors",
    "metricName": "logs.checkout.errors",
    "condition": { "services": ["checkout"], "severityNumbers": [17, 18, 19, 20, 21, 22, 23, 24] },
    "groupBy": ["http.route"]
  }'
```

- `metricName` 是你用于绘图和告警的名称。请以 `logs.` 开头，避免与应用记录的指标重名。它必须以字母开头，且只能包含字母、数字、`_`、`.` 和 `-`。
- `condition` 接受与[管道规则](manage-pipeline-rules.zh-CN.md)条件相同的字段：服务、严重级别编号、正文文本和属性条件。空条件会统计所有日志。
- `groupBy` 可选，最多五个属性键。每个键都会成为指标的一个属性。键的取值先从日志自身的属性中读取，再从其资源属性中读取。

指标会在约 30 秒内开始统计新日志。已存储的日志不会被统计。

## 使用该指标

打开 **Metrics > Catalog**：`logs.checkout.errors` 会和其他指标一样出现在列表中，并显示其序列数。可在指标浏览器或仪表板面板中绘制它，或为它创建指标告警。它是单位为 `{log}` 的增量求和（delta sum），因此它在某个时间窗口内的总和就是该窗口内符合条件的日志数。

每个数据点还带有这些日志的 `service.name`。

## 控制基数

分组值的每一种不同组合都是一条序列。请按取值较少的属性分组，例如路由或状态码，而不要按用户 ID 或请求 ID 分组。

作为保险措施，一个指标在每次刷新中最多输出 1000 种不同的组合。超出的组合会被计入值 `__overflow__`，总数仍然正确，但细节会丢失。如果在图表中看到 `__overflow__`，请移除导致它的分组键。

## 修改或删除指标

```bash
curl http://localhost:8080/api/log-metrics -H "Authorization: Bearer $FLARE_TOKEN"
curl -X PUT http://localhost:8080/api/log-metrics/<id> ...   # same body as create
curl -X DELETE http://localhost:8080/api/log-metrics/<id> -H "Authorization: Bearer $FLARE_TOKEN"
```

设置 `"enabled": false` 可暂停统计而不删除定义。删除或修改指标不会改变已存储的数据点，它们会保留到指标保留期将其清除为止。
