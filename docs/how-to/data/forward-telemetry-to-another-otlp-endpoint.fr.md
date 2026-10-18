# Transférer la télémétrie vers un autre endpoint OTLP

Flare peut envoyer une copie de tout ce qu'il accepte vers un ou plusieurs autres endpoints OTLP/HTTP, par exemple pour faire tourner Flare à côté d'un autre backend pendant une migration, ou pour alimenter un second consommateur.

## Ajouter une cible dans le tableau de bord

Ouvrez **Paramètres > Espace de travail > Export de télémétrie** (réservé aux administrateurs) et choisissez **Nouvelle cible**. Donnez-lui un nom et l'URL de base de l'endpoint, ajoutez si besoin des en-têtes (un `Nom: valeur` par ligne), choisissez les signaux, services et clés d'ingestion à transférer, puis enregistrez. Flare.Ingest relit les cibles enregistrées toutes les 30 secondes : une cible créée, modifiée, désactivée ou supprimée prend effet sans redémarrage.

Les valeurs d'en-tête sont masquées une fois enregistrées. En modifiant une cible, laissez une valeur masquée telle quelle pour la conserver, ou saisissez-en une nouvelle pour la remplacer.

Le tableau affiche l'état en direct de chaque cible, actualisé toutes les quelques secondes : requêtes **en attente** dans sa file, compteurs **envoyées** et **en échec**, et dernière erreur. Les cibles définies en configuration apparaissent sous les cibles enregistrées, marquées *Depuis la configuration* ; elles sont en lecture seule ici.

## Envoyer vers Grafana Cloud

En créant une cible, choisissez **Grafana Cloud** sous **Destination** et renseignez trois valeurs de la tuile OpenTelemetry de votre stack :

- **Région** : la région de la passerelle, par exemple `prod-us-central-0`. Coller l'URL complète de l'endpoint fonctionne aussi.
- **ID d'instance** : l'identifiant numérique de la stack.
- **Jeton de politique d'accès** : nécessite les portées `logs:write`, `traces:write` et `metrics:write`.

Flare construit l'endpoint (`https://otlp-gateway-<région>.grafana.net/otlp`) et l'en-tête `Authorization: Basic` à votre place. Ce qui est enregistré est une cible ordinaire : en la modifiant plus tard, vous verrez l'endpoint tel quel et un en-tête masqué.

## Tester la connexion

**Tester la connexion**, dans la boîte de dialogue de la cible, envoie un export de logs OTLP vide à `<endpoint>/v1/logs` avec les en-têtes saisis, sans rien enregistrer et sans stocker de données chez le destinataire. Le résultat indique le statut HTTP, ou la cause de l'échec : identifiants refusés (401/403), mauvais chemin (404) ou hôte injoignable. Pour une cible enregistrée, les valeurs d'en-tête masquées sont utilisées telles qu'elles sont stockées.

Le test s'exécute depuis **Flare.Api**, alors que l'envoi réel passe par Flare.Ingest. Pour une destination privée, définissez `Outbound__Allow` (voir plus bas) sur Flare.Api comme sur Flare.Ingest.

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

## Gérer les cibles depuis la CLI ou Terraform

Les cibles enregistrées se gèrent sans le tableau de bord. Le rôle Admin est requis.

```bash
flare forwarding create grafana --endpoint https://otlp.example.com \
  --header 'Authorization=Bearer <token>' --signal logs --signal traces
flare forwarding list
flare forwarding update <id> --service checkout
flare forwarding status
flare forwarding delete <id> --yes
```

`update` ne modifie que les options passées : `--header` définit un en-tête et conserve les autres, `--clear-headers` les supprime tous, et `--all-signals`, `--all-services` et `--any-ingest-key` retirent ces filtres. Les valeurs d'en-tête sont masquées dans `list` et ne sont plus jamais affichées.

Avec le fournisseur Terraform / OpenTofu de Flare :

```hcl
resource "flare_forwarding_target" "grafana" {
  name     = "grafana"
  endpoint = "https://otlp.example.com"
  headers  = { Authorization = "Bearer ${var.grafana_token}" }
  signals  = ["Logs", "Traces"]
}
```

Les valeurs d'en-tête sont en écriture seule : Flare ne les renvoie jamais, donc une modification faite hors de Terraform n'est pas détectée. Les cibles de la configuration `Forwarding:Targets` sont distinctes et ne sont pas gérées ainsi.

## Les destinations privées sont bloquées par défaut

Flare refuse d'envoyer vers le loopback, les adresses privées (RFC 1918), link-local (`169.254.169.254`) et autres adresses non publiques, afin qu'une cible ne puisse pas viser le réseau interne de l'hôte. Pour envoyer vers un récepteur interne, autorisez-le sur Flare.Ingest :

```bash
Outbound__Allow__0=10.20.0.0/16
Outbound__Allow__1=*.corp.local
```

Chaque entrée est une IP, un CIDR ou un nom d'hôte (exact ou `*.suffixe`). `Outbound__BlockPrivateNetworks=false` désactive la vérification. Les mêmes réglages s'appliquent aux canaux d'alerte de Flare.Api et Flare.AlertWorker. Notes de conception : [ADR-0201](../../../docs-internal/adr/0201-outbound-ssrf-guard.md).

## À quoi s'attendre

- Seules les requêtes acceptées par Flare sont transférées. Celles qui sont rejetées (limite d'une clé d'ingestion dépassée, service non autorisé, requête malformée) ne le sont pas.
- Chaque cible a sa propre file dans Redis. Une requête en est retirée dès que la destination l'a acceptée (ou a répondu par un 4xx non retentable comme 401, compté en échec puis abandonné). Une erreur réseau, un 429 ou un 5xx laisse la requête en file ; elle est retentée après `ReclaimIdle`, jusqu'à ce qu'elle soit plus ancienne que `MaxAge`. La file survit à un redémarrage de Flare.Ingest, et plusieurs réplicas de Flare.Ingest se partagent le travail.
- La livraison est au moins une fois : si une requête a expiré alors que la destination l'avait en fait enregistrée, la nouvelle tentative la duplique. Si la destination reste indisponible plus longtemps que `MaxAge`, ou si la file dépasse `QueueCapacity`, les requêtes les plus anciennes sont abandonnées. La copie propre à Flare n'est jamais affectée, et une destination lente ne ralentit jamais l'ingestion.
- Supprimer ou désactiver une cible efface sa file et ses compteurs.
- Les profils ne sont pas transférés.
- Une cible avec des clés d'ingestion ne reçoit rien des requêtes qui ne portent pas de clé d'ingestion.

Notes de conception : [ADR-0155](../../../docs-internal/adr/0155-otlp-forwarding.md), [ADR-0157](../../../docs-internal/adr/0157-managed-telemetry-export.md).
