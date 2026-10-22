# Comment voir vos requêtes de base de données les plus lentes

L'onglet **Database** classe les requêtes de base de données exécutées par vos services : celles qui coûtent le plus de temps, qui s'exécutent le plus souvent ou qui échouent le plus. Flare le construit à partir des spans de base de données déjà stockés. Vous n'avez pas à modifier votre instrumentation.

## Comment les requêtes sont regroupées

- Un span est un span de base de données quand il porte `db.system.name` (ou l'ancien `db.system`).
- La requête est le texte de la requête (`db.query.text` ou `db.statement`) dont les littéraux de chaîne et de nombre sont remplacés par `?`, si bien que `WHERE id = 42` et `WHERE id = 7` comptent comme une seule requête. C'est le même texte que l'onglet N+1.
- Quand un span n'a pas de texte de requête, Flare utilise l'opération et la table, par exemple `GET sessions`.
- Les lignes sont identifiées par service, système de base de données et requête. Les deux générations d'attributs (`db.system.name` et `db.system`) tombent dans la même ligne.
- Les spans sans parent comptent aussi, contrairement à la détection N+1.

## Lire le tableau

1. Ouvrez **Traces**, puis l'onglet **Database**.
2. Choisissez une période. Saisissez au besoin un nom de service ou un système de base de données comme `postgresql`.
3. Choisissez le classement : **Le plus lent au total** (temps total), **p95 le plus élevé**, **Le plus d'appels** ou **Le plus d'erreurs**. Le tableau liste jusqu'à 100 requêtes.
4. **Appels/min** est le nombre d'appels divisé par la durée de la période. **Moy. ms** et **p95 ms** s'entendent par exécution. **Total ms** est la somme de toutes les exécutions.
5. Cliquez sur **Trace d'exemple** pour ouvrir l'exécution la plus lente de cette requête dans sa trace.

Si vos services échantillonnent les traces, les appels, les erreurs et le temps total sont pondérés par le poids d'échantillonnage de chaque span, donc les débits ne sont pas sous-estimés.

## Quel onglet utiliser

- **Database** répond à « quelle requête coûte le plus sur l'ensemble ».
- **N+1** répond à « quelle requête une même requête utilisateur répète-t-elle de nombreuses fois ».
- La comparaison de durée dans le détail d'un span répond à « cette exécution est-elle lente pour son type ».

La liste est calculée à partir de vos spans à l'ouverture. L'API correspondante est `POST /api/traces/db-statements`.
