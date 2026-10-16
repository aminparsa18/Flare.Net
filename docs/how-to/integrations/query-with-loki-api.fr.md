# Lire les logs de Flare depuis Grafana avec l'API Loki

Flare expose un **sous-ensemble en lecture seule de l'API HTTP Loki** sur vos logs. La source de données Loki de Grafana peut donc exécuter du LogQL sur Flare : Explore, panneaux de logs, volume de logs, sélecteurs de labels et liens trace vers logs. Il couvre les sélecteurs de flux, les filtres de ligne, `| json`, les filtres de labels et les fonctions qui transforment des logs en métriques (`rate`, `count_over_time`, `bytes_rate`, `bytes_over_time`) avec `sum`, `topk` et l'arithmétique. Tout le reste est refusé avec une erreur qui nomme la construction non prise en charge, jamais traité partiellement.

## Connecter Grafana

1. Créez un [jeton d'accès personnel](../admin/configure-authentication.fr.md#jetons-daccès-personnels).
2. Dans Grafana, ajoutez une source de données **Loki**.
3. Définissez l'**URL** sur la base de votre API Flare (`http://localhost:8080` dans la pile Docker autonome). Flare sert l'API sur `/loki/api/v1`, là où Grafana l'attend.
4. Sous **Authentication**, ajoutez un en-tête HTTP personnalisé `Authorization` avec la valeur `Bearer flr_pat_...`.
5. Cliquez sur **Save & test**.

## Relier une trace à ses logs

Une fois la [source de données Tempo](query-with-tempo-api.fr.md) en place, ouvrez ses paramètres **Trace to logs**, choisissez la source Loki et associez le tag `service.name` au label `service_name`. Activez **Use custom query** et utilisez

```
{${__tags}} | trace_id="${__trace.traceId}"
```

Flare conserve les identifiants de trace et de span de chaque entrée sous les noms `trace_id` et `span_id`, la correspondance est donc exacte. L'option par défaut **Filter by trace ID** cherche l'identifiant dans le texte du log (`|= "<id>"`), ce qui ne trouve que les logs qui l'affichent.

## Essayer avec curl

```bash
export FLARE=http://localhost:8080 TOKEN=flr_pat_...

# Lignes de log : erreurs d'un service qui mentionnent un timeout
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query={service_name="shop-api"} |= "timeout" | detected_level="error"' \
  --data-urlencode limit=20 \
  $FLARE/loki/api/v1/query_range

# Volume de logs par niveau, un point par minute
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query=sum by (detected_level) (count_over_time({service_name="shop-api"}[1m]))' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" --data-urlencode step=60 \
  $FLARE/loki/api/v1/query_range
```

`start` et `end` acceptent des nanosecondes Unix (ce qu'envoie Grafana), des secondes ou du RFC 3339. Par défaut, c'est la dernière heure.

## Labels

Flare associe ses champs aux labels Loki comme le fait l'ingestion OTLP de Loki :

| Flare | Label Loki |
|---|---|
| service | `service_name` |
| sévérité (regroupée en trace, debug, info, warn, error, fatal, unknown) | `detected_level` (aussi `level` avant une étape `\| json`) |
| texte de sévérité | `severity_text` |
| portée d'instrumentation | `scope_name` |
| identifiant de trace, de span | `trace_id`, `span_id` |
| attribut de ressource `k8s.pod.name` | `k8s_pod_name` (les points deviennent des tirets bas) |
| attribut de log `http.method` | `http_method` |

Dans une réponse, un **flux** est `service_name` plus les attributs de ressource que Loki indexe par défaut (`k8s.namespace.name`, `k8s.pod.name`, `deployment.environment` et le reste de la liste de Loki). Tout le reste est de la **métadonnée structurée** attachée à chaque entrée, que Grafana affiche dans le détail du log sans fragmenter le flux.

Contrairement à Loki, n'importe quel label peut figurer dans le sélecteur de flux, y compris les attributs de log et les identifiants ci-dessus. `GET /loki/api/v1/labels` liste les attributs de ressource vus dans la fenêtre.

## Ce qui est pris en charge

| Construction | Exemple |
|---|---|
| Sélecteur de flux avec `=`, `!=`, `=~`, `!~` | `{service_name="api", k8s_namespace_name=~"prod-.*"}` |
| Filtres de ligne : `\|=`, `!=`, `\|~`, `!~`, avec `or` | `{service_name="api"} \|= "timeout" or "refused" != "health"` |
| `\| json`, seul ou avec des expressions | `\| json uid="user.id"` |
| Filtres de labels : `=`, `!=`, `=~`, `!~`, `>`, `>=`, `<`, `<=`, reliés par `and`, `,`, `or` et des parenthèses | `\| json \| status >= 500 and method="POST"` |
| `\| drop __error__` | accepté, sans effet |
| `count_over_time`, `rate`, `bytes_over_time`, `bytes_rate` | `rate({service_name="api"} \|= "error" [5m])` |
| `sum`, `avg`, `min`, `max`, `count` avec `by`/`without` | `sum by (service_name) (rate({service_name=~".+"}[5m]))` |
| `topk`, `bottomk` | `topk(5, sum by (service_name) (rate({service_name=~".+"}[5m])))` |
| `+ - * / %` entre expressions et nombres, `vector(n)` | `sum(rate({...} \|= "error" [5m])) / sum(rate({...}[5m]))` |

Points d'accès : `query_range`, `query` (requêtes de métriques), `labels`, `label/<name>/values`, `series`, `index/stats`, `status/buildinfo`. GET et POST encodé en formulaire fonctionnent tous deux.

Non pris en charge : `logfmt`, `line_format`, `label_format`, `pattern`, `regexp`, `unpack`, `unwrap` et les fonctions `*_over_time` qui en ont besoin, `offset`, les opérateurs de comparaison et d'ensemble (`>`, `and`, `or`, `unless`) entre métriques, la mise en correspondance `on`/`ignoring`, le live tail (`/tail`), et les points `detected_fields` et `patterns` de Logs Drilldown.

## Différences avec Loki

- **`| json` aplatit les clés imbriquées avec `_`**, comme Loki (`{"request":{"method":"GET"}}` devient `request_method`). Un filtre de label sur un nom aplati essaie toutes les façons de le découper en clés, donc `request_user_id` trouve `request.user.id` comme une clé littéralement nommée `request_user_id`. Dans les expressions `| json label="path"`, le chemin est séparé par des points ; les indices de tableau ne sont pas pris en charge.
- **Après `| json`, un champ nommé `level` l'emporte** sur le niveau dérivé de la sévérité, et on retombe sur ce dernier quand la ligne n'a pas ce champ.
- **Les regex de labels sont ancrées** (`=~"web"` ne correspond qu'à `web`), comme dans Loki ; **les regex de ligne ne le sont pas** (`|~"5\d\d"` correspond n'importe où dans la ligne). `|=` est sensible à la casse.
- **Les fenêtres des métriques sont fondées sur des seaux.** Une agrégation sur plage lit des seaux dont la largeur est le plus grand diviseur commun de la plage et du pas (le pas si cela donnerait trop de seaux), alignés sur les pas de la requête. Un pas sans ligne correspondante n'a pas d'échantillon.
- **Limites.** Une requête de logs renvoie au plus 5000 lignes (`limit` vaut 100 par défaut) ; une requête de métriques échoue plutôt que de tronquer au-delà de 500 séries ou 11 000 points par série.
- **Les expressions régulières invalides** sont signalées à l'exécution de la requête sur des données, en syntaxe RE2.
- **Les requêtes instantanées** fonctionnent pour les métriques uniquement ; un sélecteur de logs demande `query_range`.

Notes de conception : [ADR-0236](../../../docs-internal/adr/0236-loki-compatible-log-api.md).
