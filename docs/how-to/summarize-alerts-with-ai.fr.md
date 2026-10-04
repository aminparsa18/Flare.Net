# Obtenir un résumé IA d'une alerte déclenchée

Flare peut demander à un modèle de langage de rédiger un court résumé
d'incident chaque fois qu'une alerte se déclenche : la première erreur, le
service ou le span en échec, et ce qui a changé par rapport à la fenêtre
précédente. C'est désactivé par défaut et utilise un modèle que vous fournissez :
tout point d'accès compatible OpenAI, y compris un Ollama local.

Le résumé ne retarde jamais l'alerte. La notification normale part en premier ;
le résumé est généré ensuite et, s'il réussit, apparaît dans l'historique des
alertes et suit comme second message.

## L'activer

Définissez ces variables sur **à la fois** `Flare.Api` et `Flare.AlertWorker` (les
services `api` et `alert-worker` de `docker-compose.yml`). Le worker est le
processus qui évalue les règles et appelle le modèle :

```bash
Ai__Enabled=true
Ai__IncidentSummaries=true
Ai__Endpoint=http://localhost:11434/v1   # URL de base ; Flare appelle /chat/completions
Ai__Model=llama3.1
Ai__ApiKey=...                            # facultatif pour les modèles locaux
```

`Ai__Enabled` seul n'active que [Expliquer cette exception](link-exceptions-to-source-code.fr.md) ;
les résumés d'alerte demandent aussi `Ai__IncidentSummaries`.

## Ce qui est envoyé

Pour chaque alerte déclenchée, Flare envoie le nom, la description et le seuil de
la règle, la valeur observée et la même mesure pour la fenêtre précédente, ainsi
qu'un petit échantillon des preuves, selon le type de règle :

- les principaux motifs de logs de la fenêtre (règles de logs et de métriques) ;
- les exceptions les plus fréquentes (règles d'exceptions) ;
- les spans en échec d'une trace représentative.

Flare masque d'abord les jetons, mots de passe, secrets de chaîne de connexion,
e-mails et adresses IP, mais la recherche de motifs peut en manquer ; utilisez un
modèle local si votre télémétrie est sensible. Le prompt exact, une fois masqué,
est stocké avec chaque résumé dans la table `alert_event_summaries` et journalisé
au niveau Debug.

## Limites

- Le prompt est plafonné à `Ai__MaxInputChars` (12000) et la réponse à
  `Ai__MaxOutputTokens` (800).
- Au plus `Ai__IncidentSummariesPerHour` (20) résumés sont générés par heure pour
  l'ensemble des règles. Les alertes au-delà gardent leur notification normale,
  sans résumé.
- Deux résumés s'exécutent à la fois ; les suivants sont ignorés, pas mis en file.
- Les événements résolus et les alertes supprimées par une fenêtre de maintenance
  n'ont pas de résumé. Une alerte de données absentes en reçoit un construit à
  partir de la règle seule, faute de données.

## Où apparaît le résumé

- **L'historique des alertes** du tableau de bord l'affiche sous l'événement, en
  texte brut.
- **Un message de suivi** vers les webhooks Slack, Telegram, Microsoft Teams,
  Discord et les canaux e-mail, intitulé `AI summary: <nom de la règle>`.
  PagerDuty, Jira, incident.io, JSM Ops et les webhooks génériques n'en reçoivent
  pas, car un second message ouvrirait un second incident ou ressemblerait à une
  seconde alerte.

Cela nécessite la migration `0048_alert_event_summaries.sql`. Les nouvelles
installations l'appliquent automatiquement ; sur une instance existante,
exécutez-la à la main avec `clickhouse-client`.

## Voir aussi

- [Décision d'architecture : ADR-0104](../../docs-internal/adr/0104-ai-incident-summary.md)
- [ADR-0103](../../docs-internal/adr/0103-explain-exception-llm.md)
