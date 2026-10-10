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

## 回归

在 [Errors 页面](triage-errors.zh-CN.md) 上标记为已解决的分组，如果在此前未出现过的版本中再次出现，会显示为 **Regressed**。这仅依赖 `service.version`，无论是否标记了发布。Releases 页面回答的是另一个问题：某个版本新增了哪些错误。

## 限制

- 只统计 span 上的异常事件，按完整的类型和消息分组，与 Errors 页面一致。
- 没有 `service.version` 的遥测不会归属到任何发布。
- 新增错误数量在页面加载时根据你的 span 计算，且仅针对所选服务。
