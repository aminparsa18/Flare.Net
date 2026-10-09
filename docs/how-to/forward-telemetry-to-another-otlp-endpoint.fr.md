# Transférer la télémétrie vers un autre endpoint OTLP

Flare peut envoyer une copie de tout ce qu'il accepte vers un ou plusieurs autres endpoints OTLP/HTTP, par exemple pour faire tourner Flare à côté d'un autre backend pendant une migration, ou pour alimenter un second consommateur.

## Configurer une cible

Les cibles se configurent sur **Flare.Ingest** (appsettings ou variables d'environnement). Avec des variables d'environnement, qui correspondent à `Forwarding:Targets:<index>:<réglage>` :

```yaml
services:
  ingest:
    environment:
      Forwarding__Targets__0__Name: second-backend
      Forwarding__Targets__0__Endpoint: https://collector.example.com:4318
      Forwarding__Targets__0__Headers__Authorization: Bearer <token>
      Forwarding__Targets__0__Signals__0: Logs
      Forwarding__Targets__0__Services__0: checkout
```

Flare ajoute `/v1/logs`, `/v1/traces` ou `/v1/metrics` à `Endpoint` et envoie du protobuf compressé en gzip.

| Réglage | Défaut | Signification |
|---|---|---|
| `Name` | obligatoire | Libellé unique utilisé dans les messages de log. |
| `Endpoint` | obligatoire | URL de base de l'endpoint OTLP/HTTP récepteur (`http` ou `https`). |
| `Headers` | aucun | En-têtes de requête supplémentaires, comme `Authorization`. |
| `Signals` | tous | `Logs`, `Traces`, `Metrics`, au choix. |
| `Services` | tous | Ne transférer que ces valeurs de `service.name` ; les données des autres services sont retirées de la copie. |
| `IngestKeyIds` | tous | Ne transférer que les requêtes authentifiées avec ces clés d'ingestion (identifiants de clé). |
| `Gzip` | `true` | Compresser les corps de requête. |
| `Timeout` | `00:00:10` | Délai d'attente par requête. |
| `QueueCapacity` | `1000` | Requêtes mises en mémoire tampon par cible. |
| `MaxAttempts` | `3` | Tentatives d'envoi en cas d'erreur réseau, 429 et 5xx. |

Une cible invalide (URL incorrecte, nom en double) arrête Flare.Ingest au démarrage.

## À quoi s'attendre

- Seules les requêtes acceptées par Flare sont transférées. Celles qui sont rejetées (limite d'une clé d'ingestion dépassée, service non autorisé, requête malformée) ne le sont pas.
- Le transfert se fait au mieux. Si la destination est lente ou indisponible, la file se remplit et les requêtes les plus récentes sont abandonnées avec un avertissement dans le log d'ingestion ; la copie propre à Flare n'est jamais affectée. Redémarrer Flare.Ingest perd ce qui était encore en file. Si vous avez besoin d'une diffusion durable, placez plutôt un OpenTelemetry Collector devant Flare.
- Les profils ne sont pas transférés.
- Une cible avec `IngestKeyIds` ne reçoit rien des requêtes qui ne portent pas de clé d'ingestion.

Notes de conception : [ADR-0155](../../docs-internal/adr/0155-otlp-forwarding.md).
