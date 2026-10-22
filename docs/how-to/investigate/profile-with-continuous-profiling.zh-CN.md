# 如何使用持续性能剖析

性能剖析（profiles）显示代码的 CPU 时间或内存花在了哪些函数上。Flare 在与日志、链路和指标相同的 OTLP 端口上接收 OpenTelemetry 的 **profiles** 信号，连同调用栈存储每个样本，并把合并后的结果绘制成火焰图。在某个链路 span 期间采集的样本带有该 span 的 ID，因此你可以从一个慢 span 直接跳到当时正在运行的代码。

OTLP profiles 在 OpenTelemetry 中仍处于 **Alpha** 阶段。传输格式在版本之间仍可能变化，升级发送端后请重新检查。

## 向 Flare 发送性能剖析数据

把 OTLP profiles 发送端指向你已在使用的端点：

| 传输 | 地址 |
| --- | --- |
| gRPC | `localhost:4317`（`ProfilesService/Export`） |
| HTTP | `POST http://localhost:4318/v1development/profiles`（protobuf 或 JSON） |

性能剖析数据与其他信号使用相同的摄取密钥认证、请求大小上限和每密钥限额。**Ingestion** 和 **Pipeline** 页面会把 Profiles 与 Logs、Traces、Metrics 并列显示。

OTLP profiles 还很新，目前发送端不多。OpenTelemetry Collector 可以转发它收到的性能剖析数据：启动时加上 `--feature-gates=service.profilesSupport`，并在其 `otlp_http` 导出器上设置 `profiles_endpoint: http://localhost:4318/v1development/profiles`。如果你启用了[摄取 API 密钥](../admin/configure-authentication.zh-CN.md#摄取-api-密钥)，请把密钥加到导出器的 `headers` 中。压缩（gzip）的请求体会被接受。

Collector 的 `pprof` 接收器（v0.162，Alpha）可以读取 pprof 文件和 Go 的 `/debug/pprof` 端点，但目前发出的样本不带调用栈，因此据此生成的火焰图只有根节点。出现这种情况时 Flare 会提示你。

### 用 eBPF profiler 分析整台主机

目前实用的发送端是 OpenTelemetry 的 **eBPF profiler**。它无需改动代码即可分析 Linux 主机上的每个进程，并发送调用栈，包括 Python、Java、.NET、Go 和原生帧。Flare 已用 `otel/opentelemetry-collector-ebpf-profiler` 镜像 **0.163.2** 验证。由于 profiles 仍是 Alpha，请使用你测试过的版本，并在每次升级后重新检查。

容器需要 `--privileged` 和 `--pid=host`，以及带 BTF 的内核（任何较新的发行版都可以）。profiler 不知道你的服务名，所以下面的配置把可执行文件名复制到 `service.name`；没有它，profiles 会归入 **（无服务）**。

保存为 `ebpf-profiler.yaml`：

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

在 Flare 旁边运行它（把 `endpoint` 指向你的 Flare.Ingest 主机；如果启用了[摄取 API 密钥](../admin/configure-authentication.md#ingest-api-keys)，取消 `headers` 的注释）：

```bash
docker run -d --name flare-ebpf --privileged --pid=host --network host \
  -v /sys/kernel/debug:/sys/kernel/debug -v /sys/kernel/tracing:/sys/kernel/tracing \
  -v $PWD/ebpf-profiler.yaml:/etc/otel/config.yaml:ro \
  otel/opentelemetry-collector-ebpf-profiler:0.163.2 \
  --config=/etc/otel/config.yaml --feature-gates=+service.profilesSupport
```

在 **Docker Desktop** 上，Linux 虚拟机没有挂载 debugfs 和 tracefs，profiler 会报错 `neither debugfs nor tracefs are mounted`。每次虚拟机启动后挂载一次：

```bash
docker run --rm --privileged --pid=host alpine nsenter -t 1 -m -- sh -c \
  'mount -t debugfs none /sys/kernel/debug; mount -t tracefs none /sys/kernel/tracing'
```

打开 **Profiles**，选择例如 `python3.12` 的服务和 `samples` 类型。解释器（Python、Java、.NET 等）的帧会显示名称；原生帧仍为 `module+0xaddress`。

### 用 curl 试一试

下面的请求发送一份包含两个调用栈的性能剖析数据。每个样本按索引指向一个调用栈，调用栈从叶子开始列出栈帧。每张表的索引 0 都是空条目。

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

打开 **Profiles**，选择 `checkout` 服务和 `cpu` 样本类型，即可看到 `main` > `handle` > `db.Exec`。

## 打开 Profiles 页面

1. 在 **More** 菜单中打开 **Profiles**。
2. 选择**服务**、**样本类型**（例如 `cpu` 或 `alloc_space`）和时间窗口。序列按服务和样本类型列出，因为不同类型的值不能相加。
3. 火焰图合并窗口内的所有样本。栈帧的宽度是其占总量的比例。悬停在栈帧上可查看总计、百分比和 **self** 值（花在该栈帧本身、而非它所调用函数上的部分）。
4. 点击栈帧可放大。**Reset zoom** 返回完整图。

Flare 最多合并 5,000 个不同的调用栈。超出时页面会显示 **Truncated**，并省略最轻的调用栈。

## 剖析单个 span

1. 打开一条链路并点击某个 span。
2. 在 span 面板中点击 **View profile**。

Profiles 页面会打开，且只包含该 span 期间采集的样本。这需要发送端在每个样本上记录当前活动的 span（即剖析数据到 `trace_id` 和 `span_id` 的关联）。否则图为空；点击 **Clear** 可回到服务级图。

## 查询 API

```bash
# 过去一小时有哪些序列？
curl -s -X POST http://localhost:8080/api/profiles/types \
  -H 'Content-Type: application/json' -d '{"windowMinutes":60}'

# 某个序列合并后的调用树，可选限定到某个 span
curl -s -X POST http://localhost:8080/api/profiles/flamegraph \
  -H 'Content-Type: application/json' \
  -d '{"service":"checkout","sampleType":"cpu","windowMinutes":60,"traceId":"<hex>","spanId":"<hex>"}'
```

火焰图响应是挂在合成根节点 `all` 下的 `{ name, total, self, children }` 树。`sampleUnit` 说明数值单位是纳秒、字节还是普通计数。

## 限制

- 性能剖析数据暂时没有自己的保留策略，与 span 相同。所有信号的保留策略由路线图中的同一项统一跟踪。
- 未符号化的原生栈帧显示为 `module+0x地址`。
