# Comment trouver les problèmes de santé du runtime .NET

Flare lit les métriques de runtime `dotnet.*` que votre service envoie déjà et signale les problèmes sous forme de constats, afin que vous n'ayez pas à comparer plusieurs graphiques. Aucune instrumentation supplémentaire n'est nécessaire en dehors des métriques de runtime elles-mêmes.

## Envoyer les métriques de runtime

Avec .NET 9 ou une version ultérieure, le runtime émet lui-même ces métriques via le compteur `System.Runtime`. Ajoutez ce compteur à votre configuration OpenTelemetry et exportez-le vers Flare comme vos autres métriques :

```csharp
builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter("System.Runtime"));
```

## Voir les constats

1. Ouvrez **Traces**, puis l'onglet **Services**.
2. Ouvrez un service (cliquez sur son nœud dans la vue **Map**).
3. La section **.NET runtime health** se trouve en haut. Elle remonte sur la fenêtre sélectionnée, et jamais moins de 30 minutes.

Chaque constat indique sa gravité, la période couverte, l'instance concernée, ainsi que des liens vers les **Traces** et les **Logs** de cette fenêtre exacte. **En cours** signifie que le constat atteint la fin de la fenêtre. Un constat est **Critique** lorsqu'il est en cours et bien au-delà de son seuil.

Si la section indique que le service n'a envoyé aucune métrique de runtime, il s'agit de données manquantes, pas d'un bilan de santé satisfaisant.

## Ce qui est détecté

La détection s'effectue par instance (`service.instance.id`, sinon le nom du pod, sinon `host.name`), de sorte qu'un réplica défaillant n'est pas masqué par les réplicas sains. Les seuils sont fixes.

| Constat | Métriques | Signalé lorsque |
| --- | --- | --- |
| Famine du pool de threads | `dotnet.thread_pool.queue.length`, `dotnet.thread_pool.work_item.count` | 10 éléments ou plus en file alors que les éléments de travail terminés par seconde sont au plus la moitié de leur débit habituel, pendant 3 intervalles consécutifs, et que la file ne se résorbe pas |
| Pression du GC | `dotnet.gc.pause.time` | 10 % ou plus du temps réel passé en pause de ramasse-miettes, pendant 2 intervalles consécutifs |
| Pic de contention de verrous | `dotnet.monitor.lock_contentions` | Au moins 5 par seconde et 5 fois le débit habituel, pendant 2 intervalles consécutifs |
| Pic d'exceptions | `dotnet.exceptions` | Au moins 2 par seconde et 3 fois le débit habituel, pendant 2 intervalles consécutifs |

Un intervalle représente environ un soixantième de la fenêtre et jamais moins d'une minute. Le débit habituel est le premier quartile des débits par intervalle de la fenêtre, et les règles de débit exigent au moins 6 intervalles de données. Une interruption des données met fin à un constat.

Les constats sont calculés à partir de vos métriques à l'ouverture de la vue détaillée. L'API correspondante est `POST /api/services/runtime-health`.
