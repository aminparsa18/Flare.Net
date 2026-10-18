# Interroger Flare avec Grafana ou l'API Prometheus

Flare expose un **sous-ensemble en lecture seule de l'API HTTP Prometheus** sur vos métriques OTel. Grafana, `promtool` et `prometheus-adapter` (HPA Kubernetes sur métriques personnalisées) peuvent donc l'utiliser comme source de données Prometheus. Il couvre les sélecteurs avec `offset`, `rate`/`increase`, les agrégations dont `topk` et `quantile`, `histogram_quantile`, les opérateurs entre séries (`a / b`) et un ensemble de fonctions mathématiques. Tout le reste est refusé avec une erreur qui nomme la construction non prise en charge, jamais traité partiellement.

## Connecter Grafana

1. Créez un [jeton d'accès personnel](../admin/configure-authentication.fr.md#jetons-daccès-personnels).
2. Dans Grafana, ajoutez une source de données **Prometheus**.
3. Définissez l'**URL** sur la base de votre API Flare (`http://localhost:8080` dans la pile Docker autonome). Flare sert l'API sur `/api/v1`, là où Grafana l'attend.
4. Sous **Authentication**, ajoutez un en-tête HTTP personnalisé `Authorization` avec la valeur `Bearer flr_pat_...`.
5. Cliquez sur **Save & test**.

## Essayer avec curl

```bash
export FLARE=http://localhost:8080 TOKEN=flr_pat_...

# Requête instantanée
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query=sum by (service_name) (rate(http_server_requests_total[5m]))' \
  $FLARE/api/v1/query

# Requête sur plage
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query=histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket[5m])))' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" --data-urlencode step=60 \
  $FLARE/api/v1/query_range
```

## Noms de métriques et de labels

Flare convertit les noms OTel selon les conventions Prometheus :

| OTel | Prometheus |
|---|---|
| `http.server.request.duration` (histogramme, unité `s`) | `http_server_request_duration_seconds_bucket`, `_sum`, `_count` |
| `http.server.requests` (sum) | `http_server_requests_total` |
| `process.memory` (gauge, unité `By`) | `process_memory_bytes` |
| attribut `http.route` | label `http_route` |
| ressource `service.name` | label `service_name` |

Trouvez les noms exacts avec `GET /api/v1/label/__name__/values`.

## Ce qui est pris en charge

| Construction | Exemple |
|---|---|
| Sélecteurs, avec `=`, `!=`, `=~`, `!~` | `up{service_name="api", code!="200"}` |
| `offset` sur un sélecteur ou une plage | `requests_total offset 1w`, `rate(requests_total[5m] offset 1d)` |
| `rate`, `increase`, `irate` | `rate(requests_total[5m])` |
| `avg_over_time`, `last_over_time` (jauges) | `avg_over_time(queue_depth[10m])` |
| `sum`, `avg`, `min`, `max`, `count`, `group`, `stddev`, `stdvar` avec `by`/`without` | `sum by (route) (rate(requests_total[5m]))` |
| `topk`, `bottomk`, `quantile` avec `by`/`without` | `topk(5, sum by (host) (rate(cpu_seconds_total[5m])))` |
| `histogram_quantile` sur `rate`/`increase`, éventuellement dans `sum by (...)` | `histogram_quantile(0.99, sum by (le, route) (rate(d_bucket[5m])))` |
| `_sum` et `_count` d'un histogramme sous `rate`/`increase` | `rate(d_seconds_count[1m])` |
| Arithmétique `+ - * / % ^` entre séries et nombres | `100 * sum(rate(errors_total[5m])) / sum(rate(requests_total[5m]))` |
| Comparaisons `== != > < >= <=`, avec filtrage, ou `bool` pour 1/0 | `queue_depth > 100`, `up > bool 0` |
| `and`, `or`, `unless` | `rate(a_total[5m]) > 1 and on(job) rate(b_total[5m]) > 1` |
| `on(...)`, `ignoring(...)`, `group_left`, `group_right` | `errors / on(job) group_left(team) owners` |
| `abs`, `ceil`, `floor`, `round`, `sqrt`, `exp`, `ln`, `log2`, `log10`, `sgn`, `clamp`, `clamp_min`, `clamp_max`, `vector` | `clamp_min(free_ratio, 0)` |
| `absent` | `absent(up{job="api"})` |
| `label_replace`, `label_join` | `label_replace(x, "host", "$1", "instance", "(.*):\\d+")` |

Points d'accès : `query`, `query_range`, `query_exemplars`, `series`, `labels`, `label/<name>/values`, `status/buildinfo`.

Non pris en charge : `@`, sous-requêtes, `delta`, `predict_linear`, `min_over_time` et `max_over_time` (Flare stocke une jauge comme une moyenne par bucket, elles seraient fausses), règles d'enregistrement et écriture de données.

## Exemplars

Un point d'histogramme ou de compteur enregistré dans une trace porte un **exemplar** : une valeur échantillon avec la trace et le span qui l'ont produite. Flare conserve ceux qui ont un identifiant de trace et les sert via `query_exemplars`, pour que Grafana les dessine sur un graphique de latence et ouvre la requête lente.

1. Dans la source de données Prometheus, activez **Exemplars** et ajoutez un lien interne : **Label name** `trace_id`, **Data source** une source Tempo pointant vers l'[API compatible Tempo](query-with-tempo-api.fr.md) de Flare.
2. Interrogez un histogramme, par exemple `histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket[5m])))`. Les points apparaissent sur le graphique.

```bash
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query=http_server_request_duration_seconds_bucket{service_name="api"}' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" \
  $FLARE/api/v1/query_exemplars
```

Chaque résultat est une série avec ses `exemplars`, chacun portant `trace_id` (et `span_id`), une valeur et un horodatage. Jusqu'à 1000 sont renvoyés, les plus récents d'abord. Seuls les points ingérés après la mise à jour portent des exemplars. Ils arrivent via OTLP, ou depuis une cible de scrape Prometheus avec `Exemplars` activé (Flare demande alors OpenMetrics à la cible et conserve les exemplars dotés d'un `trace_id` de 32 chiffres hexadécimaux).

La page Metrics de Flare et les panneaux Metrics des tableaux de bord les affichent aussi : chaque exemplar est un anneau qui, au clic, ouvre sa trace sur le span qui l'a enregistré. Sur un graphique d'histogramme, l'anneau se place à la valeur observée (un exemplar au-dessus de l'échelle se place sur le bord supérieur ; survolez-le pour voir sa vraie valeur). Sur un graphique de compteur, de jauge ou de formule, il suit la courbe de la série où il a été enregistré, dans le bucket qui le contient, car un incrément n'a pas de hauteur sur un axe de débit. Le tableau de bord en affiche jusqu'à 100, les plus récents de la fenêtre ; les aires empilées et les superpositions de comparaison n'en affichent aucun.

## Différences avec Prometheus

- **Les erreurs d'appariement sont réelles.** `a / b` échoue avec « found duplicate series for the match group » si un jeu de labels correspond à deux séries d'un côté. Agrégez d'abord, restreignez avec `on(...)`/`ignoring(...)`, ou activez `group_left`/`group_right`, exactement comme dans Prometheus.
- **`offset` est arrondi à un nombre entier de pas.** `offset 90s` avec un pas de 60 s est lu comme 120 s, afin que la série décalée reste sur les mêmes horodatages que le reste de la requête.
- **`irate` et `avg_over_time` travaillent sur des buckets.** `irate` est l'incrément du bucket le plus récent par seconde, et `avg_over_time` fait la moyenne des moyennes de buckets.
- **Les compteurs exigent `rate()` ou `increase()`.** Flare stocke des incréments par intervalle ; un compteur nu comme `requests_total` est donc refusé, avec un renvoi vers `rate()`. Une jauge utilisée sous `rate()` est lue comme un compteur, c'est ainsi qu'arrivent les métriques Prometheus `*_total` sans type.
- **Les horodatages sont des débuts de bucket.** Les échantillons tombent sur des multiples du pas, pas sur `start + k*step`. Une fenêtre `[5m]` glisse sur `round(5m / pas)` buckets, au moins un.
- **200 séries par sélecteur.** Un sélecteur correspondant à plus de séries renvoie les 200 plus grosses et ajoute une entrée `warnings`. Ajoutez des matchers de label pour restreindre. Les matchers `!=` et regex s'appliquent après cette limite.
- **Les requêtes instantanées** regardent les cinq dernières minutes.
- **`labels` et les valeurs de label sans `match[]`** échantillonnent les dix métriques ayant le plus de séries. Passez `match[]` pour une réponse complète.

Notes de conception : [ADR-0109](../../../docs-internal/adr/0109-prometheus-query-api.md), [ADR-0239](../../../docs-internal/adr/0239-prometheus-api-binary-operators-offset-topk-functions.md), [ADR-0241](../../../docs-internal/adr/0241-metric-exemplars.md).
