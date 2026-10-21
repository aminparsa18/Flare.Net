# 如何跟踪发布及其引入的错误

标记发布会告诉 Flare 某个服务的某个版本已经上线，并记录其提交和部署时间。随后 **Releases** 页面会按版本列出首次出现在该版本中的异常分组。

## 在流水线中标记发布

在部署步骤中使用 Member 或 Admin 的个人访问令牌（**Settings > Access tokens**）调用 API：

```bash
curl -X PUT "$FLARE_URL/api/releases" \
  -H "Authorization: Bearer $FLARE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"service":"orders-api","version":"2.4.0","commit":"'"$GIT_SHA"'","url":"'"$RUN_URL"'"}'
```

| 字段 | 含义 |
| --- | --- |
| `service` | 服务的 `service.name`。必填 |
| `version` | 服务实际上报的 `service.version`，必须完全一致。必填 |
| `commit`、`url`、`notes` | 可选。`url` 必须是指向提交、拉取请求或流水线运行的 http(s) 链接 |
| `deployedAt` | 上线时间（ISO 8601）。默认为当前时间 |

对同一服务和版本再次标记会更新该标记。在本地栈上，`flare releases mark orders-api 2.4.0 --commit $SHA` 效果相同，`flare releases list --service orders-api` 显示结果，`flare releases delete` 删除标记。删除标记不会影响遥测数据。

## 查看 Releases 页面

从菜单打开 **Releases** 并选择服务。每一行是一个已标记的版本，显示部署时间、提交和 **新增错误**：最早记录的出现时间落在该版本中的异常分组数量。展开一行可按出现次数从高到低查看这些分组，每个分组都链接到按该服务和版本过滤的 Errors 页面。

若某分组在部署前 30 天内没有出现过，则视为新增。沉寂时间更长后又回来的分组，会在其回归的版本中被视为新增。

## 查找嫌疑提交

每个新增错误旁的 **嫌疑提交** 会列出最可能引入它的提交。Flare 向代码托管服务查询本次发布与上一次发布之间的提交，只保留修改过该错误堆栈中文件的提交，抛出位置所在文件优先。每条记录显示提交、作者以及它修改的堆栈文件。**Compare** 链接会在托管服务上打开整个范围。

前提条件：

- 已为该服务[配置源代码仓库](link-exceptions-to-source-code.zh-CN.md)，私有仓库还需访问令牌；
- 本次发布及上一次发布都有提交：标记发布时的 `commit`，或 `service.version`（如 `1.4.2+3f9c2ab`）中 `+` 之后的 SHA；
- 堆栈帧能对应到仓库中的文件（否则请在源码链接中设置路径前缀）。

Flare 只检查该范围内最新的 40 个提交，范围很长时可能漏掉嫌疑提交，页面会给出提示。修改过堆栈文件的提交只是候选，并非证据：它是最值得先看的最短清单。

## 回归

在 [Errors 页面](triage-errors.zh-CN.md) 上标记为已解决的分组，如果在此前未出现过的版本中再次出现，会显示为 **Regressed**。这仅依赖 `service.version`，无论是否标记了发布。Releases 页面回答的是另外两个问题：某个版本新增了哪些错误，以及哪些已解决的分组在该版本中重新出现。**Regressions** 列统计后者，展开一行时会先列出这些分组及其解决时间。仅当分组确实在该服务的该版本中出现过才会计入。

## 图表上的部署标记

Logs 的数量图和 Metrics 图表会在时间范围内每次已部署的发布处绘制一条虚线竖线，针对图表筛选的服务（未筛选时为所有服务）。悬停在线上可查看服务、版本和时间。最多绘制最新的 12 个。仪表盘上的 Metrics 和 Logs 面板也会显示。

## 对有问题的发布告警

创建告警规则，条件选择 **发布后的新错误**。选择要监视的服务（未选择时为所有服务）、阈值（如 `>= 1`）和时间窗口（如 3600 秒）。规则会查看窗口内标记的发布，当其中最差的一个引入了不少于该数量的新异常组时触发，因此两次部署之间不会触发。通知会指出新增分组最多的发布，并链接到 Releases 页面。它没有“无数据”选项，因为没有新错误才是正常状态。

通过 API 则是 `conditionKind: "ReleaseErrors"`，并带上 `exceptionCondition: { "exceptionType": "", "filter": { "services": ["orders-api"] } }`；只读取过滤器中的服务。

## 限制

- 只统计 span 上的异常事件，按类型和规范化消息（ID 和数字被折叠）分组，与 Errors 页面一致。
- 没有 `service.version` 的遥测不会归属到任何发布。
- 新增错误数量在页面加载时根据你的 span 计算，且仅针对所选服务。
