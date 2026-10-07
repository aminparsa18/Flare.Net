# 如何设置数据保留时长并将旧数据迁移到冷存储

默认情况下，Flare 会永久保留所有日志、span、指标和性能剖析数据。保留时长用来限制磁盘占用：每种信号有各自的存活时间，ClickHouse 会删除早于该时间的行，也可以先把过期数据迁移到更便宜的对象存储。只有管理员（Admin）可以修改，已登录的用户都可以查看。

## 在仪表盘中设置保留时长

1. 打开 **Settings**，然后在 Workspace 下选择 **Retention**。
2. 每种信号（logs、traces、metrics、profiles）各有一张卡片。在 **保留（天）** 中输入天数，`0` 表示永久保留该信号。
3. 点击 **应用**。

卡片会显示 **ClickHouse 当前设置**（直接从数据库实时读取），当它与 Flare 上次应用的值不一致时（例如有人手动改了 TTL），会标出 **与上次请求不一致**。非 Flare 写入的 TTL 显示为 **非 Flare 设置的自定义 TTL**。

应用是异步的。在 ClickHouse 设置好新的 TTL 之前，卡片会显示 **应用中…**，同一时间只能运行一项更改。随后 ClickHouse 在后台合并时删除过期数据，因此磁盘空间会在数小时内逐步释放，而不是在更改成功的那一刻。缩短保留时长无法撤销。

服务地图、SLO 和仪表盘背后的预聚合表（如 `service_metrics`）没有 TTL，因此比它们所依据的原始数据存活更久。

## 让某些资源保留更久或更短

在 **按资源的规则** 下点击 **添加规则**，填写资源属性、值和天数，例如 `deployment.environment` = `dev`，`7` 天。资源属性等于该值的行使用这个存活时间。匹配到的第一条规则生效，**默认值（天）** 覆盖其余所有数据。

- 匹配是对资源属性的精确相等，不是前缀或模式，也不针对日志或 span 属性。
- 每种信号最多 20 条规则。
- 规则在行写入时生效。已按旧规则合并的数据保留当时分配的存活时间。

## 将旧数据迁移到冷存储

冷存储默认关闭。使用 cold-storage overlay 启动 Flare 即可开启：

```bash
docker compose -f docker-compose.yml -f docker-compose.cold-storage.yml up -d
```

（`docker-compose.cold-storage.cluster.yml` 是集群版本；使用 Aspire 时调用 `AddFlare().WithColdStorage()`。）这会添加一个 RustFS 容器作为 S3 兼容存储，以及使用它的 ClickHouse 存储策略。之后 Retention 页面上的 **冷存储** 卡片会列出各磁盘及其可用空间，每种信号也会多出 **转入冷存储前（天）** 字段。

该值要小于信号的保留时长，或小于其所有规则中最短的时长，否则数据会在迁移前被删除。`0` 表示不分层。保留时长为 `0` 且设置了迁移时间时，数据会迁入冷存储且永不删除。

冷数据仍在同一张表中，因此搜索、告警和仪表盘照常工作，只是从冷存储读取更慢。迁移在后台进行，需要几分钟到几小时。之后关闭分层会移除迁移规则，但已在冷存储中的数据片段保持原位。

## 使用命令行

```bash
flare retention show
flare retention set logs --days 30
flare retention set logs --days 30 --cold-after 7
flare retention set logs --days 30 --rule deployment.environment=dev:7 --rule k8s.namespace=load-test:1
flare retention set logs --clear-rules
```

`show` 打印每种信号的实时 TTL、迁移时间、规则以及上次更改的状态。`set` 会等待 ClickHouse 应用更改（`--no-wait` 在 API 接受后立即返回）。省略的选项保持当前值，因此单独使用 `--days 14` 不会重置规则。`--rule` 的格式为 `ATTRIBUTE=VALUE:DAYS`，可重复使用，并会替换现有规则。

## 限制

- 天数范围为 0（永久）到 18250（50 年）。
- 有更改正在应用时，再提交更改会被拒绝并返回 `409`。超过 30 分钟仍未完成的更改（例如 API 重启所致）会被报告为失败。
- API 为 `GET /api/retention` 和 `PUT /api/retention`。设计见 [ADR-0143](../../docs-internal/adr/0143-retention-ttl.md)（TTL）、[ADR-0144](../../docs-internal/adr/0144-cold-storage-rustfs.md)（冷存储）和 [ADR-0145](../../docs-internal/adr/0145-per-resource-retention.md)（按资源的规则）。
