# Comment savoir si un span était lent par rapport à ses semblables

Un span qui a pris 800 ms n'est un problème que si ses semblables prennent
d'habitude 50 ms. Le panneau de détail d'un span classe sa durée parmi celles
des autres spans de même service et de même nom, ce qui permet de distinguer
une valeur aberrante lente d'une opération normalement lente.

## Prérequis

- Une instance Flare en cours d'exécution qui reçoit les traces de vos
  applications.
- Au moins 10 spans de même service et de même nom dans l'heure qui entoure
  le span inspecté. En dessous, Flare n'affiche aucun classement, car un
  percentile calculé sur quelques spans n'a pas de sens.

## Lire le classement

1. Ouvrez **Traces**, puis cliquez sur une trace pour l'ouvrir.
2. Cliquez sur un span dans la cascade ou dans le flame graph. Le panneau de
   détail s'ouvre à droite.
3. Sous le nom et l'horodatage du span, cherchez une ligne du type
   **p92 de `GET /orders` dans `api`**.

La ligne indique que 92 % des spans comparés n'étaient pas plus longs que
celui-ci. Un nombre élevé comme p95 ou p99 signale une valeur aberrante. Un
nombre proche de p50 signifie que le span a duré un temps typique, même si
ce temps est long.

Survolez la ligne pour voir le détail de la comparaison : le nombre de spans
comparés et les durées **p50**, **p95** et **p99** de ce groupe.

## À quoi Flare compare le span

- Les spans de **même nom de service et de même nom de span** que celui que
  vous avez sélectionné.
- Uniquement les spans démarrés **dans l'heure qui précède ou suit** le span
  sélectionné. La fenêtre est fixe pour que le nombre ait la même
  signification d'un span à l'autre.
- Le span sélectionné fait partie du groupe.

Les autres attributs, comme la route HTTP ou le statut, ne font pas partie
de la correspondance. Si un même nom de span recouvre des requêtes de coût
très différent, utilisez l'éditeur **Structure** ou les filtres d'attributs
pour affiner le groupe.

## Voir les spans auxquels il a été comparé

Cliquez sur la ligne. **Traces** s'ouvre avec un [filtre
structurel](find-traces-by-structure.fr.md) sur ce service et ce nom de span,
trié par **Durée**, du plus lent au plus rapide. La plage de temps est le plus
petit préréglage qui couvre le début du span ; elle peut donc être plus large
que la fenêtre d'une heure utilisée pour le classement.

## L'interroger depuis l'API

Le panneau appelle `POST /api/spans/duration-percentile` :

```json
{
  "serviceName": "api",
  "name": "GET /orders",
  "durationNano": 800000000,
  "startTime": "2026-10-03T09:15:00Z"
}
```

La réponse contient `sampleCount` (0 si rien ne correspond), `percentile`
(0-100), ainsi que `p50Nano`, `p95Nano` et `p99Nano`. Le point d'accès
n'applique aucun minimum de 10 spans. Le tableau de bord masque la ligne ;
appliquez cette vérification vous-même si vous vous fiez au nombre.
