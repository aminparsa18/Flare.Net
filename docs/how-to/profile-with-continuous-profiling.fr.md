# Comment explorer les profils continus

Les profils montrent dans quelles fonctions votre code passe son temps CPU ou sa mémoire. Flare accepte le signal **profiles** d'OpenTelemetry sur les mêmes ports OTLP que les logs, les traces et les métriques, stocke chaque échantillon avec sa pile d'appels, et affiche le résultat fusionné sous forme de flame graph. Les échantillons pris pendant un span tracé portent les identifiants de ce span : vous pouvez donc passer d'un span lent au code qui s'exécutait.

OTLP profiles est en **Alpha** dans OpenTelemetry. Le format de transmission peut encore changer entre deux versions, donc revérifiez votre émetteur après une mise à jour.

## Envoyer des profils à Flare

Pointez un émetteur de profils OTLP vers les points d'accès que vous utilisez déjà :

| Transport | Adresse |
| --- | --- |
| gRPC | `localhost:4317` (`ProfilesService/Export`) |
| HTTP | `POST http://localhost:4318/v1development/profiles` (protobuf ou JSON) |

Les profils passent par la même authentification par clé d'ingestion, la même limite de taille de requête et les mêmes limites par clé que les autres signaux. Les pages **Ingestion** et **Pipeline** listent Profiles à côté de Logs, Traces et Metrics.

OTLP profiles est récent, donc peu d'émetteurs existent pour l'instant. L'OpenTelemetry Collector peut retransmettre les profils qu'il reçoit : démarrez-le avec `--feature-gates=service.profilesSupport` et définissez `profiles_endpoint: http://localhost:4318/v1development/profiles` sur son exportateur `otlp_http`. Si vous avez activé les [clés d'API d'ingestion](configure-authentication.fr.md#clés-api-dingestion), ajoutez la clé aux `headers` de l'exportateur. Les corps de requête compressés (gzip) sont acceptés.

Le récepteur `pprof` du Collector (v0.162, Alpha) lit des fichiers pprof et des points `/debug/pprof` Go, mais il émet pour l'instant des échantillons sans pile d'appels : un flame graph construit à partir de lui n'a que la racine. Flare vous le signale.

### Essayer avec curl

Cette requête envoie un profil avec deux piles. Chaque échantillon désigne une pile par son indice, et une pile liste ses frames en commençant par la feuille. L'indice 0 de chaque table est l'entrée vide.

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

Ouvrez **Profiles**, choisissez le service `checkout` et le type d'échantillon `cpu` : vous verrez `main` > `handle` > `db.Exec`.

## Ouvrir la page Profiles

1. Ouvrez **Profiles** depuis le menu **More**.
2. Choisissez un **service**, un **type d'échantillon** (par exemple `cpu` ou `alloc_space`) et une fenêtre de temps. Les séries sont listées par service et type d'échantillon, car des valeurs de types différents ne peuvent pas être additionnées.
3. Le flame graph fusionne tous les échantillons de la fenêtre. La largeur d'une frame est sa part du total. Survolez une frame pour voir son total, son pourcentage et sa valeur **self**, la part passée dans cette frame et non dans les fonctions qu'elle appelle.
4. Cliquez sur une frame pour zoomer. **Reset zoom** revient au graphe complet.

Flare fusionne jusqu'à 5 000 piles distinctes. Au-delà, la page affiche **Truncated** et omet les piles les plus légères.

## Profiler un span

1. Ouvrez une trace et cliquez sur un span.
2. Cliquez sur **View profile** dans le panneau du span.

La page Profiles s'ouvre avec uniquement les échantillons pris pendant ce span. Cela exige un émetteur qui enregistre le span actif sur chaque échantillon (le lien du profil vers `trace_id` et `span_id`). Sans cela, le graphe est vide ; cliquez sur **Clear** pour revenir au graphe du service.

## Interroger l'API

```bash
# Quelles séries existent dans la dernière heure ?
curl -s -X POST http://localhost:8080/api/profiles/types \
  -H 'Content-Type: application/json' -d '{"windowMinutes":60}'

# L'arbre d'appels fusionné d'une série, éventuellement limité à un span
curl -s -X POST http://localhost:8080/api/profiles/flamegraph \
  -H 'Content-Type: application/json' \
  -d '{"service":"checkout","sampleType":"cpu","windowMinutes":60,"traceId":"<hex>","spanId":"<hex>"}'
```

La réponse du flame graph est un arbre `{ name, total, self, children }` sous une racine synthétique `all`. `sampleUnit` indique si les valeurs sont en nanosecondes, en octets ou un simple compteur.

## Limites

- Les données de profil n'ont pas encore de politique de rétention propre, comme les spans. La rétention de tous les signaux est suivie par un seul élément de la feuille de route.
- Les frames natives non symbolisées apparaissent sous la forme `module+0xadresse`.
