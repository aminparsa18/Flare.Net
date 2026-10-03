# Comment trouver les requêtes N+1

Une requête N+1 est une instruction de base de données qu'un même morceau de code exécute en boucle au sein d'une seule requête, en général une fois par ligne d'un résultat précédent. Flare les détecte à partir des spans de base de données qu'il stocke déjà. Vous n'avez pas besoin de modifier votre instrumentation.

## Comment un motif est détecté

Flare regroupe les spans de base de données d'une trace par span parent et par instruction. Un groupe de **10 instructions identiques ou plus** sous un même parent est un motif N+1.

- Un span est un span de base de données s'il porte `db.system.name` (ou l'ancien `db.system`). Les spans de base de données sans parent sont ignorés.
- L'instruction est le texte de la requête (`db.query.text` ou `db.statement`) dont les littéraux chaîne et nombre sont remplacés par `?`.
- Si un span n'a pas de texte de requête, Flare utilise l'opération et la table, par exemple `SELECT users`.

## Le voir dans une trace

1. Ouvrez une trace depuis **Traces**.
2. Dans l'onglet **Cascade**, un span parent qui a exécuté un motif N+1 affiche un badge tel que `N+1: 48× SELECT * FROM orders WHERE customer_id = ?`.

## Trouver les pires cas

1. Ouvrez **Traces**, puis l'onglet **N+1**.
2. Choisissez une fenêtre de temps. Vous pouvez saisir un nom de service ou modifier le nombre minimal de répétitions (10 par défaut, de 2 à 1000).
3. Le tableau liste jusqu'à 50 instructions, les plus coûteuses en temps total d'abord. **Traces** est le nombre de traces où le motif apparaît. **Répétitions max** est le plus grand nombre de répétitions sous un seul parent.
4. Cliquez sur **Trace d'exemple** pour ouvrir le pire cas et repérer le badge.

La liste est calculée à partir de vos spans à l'ouverture de la page, elle couvre donc aussi les données stockées avant sa première ouverture. L'API correspondante est `POST /api/traces/n-plus-one`.
