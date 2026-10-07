# Comment réduire les attributs d'une métrique à l'ingestion

Supprimez un attribut de point de données à forte cardinalité, comme `user.id`
ou un identifiant de requête, avant que Flare ne stocke la métrique, quand vous
ne pouvez pas modifier l'application qui l'enregistre. Le nombre de séries de la
métrique, et le coût de stockage et de requête qui va avec, diminue sans toucher
à l'émetteur.

Quand c'est possible, corrigez plutôt l'attribut là où la métrique est
enregistrée (voir
[Corriger une métrique à forte cardinalité](find-high-cardinality-metrics.fr.md#corriger-une-métrique-à-forte-cardinalité)).
Utilisez une règle quand ce n'est pas possible.

## Prérequis

- Un compte Membre ou Admin. Les lecteurs ne peuvent pas créer de règles.
- Une métrique avec un grand nombre de séries.
  [Trouvez-la dans le catalogue de métriques](find-high-cardinality-metrics.fr.md).

## Créer une règle

1. Ouvrez **Métriques > Catalogue** et cliquez sur la métrique.
2. Dans **Réduire les attributs à l'ingestion**, cochez les attributs à
   supprimer. Chacun affiche son nombre de valeurs distinctes, ce qui fait
   ressortir celui à forte cardinalité.
3. Choisissez un mode :
   - **Supprimer la sélection** retire les attributs cochés et garde les autres.
   - **Garder uniquement la sélection** garde les attributs cochés et retire
     les autres.
4. Cliquez sur **Aperçu**.
5. Si les chiffres vous conviennent, cliquez sur **Créer la règle**.

La règle s'applique aux nouvelles données en 30 secondes environ.

## Vérifier l'effet avant d'enregistrer

**Aperçu** compte les séries actives de la métrique sur la dernière heure, telles
qu'elles sont stockées, puis de nouveau sans les attributs cochés. Par exemple :
« Réduirait les séries actives de 19,9 K à 1,2 K. » Pour une règle à préfixe, il
liste aussi chaque métrique concernée, celles qui perdent le plus de séries en
premier.

L'aperçu est calculé sur les données stockées : une métrique déjà réduite par une
règle précédente apparaît donc inchangée. C'est un essai à blanc : rien n'est
enregistré ni modifié. Modifier les attributs cochés ou le mode efface l'aperçu.

## Ce que fait une règle

- Elle ne s'applique qu'aux **attributs de point de données**. Les attributs de
  ressource et de portée décrivent l'émetteur et restent intacts.
- Les points qui se retrouvent sur la même série au même horodatage sont fusionnés.
  Les sommes et histogrammes en delta sont additionnés. Les jauges et les
  métriques cumulatives gardent la dernière valeur. Les points d'histogramme
  exponentiel sont réduits mais pas fusionnés.
- Plusieurs règles couvrant une même métrique s'appliquent dans l'ordre de
  création, chacune voyant le résultat de la précédente.
- **Les attributs supprimés sont irrécupérables.** La règle s'applique aux
  données telles qu'elles sont stockées. Les données déjà stockées gardent leurs
  attributs jusqu'à leur expiration.
- Soyez prudent avec les métriques **cumulatives**. Fusionner plusieurs séries
  cumulatives en une seule en gardant la dernière valeur n'équivaut pas à leur
  somme. Préférez les règles sur les métriques en delta, les jauges et les
  histogrammes, ou supprimez des attributs qui étaient de toute façon constants
  pour chaque série.

## Couvrir plusieurs métriques avec un préfixe

Le catalogue crée une règle pour une seule métrique. Pour couvrir une famille,
comme `http.client.*`, créez la règle via l'API. Le nom peut se terminer par un
seul `*` après un préfixe non vide :

```bash
curl -X POST http://localhost:8080/api/metric-attribute-rules \
  -H "Authorization: Bearer $FLARE_PAT" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Drop user.id from HTTP client metrics",
    "metricName": "http.client.*",
    "mode": "Drop",
    "attributes": ["user.id"]
  }'
```

`POST /api/metric-attribute-rules/preview` accepte le même corps sans `name` et
renvoie le nombre de séries par métrique concernée. Une règle à préfixe apparaît
aussi dans le panneau de chaque métrique qu'elle couvre, marquée « via
http.client.* ».

Les noms de règles sont uniques (sans distinction de casse) : enregistrer une règle dont le nom est déjà pris par une autre est refusé avec un 409, ce qui permet aux outils de désigner une règle par son nom.

## Désactiver ou supprimer une règle

Le panneau de la métrique liste les règles qui la couvrent, y compris celles à
préfixe. Utilisez l'interrupteur pour en désactiver une sans la perdre, ou la
corbeille pour la supprimer. Les nouvelles données sont de nouveau stockées avec
tous leurs attributs une fois la modification prise en compte.

## Trouver les règles qui ne correspondent à rien

Une règle peut cesser de correspondre quand une métrique est renommée, n'est plus
envoyée, ou que son nom contient une faute de frappe. Les membres voient en haut
du catalogue de métriques un avertissement listant les règles qui ne
correspondent à aucune métrique reçue au cours des dernières 24 heures, chacune
avec un bouton de suppression. Une métrique simplement silencieuse depuis un jour
y figure aussi : vérifiez donc le nom avant de supprimer.
