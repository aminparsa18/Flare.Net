# Comment définir la rétention des données et déplacer les anciennes données vers un stockage froid

Par défaut, Flare conserve indéfiniment tous les logs, spans, métriques et profils. La rétention borne l'usage disque : chaque signal a une durée de vie, ClickHouse supprime les lignes plus anciennes et peut d'abord déplacer les données vieillissantes vers un stockage objet moins cher. Seul un Admin peut la modifier ; toute personne connectée peut la lire.

## Définir la rétention dans le tableau de bord

1. Ouvrez **Settings**, puis **Retention** sous Workspace.
2. Chaque signal (logs, traces, metrics, profiles) a sa propre carte. Dans **Keep for (days)**, saisissez un nombre de jours. `0` conserve le signal indéfiniment.
3. Choisissez **Apply**.

La carte affiche **In ClickHouse now**, lu en direct dans la base, et signale **Differs from last request** quand la valeur ne correspond plus à ce que Flare a appliqué en dernier, par exemple après une modification manuelle d'un TTL. Un TTL que Flare n'a pas écrit apparaît comme **Custom TTL**.

L'application est asynchrone. La carte affiche **Applying…** tant que ClickHouse n'a pas posé le nouveau TTL, et un seul changement peut s'exécuter à la fois. ClickHouse supprime ensuite les données expirées lors des fusions en arrière-plan : l'espace disque se libère en quelques heures, pas au moment où le changement réussit. Raccourcir la rétention est irréversible.

Les agrégats derrière la carte des services, les SLO et les tableaux de bord (`service_metrics` et similaires) n'ont pas de TTL ; ils survivent donc aux données brutes dont ils sont issus.

## Garder certaines ressources plus ou moins longtemps

Sous **Per-resource rules**, choisissez **Add rule** et saisissez un attribut de ressource, une valeur et un nombre de jours, par exemple `deployment.environment` = `dev` pendant `7` jours. Les lignes dont l'attribut de ressource est égal à la valeur utilisent cette durée. La première règle qui correspond l'emporte, et **Default (days)** couvre tout le reste.

- La correspondance est une égalité exacte sur un attribut de ressource, pas un préfixe ni un motif, et pas sur les attributs de logs ou de spans.
- Un signal accepte au plus 20 règles.
- Les règles s'appliquent aux lignes à l'écriture. Les données déjà fusionnées sous un ancien jeu de règles gardent la durée qui leur a été donnée.

## Déplacer les anciennes données vers le stockage froid

Le stockage froid est désactivé par défaut. Démarrez Flare avec l'overlay cold-storage pour l'activer :

```bash
docker compose -f docker-compose.yml -f docker-compose.cold-storage.yml up -d
```

(`docker-compose.cold-storage.cluster.yml` est la variante cluster ; avec Aspire, appelez `AddFlare().WithColdStorage()`.) Cela ajoute un conteneur RustFS comme stockage compatible S3 et une politique de stockage ClickHouse qui l'utilise. La carte **Cold storage** de la page Retention liste alors les disques avec leur espace libre, et chaque signal reçoit un champ **Move to cold after (days)**.

Choisissez une valeur inférieure à la rétention du signal, ou à la plus courte de ses règles, sinon les données seraient supprimées avant d'être déplacées. `0` signifie aucun déplacement. Avec une rétention de `0` et un délai de déplacement défini, les données passent en stockage froid et ne sont jamais supprimées.

Les données froides restent dans la même table : recherches, alertes et tableaux de bord continuent de fonctionner, la lecture depuis le stockage froid étant plus lente. Les déplacements se font en arrière-plan, en quelques minutes à quelques heures. Désactiver ensuite le déplacement retire la règle de déplacement mais laisse en place les parties déjà en stockage froid.

## En ligne de commande

```bash
flare retention show
flare retention set logs --days 30
flare retention set logs --days 30 --cold-after 7
flare retention set logs --days 30 --rule deployment.environment=dev:7 --rule k8s.namespace=load-test:1
flare retention set logs --clear-rules
```

`show` affiche le TTL réel de chaque signal, le délai de déplacement, les règles et l'état du dernier changement. `set` attend que ClickHouse ait appliqué le changement (`--no-wait` rend la main dès que l'API l'accepte). Les options omises gardent leur valeur actuelle : `--days 14` seul ne réinitialise donc pas les règles. `--rule` prend `ATTRIBUTE=VALUE:DAYS`, est répétable et remplace les règles existantes.

## Limites

- La durée va de 0 (indéfiniment) à 18250 jours (50 ans).
- Un second changement est rejeté avec `409` tant qu'un autre s'applique. Un changement qui n'a pas abouti après 30 minutes, par exemple parce que l'API a redémarré, est signalé comme échoué.
- L'API est `GET /api/retention` et `PUT /api/retention`. La conception est décrite dans [ADR-0143](../../docs-internal/adr/0143-retention-ttl.md) (TTL), [ADR-0144](../../docs-internal/adr/0144-cold-storage-rustfs.md) (stockage froid) et [ADR-0145](../../docs-internal/adr/0145-per-resource-retention.md) (règles par ressource).
