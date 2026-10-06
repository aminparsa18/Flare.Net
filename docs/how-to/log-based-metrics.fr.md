# Comment transformer une recherche de logs en métrique

Comptez les logs qui correspondent à un filtre au fil de leur arrivée et
stockez le résultat sous forme de métrique. Les graphiques et les règles
d'alerte sur cette métrique lisent une petite série temporelle au lieu de
parcourir la table des logs à chaque fois, ce qui garde « les erreurs de
checkout par minute » peu coûteux sur une instance chargée.

Gérez les métriques de logs dans **Paramètres > Métriques de logs**, ou via
l'API.

## Prérequis

- Un compte Membre ou Administrateur, ou un [jeton d'accès personnel](configure-authentication.fr.md#jetons-daccès-personnels)
  associé. Les lecteurs ne peuvent pas créer de métriques de logs.
- L'adresse de l'API de Flare, `http://localhost:8080` dans les exemples.

## Créer une métrique depuis le tableau de bord

Dans l'explorateur de logs, définissez le filtre à compter puis cliquez sur
**Créer une métrique**. Le formulaire s'ouvre avec ce filtre déjà rempli, filtres
d'attributs compris. Donnez un nom, choisissez le nom de la métrique et ajoutez
éventuellement des clés de regroupement, puis cliquez sur **Prévisualiser les
séries** : Flare lit la dernière heure de logs stockés et indique combien de
séries ces clés créeraient, en listant les plus actives. Si ce nombre est
élevé, retirez une clé avant d'enregistrer. Vous pouvez aussi partir de zéro
avec **Nouvelle métrique de logs** dans **Paramètres > Métriques de logs**, qui
permet aussi de lister, suspendre, modifier et supprimer les métriques.

Le même aperçu est disponible via `POST /api/log-metrics/preview` avec un corps
`{ "condition": {...}, "groupBy": [...] }`.

## Créer une métrique de logs

Cet exemple compte les logs d'erreur (gravité 17 et plus) de `checkout`,
répartis selon l'attribut `http.route` :

```bash
curl -X POST http://localhost:8080/api/log-metrics \
  -H "Authorization: Bearer $FLARE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Checkout errors",
    "metricName": "logs.checkout.errors",
    "condition": { "services": ["checkout"], "severityNumbers": [17, 18, 19, 20, 21, 22, 23, 24] },
    "groupBy": ["http.route"]
  }'
```

- `metricName` est le nom que vous tracez et sur lequel vous alertez.
  Commencez-le par `logs.` pour qu'il n'entre pas en collision avec une
  métrique enregistrée par une application. Il doit commencer par une lettre et
  n'utiliser que des lettres, des chiffres, `_`, `.` et `-`.
- `condition` accepte les mêmes champs que la condition d'une
  [règle de pipeline](manage-pipeline-rules.fr.md) : services, numéros de
  gravité, texte du corps et conditions sur les attributs. Une condition vide
  compte tous les logs.
- `groupBy` est facultatif, avec jusqu'à cinq clés d'attribut. Chacune devient
  un attribut de la métrique. Une clé est lue dans les attributs du log, puis
  dans ses attributs de ressource.

La métrique commence à compter les nouveaux logs en environ 30 secondes. Les
logs déjà stockés ne sont pas comptés.

## Utiliser la métrique

Ouvrez **Metrics > Catalog** : `logs.checkout.errors` figure dans la liste comme
n'importe quelle métrique, avec son nombre de séries. Tracez-la dans
l'explorateur de métriques ou dans un panneau de tableau de bord, ou créez une
alerte de métrique dessus. C'est une somme delta d'unité `{log}` : son compte
sur une fenêtre est le nombre de logs correspondants dans cette fenêtre.

Chaque point porte aussi le `service.name` des logs.

## Maîtriser la cardinalité

Chaque combinaison distincte de valeurs de regroupement est une série.
Regroupez selon des attributs ayant peu de valeurs, comme une route ou un code
de statut, pas selon un identifiant d'utilisateur ou de requête.

Par sécurité, une métrique émet au plus 1000 combinaisons distinctes par vidage.
Quand il en arrive davantage, les supplémentaires sont comptées sous la valeur
`__overflow__` : le total reste juste, mais le détail est perdu. Si vous voyez
`__overflow__` dans un graphique, retirez la clé de regroupement qui en est la
cause.

## Modifier ou supprimer une métrique

```bash
curl http://localhost:8080/api/log-metrics -H "Authorization: Bearer $FLARE_TOKEN"
curl -X PUT http://localhost:8080/api/log-metrics/<id> ...   # same body as create
curl -X DELETE http://localhost:8080/api/log-metrics/<id> -H "Authorization: Bearer $FLARE_TOKEN"
```

Définir `"enabled": false` met le comptage en pause sans supprimer la
définition. Supprimer ou modifier une métrique ne change pas les points déjà
stockés : ils restent jusqu'à ce que la période de rétention des métriques les
supprime.
