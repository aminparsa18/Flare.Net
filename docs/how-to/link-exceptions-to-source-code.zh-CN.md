# 如何将异常堆栈跟踪链接到源代码

在 **Exceptions** 页面打开一个分组，可以看到它的示例事件。为服务配置源代码仓库后，
每个堆栈帧中的文件位置会变成链接，指向仓库中该服务所构建提交上的对应文件和行。

## 前提条件

- 堆栈跟踪包含文件位置。构建包含 portable PDB（默认如此）时，.NET 会输出
  `in /src/File.cs:line 42`。
- 你是管理员，或者未启用身份验证。查看者和成员能看到链接，但不能修改配置。

## 在构建中标记提交

Flare 按以下顺序从 span 的资源属性中获取提交：

1. `vcs.ref.head.revision`
2. `vcs.revision`
3. `service.version`

对 .NET 来说，最简单的方式是 SourceLink，它会把提交写入信息版本（`1.2.3+abc1234`），
Flare 读取 `+` 之后的部分。如果 `service.version` 只是 `1.2.3`，它不指向任何提交，
Flare 会回退到下面配置的默认分支或标签。

## 配置仓库

1. 在某个事件行中，点击 **Show stack trace** 旁边的链接图标。
2. 选择托管平台：GitHub、GitLab 或 Azure DevOps。
3. 输入仓库地址，例如 `https://github.com/acme/shop`。Azure DevOps 使用
   `https://dev.azure.com/org/project/_git/repo`。
4. 可选：设置备用分支或标签，例如 `main`。
5. 如果构建路径不是相对于仓库的，请设置**路径前缀**。它是构建时所在的目录，
   Flare 会从每个帧中去掉它。例如 Docker 构建中的 `/src/`，或 GitHub Actions 上的
   `/home/runner/work/shop/shop/`。
6. 点击 **Save**。

启用 `<Deterministic>` 和 `ContinuousIntegrationBuild` 的构建会把路径改写为以 `/_/`
开头，Flare 无需前缀即可去掉它。

## 内联显示出错的代码行

当某个帧链接到你的仓库时，堆栈跟踪下方会出现 **Show source** 按钮，显示抛出位置
（第一个可链接的帧）周围的代码行。

Flare 的 API 会从你的仓库托管平台获取文件，因此私有仓库需要只读访问令牌。在同一个
链接图标表单中填写：

- GitHub：对 **Contents** 有读取权限的细粒度令牌。
- GitLab：具有 `read_repository` 范围的令牌。
- Azure DevOps：具有 **Code (Read)** 权限的个人访问令牌。

令牌只能写入：Flare 不会再次显示它，字段留空则保留已保存的令牌。公共仓库无需令牌。
Flare 不会跟随重定向，会忽略超过 2 MB 的文件，并将文件缓存 10 分钟。

## 帧没有变成链接的情况

Flare 宁可保留纯文本也不猜测：

- 路径是绝对路径，且不以配置的前缀开头；
- 位置只是没有目录的文件名，例如 Java 的帧；
- 该事件没有提交，且服务没有备用分支。

## 另请参阅

- [架构决策：ADR-0095](../../docs-internal/adr/0095-exception-source-links.md)
- [ADR-0096](../../docs-internal/adr/0096-inline-exception-source.md)
