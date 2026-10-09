# Transférer la télémétrie vers un autre endpoint OTLP

Flare peut envoyer une copie de tout ce qu'il accepte vers un ou plusieurs autres endpoints OTLP/HTTP, par exemple pour faire tourner Flare à côté d'un autre backend pendant une migration, ou pour alimenter un second consommateur.

## Ajouter une cible dans le tableau de bord

Ouvrez **Paramètres > Espace de travail > Export de télémétrie** (réservé aux administrateurs) et choisissez **Nouvelle cible**. Donnez-lui un nom et l'URL de base de l'endpoint, ajoutez si besoin des en-têtes (un `Nom: valeur` par ligne), choisissez les signaux, services et clés d'ingestion à transférer, puis enregistrez. Flare.Ingest relit les cibles enregistrées toutes les 30 secondes : une cible créée, modifiée, désactivée ou supprimée prend effet sans redémarrage.

Les valeurs d'en-tête sont masquées une fois enregistrées. En modifiant une cible, laissez une valeur masquée telle quelle pour la conserver, ou saisissez-en une nouvelle pour la remplacer.

Le tableau affiche l'état en direct de chaque cible, actualisé toutes les quelques secondes : requêtes **en attente** dans sa file, compteurs **envoyées** et **en échec**, et dernière erreur. Les cibles définies en configuration apparaissent sous les cibles enregistrées, marquées *Depuis la configuration* ; elles sont en lecture seule ici.

## Ou configurer une cible dans des fichiers

Les cibles peuvent aussi être définies dans la configuration de **Flare.Ingest** (appsettings ou variables d'environnement), ce qui convient à l'infrastructure as code. Les cibles enregistrées fonctionnent en plus de celles-ci. Avec des variables d'environnement, qui correspondent à `Forwarding:Targets:<index>:<réglage>` :

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
| `QueueCapacity` | `10000` | Requêtes conservées par la file Redis de la cible ; au-delà, les plus anciennes sont supprimées. |
| `MaxAttempts` | `3` | Tentatives d'envoi immédiates en cas d'erreur réseau, 429 et 5xx, avant que la requête attende une nouvelle tentative. |

Trois réglages s'appliquent à toutes les cibles et se placent directement sous `Forwarding` :

| Réglage | Défaut | Signification |
|---|---|---|
| `Forwarding__MaxAge` | `06:00:00` | Une requête en file plus ancienne que cette durée est abandonnée au lieu d'être envoyée. |
| `Forwarding__ReclaimIdle` | `00:00:30` | Durée d'attente d'une requête non livrée avant une nouvelle tentative. |
| `Forwarding__RefreshInterval` | `00:00:30` | Fréquence de relecture des cibles enregistrées. |

Une cible invalide en configuration (URL incorrecte, nom en double) arrête Flare.Ingest au démarrage.

## À quoi s'attendre

- Seules les requêtes acceptées par Flare sont transférées. Celles qui sont rejetées (limite d'une clé d'ingestion dépassée, service non autorisé, requête malformée) ne le sont pas.
- Chaque cible a sa propre file dans Redis. Une requête en est retirée dès que la destination l'a acceptée (ou a répondu par un 4xx non retentable comme 401, compté en échec puis abandonné). Une erreur réseau, un 429 ou un 5xx laisse la requête en file ; elle est retentée après `ReclaimIdle`, jusqu'à ce qu'elle soit plus ancienne que `MaxAge`. La file survit à un redémarrage de Flare.Ingest, et plusieurs réplicas de Flare.Ingest se partagent le travail.
- La livraison est au moins une fois : si une requête a expiré alors que la destination l'avait en fait enregistrée, la nouvelle tentative la duplique. Si la destination reste indisponible plus longtemps que `MaxAge`, ou si la file dépasse `QueueCapacity`, les requêtes les plus anciennes sont abandonnées. La copie propre à Flare n'est jamais affectée, et une destination lente ne ralentit jamais l'ingestion.
- Supprimer ou désactiver une cible efface sa file et ses compteurs.
- Les profils ne sont pas transférés.
- Une cible avec des clés d'ingestion ne reçoit rien des requêtes qui ne portent pas de clé d'ingestion.

Notes de conception : [ADR-0155](../../docs-internal/adr/0155-otlp-forwarding.md), [ADR-0157](../../docs-internal/adr/0157-managed-telemetry-export.md).
