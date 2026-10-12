# Comment effacer ou exporter la télémétrie d'une personne

La rétention supprime la télémétrie selon son âge. Lorsqu'une personne demande que ses données soient supprimées (RGPD, art. 17) ou communiquées (art. 15), il faut les supprimer ou les exporter selon la personne concernée. Flare fait correspondre les lignes à des attributs comme `user.id`, `user.email` ou `client.address`, dans les logs, les spans (y compris les événements d'exception), les métriques, les profils et le reste des sessions de l'utilisateur, captures d'écran et retours compris. Seul un Admin peut le faire.

Vous pouvez le faire dans le tableau de bord sous **Settings > Data erasure**, ou avec la CLI `flare erasure` et l'API `/api/erasure`.

Flare ne retrouve que les données qui portent l'identifiant comme attribut. Si votre application n'enregistre jamais `user.email`, une recherche par e-mail ne trouve rien.

## Dans le tableau de bord

1. Ouvrez **Settings**, puis **Data erasure** sous Workspace.
2. Saisissez un ou plusieurs identifiants (clé et valeur d'attribut), par exemple `user.id` = `42`. Choisissez **Add identifier** pour en ajouter.
3. Choisissez **Preview** pour voir les lignes correspondantes par table, ou **Export** pour les télécharger en NDJSON.
4. Pour supprimer, saisissez la valeur du premier identifiant dans la zone de confirmation, puis choisissez **Erase**. Le bouton reste désactivé tant que l'aperçu ne correspond pas aux identifiants actuels.

La liste **Erasure requests** en dessous affiche chaque demande avec sa progression par table et s'actualise pendant une suppression.

Les sections suivantes montrent les mêmes étapes avec la CLI.

## Prévisualiser ce qui serait supprimé

```bash
flare erasure preview --id user.id=42 --id user.email=jane@example.com
```

Chaque `--id CLÉ=VALEUR` est un identifiant de la même personne. Une ligne lui appartient si l'un des identifiants correspond. La correspondance est une égalité exacte sur un attribut de ressource, de scope, de log, de span, de point de données ou d'événement de span. La commande affiche le nombre de lignes correspondantes par table.

Les sessions sont suivies aussi : Flare trouve le `session.id` de chaque log et span correspondant, puis fait également correspondre les lignes portant l'un de ces identifiants de session. Les lignes de log, spans et points de métriques de la même session sont donc incluses même s'ils ne répètent pas l'identifiant d'utilisateur. Les captures d'écran et les retours ne sont trouvés que de cette façon, plus par les champs d'identifiant utilisateur et d'e-mail propres au retour. Si plus de 10 000 sessions correspondent, l'aperçu avertit que les autres ne sont pas couvertes.

Le même appel est `POST /api/erasure/preview` avec `{"identifiers": [{"key": "user.id", "value": "42"}]}`.

## Exporter ses données

```bash
flare erasure export --id user.id=42 --output subject-42.ndjson
```

L'export est du JSON délimité par des sauts de ligne, une ligne `{"table": "logs", "row": {...}}` par ligne de données. Une table est plafonnée à 100 000 lignes ; une table plafonnée se termine par `{"table": "logs", "truncated": true}`, et vous pouvez affiner la demande ou exporter à nouveau. La forme API est `POST /api/erasure/export`.

Exportez avant d'effacer si la personne a demandé les deux.

## Effacer ses données

```bash
flare erasure run --id user.id=42 --id user.email=jane@example.com --confirm 42
```

`--confirm` doit être égal à la valeur du premier `--id`, afin qu'une commande lancée par erreur ne supprime rien. La suppression est définitive.

L'API accepte la demande avec `202` et un identifiant de transaction, puis met en file un `ALTER TABLE ... DELETE` ClickHouse par table contenant des lignes correspondantes. ClickHouse réécrit les parties de données concernées en arrière-plan ; sur une grande table, cela peut prendre longtemps. La CLI attend et affiche la progression par défaut ; passez `--no-wait` pour rendre la main aussitôt et vérifier plus tard :

```bash
flare erasure status            # demandes récentes
flare erasure status <id>       # une demande, par table
```

Chaque table affiche `pending`, `running`, `success` ou `failed`, ainsi que le nombre de parties de données que la suppression doit encore réécrire. Une demande est `success` quand la suppression de chaque table est terminée. Une seule demande peut être soumise à la fois ; une deuxième reçoit `409` tant que la première n'a pas mis ses suppressions en file.

## Ce qui reste

- **Agrégats.** Les cartes de services, les SLO, les tableaux de bord et les autres tables pré-agrégées contiennent des compteurs et des quantiles sans identifiants ; ils ne sont donc pas modifiés.
- **Sauvegardes et archives.** Les données déjà transmises à un autre point de terminaison OTLP ou archivées dans S3 sont hors de Flare. Supprimez-les à cet endroit.
- **Lignes sans l'attribut.** Une ligne de log qui mentionne la personne uniquement dans le corps du message n'est pas trouvée. Cherchez-la dans Logs.
- **Lignes ingérées après la demande.** L'effacement supprime ce qui existe au moment de son exécution. Empêchez la source d'envoyer l'identifiant, ou ajoutez une [règle de pipeline](manage-pipeline-rules.fr.md) pour le masquer.

## La piste d'audit

Chaque effacement et chaque export est consigné dans le journal d'audit (Settings > Audit log) avec l'auteur, l'heure, les clés d'attribut utilisées et un court hachage des valeurs. Les valeurs elles-mêmes ne sont pas conservées, car ce sont les données personnelles. Les demandes restent dans l'historique des effacements pendant trois ans, comme preuve qu'elles ont été traitées.

Voir l'[ADR-0198](../../../docs-internal/adr/0198-data-subject-erasure-export.md) pour la conception.
