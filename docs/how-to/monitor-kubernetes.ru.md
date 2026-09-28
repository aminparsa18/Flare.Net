# Как отслеживать узлы и поды Kubernetes

Отправляйте метрики узлов и подов вашего кластера во Flare с помощью
приёмников `kubeletstats` и `k8s_cluster` из OpenTelemetry Collector и
просматривайте их на странице **Kubernetes**: таблица **Nodes** и таблица
**Pods**, у каждой есть детальные графики.

Страница **Kubernetes** отличается от **Resources**. Resources опрашивает API
Kubernetes и показывает только собственные поды Flare. Страница Kubernetes
показывает весь ваш кластер так, как его передаёт коллектор по OTLP.

## Предварительные требования

- Запущенный экземпляр Flare, порт OTLP которого (`4317` gRPC или `4318` HTTP)
  доступен из кластера.
- Дистрибутив [OpenTelemetry Collector Contrib](https://github.com/open-telemetry/opentelemetry-collector-contrib)
  (`otelcol-contrib`), развёрнутый в кластере. Проще всего развернуть его через
  [Helm-чарт OpenTelemetry](https://github.com/open-telemetry/opentelemetry-helm-charts).

## Настройка коллектора

Два приёмника передают разные данные, и каждый заполняет свои столбцы:

| Приёмник | Запускается как | Передаёт |
|---|---|---|
| `kubeletstats` | DaemonSet (по коллектору на узел) | Использование CPU и памяти узлами и подами |
| `k8s_cluster` | Deployment с одной репликой | Готовность узлов, выделяемый CPU, фазу подов, перезапуски контейнеров |

В Helm-чарте пресет `kubeletMetrics` добавляет `kubeletstats` в коллектор-
DaemonSet, а пресет `clusterMetrics` добавляет `k8s_cluster` в коллектор-
Deployment. Можно запустить только один из них; столбцы, которые заполняет
другой, покажут **—**.

### Коллектор-DaemonSet (`kubeletstats`)

```yaml
receivers:
  kubeletstats:
    auth_type: serviceAccount
    endpoint: "https://${env:K8S_NODE_NAME}:10250"
    insecure_skip_verify: true
    collection_interval: 60s

processors:
  k8sattributes: {}

exporters:
  otlp:
    endpoint: flare.example.internal:4317   # ваш хост Flare.Ingest
    tls:
      insecure: true                        # или настройте TLS

service:
  pipelines:
    metrics:
      receivers: [kubeletstats]
      processors: [k8sattributes]
      exporters: [otlp]
```

Рекомендуется процессор `k8sattributes`. Он добавляет к каждому поду имя узла и
владеющую рабочую нагрузку (Deployment, StatefulSet, DaemonSet, Job или
CronJob), которые таблица Pods показывает в столбцах **Node** и **Workload**.

### Коллектор-Deployment (`k8s_cluster`)

```yaml
receivers:
  k8s_cluster:
    collection_interval: 60s
    allocatable_types_to_report: [cpu]

service:
  pipelines:
    metrics:
      receivers: [k8s_cluster]
      exporters: [otlp]
```

`allocatable_types_to_report: [cpu]` необязателен. Без него столбец
**CPU % alloc.** остаётся пустым.

Чтобы различать кластеры, задайте атрибут ресурса `k8s.cluster.name` на обоих
коллекторах, например процессором `resource`. Тогда в таблице Nodes появится
фильтр по кластеру.

Если вы включили [ключи API приёма данных](configure-authentication.ru.md#ключи-api-приёма-данных),
добавьте ключ в `headers` экспортёра.

## Чтение таблицы Nodes

Откройте **Kubernetes** в верхнем меню. Узлы появляются в течение одного
интервала сбора.

| Столбец | Исходная метрика | Значение |
|---|---|---|
| Status | `k8s.node.condition_ready` | Последнее переданное состояние готовности |
| CPU | `k8s.node.cpu.usage` | Используемый CPU, среднее за окно, в миллиядрах (`250m`) или ядрах |
| CPU % alloc. | `k8s.node.allocatable_cpu` | CPU как доля выделяемого CPU узла |
| Memory | `k8s.node.memory.working_set` | Рабочий набор памяти, среднее за окно |
| Memory % | `k8s.node.memory.available` | Рабочий набор как доля рабочего набора плюс доступной памяти |
| Pods | любая метрика `k8s.pod.*` | Число разных подов, передававших данные с этого узла за окно |
| Last seen | любая метрика `k8s.node.*` | Когда узел передавал данные в последний раз |

Flare также читает `k8s.node.cpu.utilization` — прежнее имя той же метрики CPU,
которое отправляют старые коллекторы.

- **Детализация**: щёлкните имя узла. Откроются графики CPU, CPU %, памяти и
  памяти % за выбранное окно.
- **Поды узла**: щёлкните число в столбце **Pods** или кнопку **View pods on
  this node** в детализации.

## Чтение таблицы Pods

Переключитесь на вкладку **Pods** или откройте `/kubernetes?tab=pods`.

| Столбец | Исходная метрика | Значение |
|---|---|---|
| Workload | `k8s.deployment.name` и похожие атрибуты ресурса | Владеющий Deployment, StatefulSet, DaemonSet, CronJob, Job или ReplicaSet |
| Node | `k8s.node.name` | Узел, с которого под передавал данные в последний раз |
| Status | `k8s.pod.phase` | Последняя фаза: Pending, Running, Succeeded, Failed или Unknown |
| Restarts | `k8s.container.restarts` | Последнее число перезапусков, суммированное по контейнерам пода |
| CPU | `k8s.pod.cpu.usage` | Используемый CPU, среднее за окно, плюс доля от лимита пода, если она передаётся |
| Memory | `k8s.pod.memory.working_set` | Рабочий набор памяти, среднее за окно, плюс доля от лимита пода, если она передаётся |

Доля от лимита берётся из необязательных метрик приёмника `kubeletstats`
`k8s.pod.cpu_limit_utilization` и `k8s.pod.memory_limit_utilization`. Как их
включить, см. в разделе [Как отслеживать хосты](monitor-hosts.ru.md#метрики-хоста-рядом-с-записью-журнала).

Фильтруйте поды по имени (подстрока, без учёта регистра), пространству имён или
узлу. Щёлкните имя пода, чтобы увидеть его графики CPU и памяти.

## Ограничения и устаревание

**—** означает, что Flare не получил данных по этой метрике за окно. Это
никогда не отображается как 0.

Узел или под, не передававший данные больше пяти минут, помечается как
**stale**. Удалённый под остаётся в списке с этой пометкой, пока не выйдет за
пределы выбранного окна.

Таблица Nodes показывает до 500 узлов, таблица Pods — до 1 000 подов. Если
совпадений больше, появится предложение сузить фильтр.

## Устранение неполадок

**Не появляются ни узлы, ни поды.** Проверьте журналы коллектора на ошибки
экспорта. Flare определяет узлы по атрибуту ресурса `k8s.node.name`, а поды —
по `k8s.pod.name` и `k8s.namespace.name`; оба приёмника задают их по умолчанию.

**У некоторых подов пуст столбец Node или Workload.** `kubeletstats` сам не
задаёт узел и владельца пода. Добавьте процессор `k8sattributes` или запустите
приёмник `k8s_cluster`, который передаёт узел каждого пода.

**Пусты Status, Restarts и CPU % alloc.** Эти значения поступают от приёмника
`k8s_cluster`. Разверните его, как описано выше.
