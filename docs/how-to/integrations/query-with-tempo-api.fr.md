# Lire les traces de Flare depuis Grafana avec l'API Tempo

Flare expose un **sous-ensemble en lecture seule de l'API HTTP de Grafana Tempo** sur vos traces OTel. La source de données **Tempo** de Grafana peut ainsi retrouver une trace par identifiant, rechercher des traces, alimenter les sélecteurs de tags et de valeurs de son constructeur de requêtes et tracer des métriques TraceQL (débits et percentiles de latence) en séries temporelles. La recherche accepte une petite partie de TraceQL. Tout le reste est refusé avec une erreur qui nomme la construction non prise en charge, jamais traité partiellement.

## Connecter Grafana

1. Créez un [jeton d'accès personnel](../admin/configure-authentication.fr.md#jetons-daccès-personnels).
2. Dans Grafana, ajoutez une source de données **Tempo**.
3. Définissez l'**URL** sur la base de votre API Flare suivie de `/tempo` : `http://localhost:8080/tempo` dans la pile Docker autonome. Flare sert l'API sur `/tempo/api`, car `/api/traces` est déjà une route propre à Flare.
4. Sous **Authentication**, ajoutez un en-tête HTTP personnalisé `Authorization` avec la valeur `Bearer flr_pat_...`.
5. Cliquez sur **Save & test**.

## Essayer avec curl

```bash
export FLARE=http://localhost:8080 TOKEN=flr_pat_...

# Une trace par identifiant (OTLP JSON ; envoyez "Accept: application/protobuf" pour le protobuf qu'utilise Grafana)
curl -H "Authorization: Bearer $TOKEN" $FLARE/tempo/api/traces/0af7651916cd43dd8448eb211c80319c

# Recherche TraceQL : traces où le service payment a un span en erreur de plus de 500 ms
curl -H "Authorization: Bearer $TOKEN" -G $FLARE/tempo/api/search \
  --data-urlencode 'q={ resource.service.name = "payment" && status = error && duration > 500ms }' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" --data-urlencode limit=20

# Métriques TraceQL : le débit de requêtes de chaque service, un échantillon par minute
curl -H "Authorization: Bearer $TOKEN" -G $FLARE/tempo/api/metrics/query_range \
  --data-urlencode 'q={ kind = server } | rate() by (resource.service.name)' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" --data-urlencode step=60s

# La même idée avec l'ancienne syntaxe de tags
curl -H "Authorization: Bearer $TOKEN" -G $FLARE/tempo/api/search \
  --data-urlencode 'tags=service.name=payment http.status_code=500' --data-urlencode minDuration=500ms
```

## Relier un span à ses métriques

Le paramètre **Trace to metrics** de la source de données Tempo ajoute à un span un bouton qui ouvre une requête Prometheus pour le service et la route de ce span. Pointez-le vers une source de données Prometheus qui [lit les métriques de Flare](query-with-prometheus-api.fr.md) : un span lent dans une trace est alors à un clic du débit de requêtes, du taux d'erreurs et de la latence du service où il s'est exécuté. Les noms de labels des métriques de Flare diffèrent des noms d'attributs OTel, d'où l'importance de la correspondance ci-dessous.

Dans les paramètres de la source de données Tempo, sous **Trace to metrics** :

1. **Data source** : la source de données Prometheus qui pointe vers Flare.
2. **Span start time shift** `-5m`, **Span end time shift** `5m`. Les requêtes ci-dessous regardent en arrière sur `[5m]` ; une plage qui commencerait au span laisserait vides les premières minutes.
3. **Tags** : associez chaque attribut de span au label Flare placé à sa droite.

   | Attribut de span | Label |
   |---|---|
   | `service.name` | `service_name` |
   | `http.route` | `http_route` |
   | `http.request.method` | `http_request_method` |

   Renseignez toujours le label. Les labels de Flare sont les noms d'attributs avec `_` à la place de `.` ; laissé vide, Grafana reprend le nom de l'attribut, et Flare refuse `service.name="checkout"` comme sélecteur de label.
4. **Queries** : `$__tags` se développe en les tags associés que le span possède.

   | Nom | Requête |
   |---|---|
   | Request rate | `sum(rate(http_server_request_duration_seconds_count{$__tags}[5m]))` |
   | Error rate | `sum(rate(http_server_request_duration_seconds_count{$__tags, http_response_status_code=~"5.."}[5m]))` |
   | p95 latency | `histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket{$__tags}[5m])))` |

Les mêmes paramètres sous forme de source de données provisionnée (`$$` empêche Grafana de développer `$__tags` comme variable d'environnement) :

```yaml
apiVersion: 1
datasources:
  - name: Flare Traces
    type: tempo
    url: http://localhost:8080/tempo
    jsonData:
      tracesToMetrics:
        datasourceUid: flare-prometheus   # l'uid de votre source de données Prometheus Flare
        spanStartTimeShift: "-5m"
        spanEndTimeShift: "5m"
        tags:
          - { key: service.name, value: service_name }
          - { key: http.route, value: http_route }
          - { key: http.request.method, value: http_request_method }
        queries:
          - name: Request rate
            query: 'sum(rate(http_server_request_duration_seconds_count{$$__tags}[5m]))'
```

À savoir :

- **Le service doit envoyer ses métriques à Flare sous le même `service.name`.** Les requêtes lisent l'histogramme `http.server.request.duration`, que ASP.NET Core et les autres instrumentations HTTP serveur OpenTelemetry enregistrent. Pour une autre métrique, trouvez son nom Prometheus avec `GET /api/v1/label/__name__/values`.
- **Grafana omet un tag que le span n'a pas.** Un span enfant de base de données a `service.name` mais pas `http.route` : son lien est la requête à l'échelle du service ; le lien d'un span serveur est restreint à sa route.
- **Un tag que la métrique n'a pas ne correspond à rien.** Si le `http.server.request.duration` de l'application n'a pas de label `http.route` (vérifiez avec `GET /api/v1/labels?match[]=http_server_request_duration_seconds_count`), supprimez ce tag.
- **Trace to logs n'est pas disponible.** Il faut pour cela une API de logs compatible Loki, que Flare ne sert pas encore.

## Ce qui est pris en charge

| Point d'accès | Remarques |
|---|---|
| `GET /tempo/api/echo` | Test de connexion de Grafana |
| `GET /tempo/api/traces/{id}`, `GET /tempo/api/v2/traces/{id}` | JSON ou protobuf selon `Accept`. Les identifiants courts sont complétés par des zéros à gauche. La v2 signale `PARTIAL` pour une trace que Flare a tronquée |
| `GET /tempo/api/search` | `q` (TraceQL) ou `tags`, plus `minDuration`, `maxDuration`, `limit` (20 par défaut, 200 au plus), `spss` (spans correspondants listés par trace, 3 par défaut, 100 au plus), `start`, `end` (secondes unix ; la dernière heure par défaut) |
| `GET /tempo/api/search/tags`, `GET /tempo/api/v2/search/tags` | Noms d'attributs vus dans la fenêtre ; la v2 les regroupe en `span`, `resource` et `intrinsic` ; `scope` restreint |
| `GET /tempo/api/search/tag/{tag}/values`, `GET /tempo/api/v2/search/tag/{tag}/values` | Valeurs vues dans la fenêtre |
| `GET /tempo/api/metrics/query_range` | `q` (une [requête de métriques TraceQL](#métriques-traceql)), `start` et `end` (secondes ou nanosecondes unix, ou RFC3339) ou `since` (une durée ; la dernière heure par défaut), `step` (une durée ou des secondes entières ; par défaut la plage en 200 pas, 1 500 pas au plus). Une série par groupe |
| `GET /tempo/api/metrics/query` | Pareil sans `step` : une valeur par groupe sur toute la plage |

Une recherche renvoie une ligne par trace : le service et le nom du span racine, son début et sa durée. Quand la requête porte sur des spans, chaque ligne liste aussi jusqu'à `spss` spans
correspondants (`spanSets`), avec les attributs sur lesquels la requête a comparé, que Grafana affiche en lignes imbriquées de son tableau de résultats TraceQL.

### Le sous-ensemble TraceQL

| Construction | Exemple |
|---|---|
| Attributs de span, avec portée | `{ span.http.method = "GET" && resource.k8s.namespace.name = "prod" }` |
| Opérateurs `=`, `!=`, `=~`, `!~`, `>`, `>=`, `<`, `<=` | `{ span.http.status_code >= 500 }` |
| Champs intrinsèques du span : `name` (`=`, `!=`, `=~`, `!~`), `status` (`=`, `!=` : `error`, `ok`, `unset`), `kind` (`=`, `!=` : `unspecified`, `internal`, `server`, `client`, `producer`, `consumer`), `duration` | `{ kind = server && name =~ "POST .*" && status != ok }` |
| Champs intrinsèques de la trace, dans une requête à un seul spanset : `rootName`, `rootServiceName`, `traceDuration` | `{ rootServiceName = "gateway" && traceDuration > 1s }` |
| Combinaison de spansets : `&&`, `\|\|`, enfant `>`, descendant `>>` | `{ resource.service.name = "checkout" } >> { status = error }` |
| Attributs sans portée | `{ .service.name = "api" }` |

Non pris en charge dans une recherche : les pipelines `|` (`count()`, `avg()`, `select()`, `by()`, ... ; une requête de métriques en accepte un, ci-dessous), `||` à l'intérieur d'un même spanset, `statusMessage` et la comparaison de deux champs.

### Métriques TraceQL

`/tempo/api/metrics` prend un spanset et une fonction : `{ status = error } | rate() by (resource.service.name)`. Le spanset est tout ce qu'une recherche accepte pour un seul span, ou deux spansets reliés par `>` ou `>>` (voir plus bas), mais pas des spansets combinés avec `&&` ou `||`. Dans Grafana, utilisez le type de requête **TraceQL** avec une requête qui se termine par une fonction : le résultat est une série temporelle ; **Type**, dans les options Metrics de la requête, choisit une requête sur plage ou instantanée (une valeur).

| Fonction | Valeur par pas |
|---|---|
| `rate()` | Spans par seconde |
| `count_over_time()` | Spans |
| `quantile_over_time(duration, .5, .95)` | Quantiles de la durée en secondes, une série par quantile, étiquetée `p` |
| `avg_over_time(duration)`, `min_over_time(duration)`, `max_over_time(duration)`, `sum_over_time(duration)` | La durée, en secondes |
| `histogram_over_time(duration)` | Spans par pas dans chaque seau de la durée en puissance de deux, une série par seau, étiquetée `__bucket` avec la borne supérieure du seau en secondes. Grafana l'affiche en carte de chaleur |

`by (...)` accepte jusqu'à cinq champs parmi `name`, `status`, `kind`, `span.x`, `resource.x` et `.x` et produit une série par combinaison : `{ kind = server } | quantile_over_time(duration, .95) by (resource.service.name, span.http.route)`.

`quantile_over_time`, `avg_over_time`, `min_over_time`, `max_over_time`, `sum_over_time` et `histogram_over_time` acceptent aussi un attribut numérique à la place de `duration` : `{ kind = server } | avg_over_time(span.http.response.body.size)`, avec `span.x`, `resource.x` ou `.x`. Les valeurs sont rapportées telles quelles, pas en secondes. Un span dont la valeur est absente ou n'est pas un nombre est écarté.

Après la fonction, les étapes s'exécutent de gauche à droite. `| topk(5)` ou `| bottomk(5)` garde, à chaque pas, les cinq séries à la valeur la plus grande (la plus petite) : `{ kind = server } | rate() by (span.http.route) | topk(5)`. Une comparaison (`=`, `!=`, `<`, `<=`, `>`, `>=` et un nombre simple) écarte les échantillons qui ne la satisfont pas, ainsi que la série qui n'en garde aucun : `| rate() by (name) > 5`. Le nombre est dans l'unité de la fonction, donc en secondes pour une durée. L'ordre compte : `| rate() > 0 | topk(5)` ne classe que les échantillons non nuls, `| topk(5) > 10` écarte les gagnants inférieurs à 10.

**Relations.** `{ a } >> { b } | rate()` compte les spans `b` qui ont un span `a` au-dessus d'eux, `{ a } > { b }` ceux dont le parent est un span `a`. `{ resource.service.name = "web" } >> { name = "db.query" } | quantile_over_time(duration, .95)` est la latence des requêtes de base de données issues d'un span `web`.

**Champs de trace.** `rootName`, `rootServiceName` et `traceDuration` limitent la requête aux spans des traces dont le span racine correspond : `{ rootName = "GET /checkout" && name = "db.query" } | rate()`. Ils ne se combinent pas avec une relation.

Les **exemplars** relient un point d'un graphique à une trace. Activez **Exemplars** dans les options Metrics (Grafana envoie `exemplars=100`) ou ajoutez `with(exemplars=true)` à la requête. Chaque pas de chaque série porte alors son span le plus lent (ou celui dont la valeur d'attribut est la plus grande) avec un identifiant de trace, et Grafana l'affiche comme un point qu'on peut ouvrir. Requêtes sur plage uniquement.

Non pris en charge : `compare()`, et `&&` ou `||` entre spansets dans une requête de métriques.

## Différences avec Tempo

- **Les expressions régulières sont ancrées**, comme dans Tempo : `=~ "api-.*"` doit correspondre à toute la valeur.
- **La portée d'un attribut sans préfixe est devinée.** Dans Tempo, `.foo` signifie « span ou ressource ». Flare le lit dans la ressource quand le nom appartient à un espace de noms de ressource OpenTelemetry (`service.`, `k8s.`, `host.`, `cloud.`, `deployment.`, `process.`, `container.`, `os.`, ...) et dans le span sinon. Écrivez `span.foo` ou `resource.foo` pour être explicite.
- **`!=` correspond aussi aux spans sans l'attribut.** En TraceQL, l'attribut doit exister.
- **La recherche a besoin du span racine.** Une trace dont la racine n'a jamais été ingérée n'apparaît pas dans la liste, mais s'ouvre par identifiant.
- **Les spans correspondants listent les attributs comparés par la requête.** Chaque span listé a son identifiant, son nom, son début, sa durée, `service.name` et les attributs que la requête nomme (20 au plus), sous forme de chaînes. Un attribut aux orthographes ancienne et nouvelle (`http.status_code`, `http.response.status_code`) liste celle que le span possède. Dans une requête de plusieurs spansets (`{ a } >> { b }`), les spans de tous les spansets sont listés ensemble dans un seul groupe, pas un groupe par spanset.
- **Les métriques sont regroupées par pas.** Un échantillon porte l'horodatage du début de son pas, et `start` et `end` sont arrondis vers l'extérieur à un multiple du pas ; quand la plage se termine maintenant, le dernier pas est donc partiel. `rate()` et `count_over_time()` valent zéro dans un pas sans span, comme `histogram_over_time()` ; les autres fonctions n'y ont pas d'échantillon.
- **Les métriques ne groupent que les spans qui ont l'attribut.** Dans `by (span.http.route)`, un span sans `http.route` est écarté, pas compté sous une valeur vide. Un attribut aux orthographes ancienne et nouvelle (`http.status_code`, `http.response.status_code`) groupe selon celle que le span possède.
- **Un `histogram_over_time()` garde 20 groupes au plus,** chacun avec jusqu'à 63 séries de seaux, et une valeur inférieure à 2 (une durée de moins de 2 ns) n'est comptée dans aucun seau, comme dans Tempo.
- **`topk()` classe les séries calculées par Flare.** Comme la limite de 200 séries ci-dessous, il travaille sur les groupes qui ont le plus de spans ; le groupe à la plus grande moyenne pourrait donc avoir été écarté (la réponse indique `PARTIAL`).
- **Les relations et les champs de trace remontent d'une heure.** L'ancêtre d'un span, et la racine d'une trace, sont cherchés à partir d'une heure avant la plage ; un span dont l'ancêtre a commencé plus tôt n'est donc pas compté. Les deux lisent les spans des traces candidates, ce qui coûte plus qu'un spanset simple.
- **Les exemplars concernent les requêtes sur plage.** La réponse instantanée de Tempo n'a pas non plus de place pour eux.
- **Un exemplar est le span le plus lent,** pas un span tiré au hasard comme dans Tempo, et il est sur la série de son groupe (le plus grand quantile pour `quantile_over_time`, le seau où tombe sa valeur pour un histogramme).
- **Une requête de métriques renvoie 200 séries au plus.** Avec plus de groupes, elle garde les 200 qui ont le plus de spans et marque la réponse `PARTIAL`. Les comptes et quantiles pondèrent chaque span stocké par ce qu'il représente avec l'[échantillonnage des traces](../../explanation/sampling-clustering-and-cost.fr.md), si bien qu'un service échantillonné rapporte toujours son vrai débit.
- **Les listes de tags sont un échantillon.** Les noms viennent d'au plus 50 000 spans de la fenêtre, les plus fréquents d'abord. Réduisez `start` et `end` pour une liste complète.

Notes de conception : [ADR-0217](../../../docs-internal/adr/0217-tempo-compatible-trace-api.md), [ADR-0222](../../../docs-internal/adr/0222-traceql-metrics-over-an-attribute.md), [ADR-0223](../../../docs-internal/adr/0223-traceql-histogram-and-exemplars.md), [ADR-0224](../../../docs-internal/adr/0224-traceql-metrics-topk-bottomk.md) et [ADR-0225](../../../docs-internal/adr/0225-traceql-metrics-comparison-and-span-relations.md).
