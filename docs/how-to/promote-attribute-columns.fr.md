# Comment accélérer les filtres sur un attribut de log fréquemment utilisé

Si vous filtrez sans cesse les logs sur le même attribut (par exemple
`http.route`, `tenant.id` ou `k8s.namespace.name`), promouvez cette clé en
colonne dédiée. Flare lit alors une seule colonne avec son propre index de
saut, au lieu de toute la map d'attributs de chaque ligne. La recherche, les
graphiques et les règles d'alerte qui filtrent sur cette clé deviennent plus
rapides. Les résultats ne changent pas.

## Prérequis

- Un compte **Admin** (ou l'authentification désactivée). Les autres rôles
  voient la liste des attributs promus mais ne peuvent pas la modifier.
- La clé d'attribut exacte et l'ensemble auquel elle appartient : **Log**
  (attributs de l'enregistrement de log), **Resource** (par exemple
  `service.namespace`, `k8s.namespace.name`) ou **Scope**.

## Promouvoir une clé

1. Ouvrez **Indexing** et faites défiler jusqu'à **Promoted attributes**.
2. Choisissez l'ensemble (**Log**, **Resource** ou **Scope**) et saisissez la
   clé, par exemple `http.route`.
3. Laissez **Backfill existing data** activé, sauf si votre table `logs` est
   très volumineuse et que ses anciennes données expirent bientôt. Voir
   [Remplissage](#remplissage).
4. Cliquez sur **Promote**.

La clé apparaît dans le tableau avec son nom de colonne, par exemple
`attr_log_http_route`. Sur l'instance Flare.Api utilisée, les filtres sur la
clé utilisent immédiatement la nouvelle colonne. Les autres instances
Flare.Api et le worker d'alertes la prennent en compte sous 30 secondes.

Les clés peuvent contenir des lettres, des chiffres et `. _ - : / @`, jusqu'à
200 caractères. Vous pouvez promouvoir jusqu'à 50 clés.

## Remplissage

Avec **Backfill existing data** activé, ClickHouse réécrit les données
existantes en arrière-plan pour que les anciennes lignes obtiennent aussi la
colonne et l'index de saut. La colonne de statut affiche **Backfilling**
jusqu'à la fin, puis **Active**. Sur une grande table, cela peut prendre du
temps et solliciter le disque.

Pendant le remplissage, les filtres sur la clé renvoient des résultats
corrects. C'est aussi le cas si vous désactivez le remplissage : seule
l'accélération sur les anciennes données manque. Sans remplissage, les
anciennes données n'obtiennent la colonne qu'au fil des fusions en
arrière-plan de ClickHouse.

## Quels filtres sont accélérés

La colonne remplace la lecture de la map d'attributs pour :

- **égal**
- **différent**, **dans** et **pas dans**, sauf si l'une des valeurs comparées
  est vide

**Existe**, **absent** et les opérateurs d'expression régulière lisent
toujours la map d'attributs, car la colonne ne distingue pas une clé absente
d'une valeur vide.

## Rétrograder une clé

Cliquez sur **Demote** sur sa ligne et confirmez. Flare supprime la colonne et
son index de saut, et les filtres sur la clé relisent la map d'attributs.
Aucune donnée de log n'est perdue : l'attribut reste stocké dans la map.

## Voir aussi

- [ADR-0062 : Promoted attribute columns](../../docs-internal/adr/0062-promoted-attribute-columns.md)
  décrit la conception : nommage, DDL en mode cluster, et pourquoi le schéma de
  la table sert lui-même de registre.
