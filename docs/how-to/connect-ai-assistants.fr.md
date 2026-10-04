# Comment laisser un assistant IA interroger Flare

Lancez `flare mcp` pour donner à Claude Code, Cursor, VS Code et aux autres
clients [Model Context Protocol](https://modelcontextprotocol.io) un accès en
**lecture seule** à vos logs, traces, métriques, exceptions et alertes.
L'assistant lance lui-même `flare mcp` et communique avec lui via
stdin/stdout ; aucun nouveau port n'est ouvert. Cela referme la boucle de
débogage d'un agent de code : lancer l'application, reproduire le bug, lire la
télémétrie, modifier le code, relancer et comparer.

## Prérequis

- La [CLI `flare`](run-with-cli.fr.md) installée (`dotnet tool install -g Flare.Cli`).
- Une instance Flare contenant des données : l'instance permanente créée par
  `flare start`, ou n'importe quelle Flare.Api joignable (voir
  [Interroger un Flare distant](#interroger-un-flare-distant)).

## Enregistrer le serveur auprès de votre client

**Claude Code**

```bash
claude mcp add flare -- flare mcp
```

**Cursor** — à ajouter dans `.cursor/mcp.json` (ou `~/.cursor/mcp.json`) :

```json
{ "mcpServers": { "flare": { "command": "flare", "args": ["mcp"] } } }
```

**VS Code** — à ajouter dans `.vscode/mcp.json` :

```json
{ "servers": { "flare": { "type": "stdio", "command": "flare", "args": ["mcp"] } } }
```

Ciblez une instance nommée avec `flare mcp -n <nom>` (voir
`flare instances list`). Sans option d'instance, c'est la même instance que
pour toutes les autres commandes `flare`.

## Outils

Tous les outils sont en lecture seule et renvoient du texte compact, plafonné
pour qu'un système chargé ne submerge pas le contexte de l'assistant.

| Outil | À quoi il répond |
|---|---|
| `search_logs` | Événements de log par service, niveau, texte, identifiant de trace, attributs. 100 lignes maximum. |
| `search_traces` | Traces récentes (une ligne chacune), éventuellement uniquement en erreur ou plus lentes que N ms. |
| `get_trace` | Une trace sous forme d'arbre de spans indenté. 200 spans maximum. |
| `list_metrics` | Quelles métriques existent (nom, type, unité, service). |
| `query_metric` | Une métrique résumée par série : première/dernière/min/moyenne/max, ou percentiles pour les histogrammes. |
| `list_exceptions` | Principaux groupes d'exceptions avec nombres et services touchés. |
| `list_firing_alerts` | Règles d'alerte actuellement déclenchées, avec le résumé IA de l'incident s'il est [activé](summarize-alerts-with-ai.fr.md). |
| `list_runs` | Quand un service a démarré pour la dernière fois, et les démarrages précédents. |
| `diff_traces` | Deux traces comparées : spans ajoutés/retirés, changements de durée et d'erreurs. |
| `compare_runs` | Le même point d'entrée lors de l'exécution précédente du service et de la plus récente, comparés. |

## Se limiter à la dernière exécution

Pendant le développement, la télémétrie du processus que vous venez de lancer
importe plus que celle de la dernière heure. Passez `lastRun: true` (avec
`services`) à `search_logs`, `search_traces` ou `list_exceptions` : la plage
commence au dernier démarrage de ce service.

Une **exécution** est un démarrage de processus, détecté grâce à l'attribut de
ressource `service.instance.id`, que .NET Aspire et le SDK OpenTelemetry .NET
fixent à une nouvelle valeur à chaque démarrage. Des réplicas démarrés à moins
d'une minute d'écart comptent pour une seule exécution. La détection lit les
spans : un service qui émet des logs mais aucune trace n'a donc pas
d'exécution détectable ; utilisez `since` pour ceux-là.

## Comparer avant et après un correctif

1. Sollicitez le point d'entrée (par exemple `POST /checkout`) et constatez
   qu'il est lent ou en échec.
2. Laissez l'assistant modifier le code et redémarrer l'application.
3. Sollicitez à nouveau le point d'entrée.
4. Demandez `compare_runs` pour ce service et ce nom d'opération.

La comparaison associe les spans par service et par nom, et liste les spans
ajoutés ou retirés, les variations de durée moyenne d'au moins 20 % et 5 ms,
et les changements du nombre d'erreurs par span. `diff_traces` fait de même
pour deux identifiants de trace de votre choix.

## Interroger un Flare distant

```bash
flare mcp --api-url https://flare.example.com --token flr_pat_...
```

`--token` se rabat sur la variable d'environnement `FLARE_API_TOKEN`, ce qui
évite d'écrire le jeton dans le fichier de configuration de votre client.
Créez le jeton comme décrit dans
[Jetons d'accès personnels](configure-authentication.fr.md#jetons-daccès-personnels) ;
il hérite de votre propre rôle, donc un jeton `Viewer` ne peut rien modifier,
même si les outils sont déjà en lecture seule. L'instance locale permanente a
l'authentification désactivée par défaut et écoute sur la boucle locale ; elle
ne nécessite donc aucun jeton.

## Se connecter en HTTP

Flare.Api expose aussi les mêmes outils sur un point d'accès [streamable HTTP](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports#streamable-http) à `/mcp` : un Flare partagé ou distant ne nécessite donc aucune installation locale de `flare`. Envoyez votre jeton d'accès personnel dans un en-tête Bearer.

**Claude Code**

```bash
claude mcp add --transport http flare https://flare.example.com/mcp \
  --header "Authorization: Bearer flr_pat_..."
```

**Cursor** ou **VS Code** : utilisez `"type": "http"` (Cursor : seulement `"url"`) avec la même URL et un en-tête `Authorization`.

Le point d'accès est sans état et chaque appel d'outil s'exécute en tant qu'utilisateur du jeton : les contrôles de rôle et la limite de débit par jeton s'appliquent comme pour l'API REST. Les cookies de session ne sont pas acceptés, seulement les jetons Bearer (ou rien, quand l'authentification est désactivée). Si Flare.Api n'arrive pas à joindre sa propre adresse d'écoute, définissez `Mcp__SelfUrl`.

## Limites

- Pour un usage partagé ou en CI, authentifiez-vous avec un jeton de [compte de service](configure-authentication.fr.md#comptes-de-service) plutôt que celui d'une personne.
- Aucun outil d'écriture (création d'alertes, mise en sourdine, etc.), par
  conception.
