# Tracer les sessions, erreurs et la santé des versions de Flare dans Grafana

Les API [Prometheus](query-with-prometheus-api.fr.md), [Loki](query-with-loki-api.fr.md) et [Tempo](query-with-tempo-api.fr.md) permettent aux sources de données natives de Grafana de lire les métriques, logs et traces de Flare. Une partie de ce que Flare connaît n'a pas sa place dans ces langages de requête : les **sessions** de l'application cliente derrière un plantage, les **groupes d'erreurs** et leur état de suivi, la **santé des versions** (release health) par version, et les **releases** que vous avez marquées. Le plugin de source de données Flare les lit, pour les placer à côté de vos autres panneaux.

Son backend s'exécute dans le **serveur Grafana** et appelle Flare.Api avec votre jeton : le serveur Grafana doit donc joindre Flare, le jeton n'atteint jamais le navigateur, et les requêtes fonctionnent aussi dans les règles d'alerte, les tableaux de bord publics et les rapports.

## Installer le plugin

Le plugin n'est pas encore dans le catalogue Grafana. Il nécessite Grafana 13.2 ou plus récent. Il a un composant backend : l'installation doit donc contenir un binaire pour votre plateforme (Linux, macOS ou Windows ; x64 ou arm64).

**Depuis une release.** Chaque [release GitHub](https://github.com/aminparsa18/Flare.Net/releases) `grafana-plugin-v*` fournit `apardev-flare-datasource-<version>.zip`, avec le frontend et le backend de toutes les plateformes. Décompressez-le dans le répertoire des plugins de Grafana, autorisez son chargement sans signature, puis redémarrez Grafana :

```bash
unzip apardev-flare-datasource-<version>.zip -d /var/lib/grafana/plugins
export GF_PLUGINS_ALLOW_LOADING_UNSIGNED_PLUGINS=apardev-flare-datasource
```

**Depuis le dépôt.** Il faut Node 22 et Go. Compilez d'abord le frontend (il vide `dist/`), puis le backend (`build:linux`, `build:darwin`, `build:windows`, ou aucune cible pour toutes) :

```bash
cd src/grafana-datasource
npm ci && npm run build
go run github.com/magefile/mage -v build:linux
```

Copiez `dist/` dans le répertoire des plugins de Grafana sous le nom `apardev-flare-datasource` et définissez la même variable d'environnement. Avec Docker, montez `dist/` (ou la release décompressée) sur `/var/lib/grafana/plugins/apardev-flare-datasource`.

## Connecter Grafana

1. Créez un [jeton d'accès personnel](../admin/configure-authentication.fr.md#jetons-daccès-personnels).
2. Dans Grafana, ajoutez une source de données **Flare**.
3. Définissez **Flare API URL** sur la base de votre API Flare, telle que le serveur Grafana la joint (`http://flare-api:8080` sur le réseau compose de Flare).
4. Collez le jeton. Ne le laissez vide que si l'authentification de Flare est désactivée.
5. Facultatif : renseignez **Flare dashboard URL** pour que l'identifiant d'une session renvoie vers sa page dans Flare.
6. Cliquez sur **Save & test**.

## Requêtes

| Requête | Résultat | Options |
|---|---|---|
| **Sessions** | Une ligne par session de l'application cliente sur la plage de temps : début, dernière activité, identifiant, service, version, OS, appareil, spans, traces, erreurs et écrans | Service, version, erreurs seulement |
| **Session timeline** | Les spans d'une session dans l'ordre de démarrage : nom, durée, écran, erreur, exception, breadcrumb et lien vers la trace | Identifiant de session |
| **Errors** | Les groupes d'exceptions classés par nombre d'occurrences, avec première et dernière apparition, services touchés et responsable | Service, top N |
| **Error issues** | L'état de suivi des groupes d'erreurs : statut (ouvert, résolu, ignoré, régressé), responsable, version de la régression et lien du ticket | Aucune |
| **Release health** | Une ligne par version de l'application : sessions, sessions plantées et en erreur, utilisateurs, et taux de sessions et d'utilisateurs sans plantage | Service |
| **Release health trend** | Une série par version dans le temps | Service, série (% de sessions sans plantage, sessions ou sessions plantées), largeur de bucket |
| **Release health value** | Un nombre par version sur la plage de temps : % de sessions ou d'utilisateurs sans plantage, ou un compteur ; conçu pour les règles d'alerte | Service, valeur, minimum de sessions |
| **Releases** | Les déploiements que vous avez marqués, avec commit, URL, notes et nombres de nouvelles erreurs et de régressions | Service |
| **App starts** | Durée des démarrages à froid et à chaud (spans `app.start`) : nombre, p50 et p95 par version | Service, version |
| **Screen performance** | Par écran : chargements, p50 et p95 de chargement (spans `screen.load`), et part des images lentes et figées (spans `screen.frames`) | Service, version |

**Service** accepte un nom ou plusieurs séparés par des virgules, et une variable à valeurs multiples fonctionne. Vide, il désigne tous les services. Les requêtes de version et de santé des versions avec plusieurs services s'exécutent une fois par service et ajoutent une colonne ou un label Service.

## Variables de modèle

Créez une variable de type **Query**, choisissez la source de données Flare et indiquez ce qu'elle liste : les services ayant des sessions, les versions d'application ayant des sessions (éventuellement pour un service), les services ayant des erreurs, ou les versions de release. Utilisez la variable dans le champ Service d'un panneau, par exemple `$service`.

## Afficher les déploiements sur un panneau

Ajoutez une requête d'annotation sur la source de données Flare avec la requête **Releases**. Les réglages d'annotation de Grafana associent les champs `time`, `title` et `text`, que la requête fournit : une ligne en pointillés marque chaque déploiement. Pour des marqueurs de release sur des panneaux Prometheus, utilisez les métriques `flare_release_*` et les métriques d'annotation décrites dans [le guide de l'API Prometheus](query-with-prometheus-api.fr.md#annotations).

## Importer le tableau de bord

Le plugin embarque un tableau de bord **Flare app health** : sessions sans plantage et sessions par version dans le temps, santé des versions, démarrage de l'application et performance des écrans, principales erreurs, sessions avec erreurs et suivi des erreurs, avec vos releases marquées en annotations. Ouvrez l'onglet **Dashboards** de la source de données, cliquez sur **Import**, puis choisissez les services de la variable **Service** en haut. Les panneaux sont des panneaux Grafana ordinaires sur les requêtes ci-dessus : modifiez-les ou copiez-les librement.

## Alerter sur les sessions sans plantage

**Release health value** renvoie un nombre par version de l'application (et par service si vous en nommez plusieurs) sur la plage de temps de la requête : une règle d'alerte Grafana peut donc le comparer directement. Créez une règle avec ces requêtes :

1. **A** : la source de données Flare, requête **Release health value**, votre service, valeur **Crash-free sessions %**. Réglez la plage de temps de la requête sur la fenêtre à juger, par exemple la dernière heure (Flare répond de 5 minutes à 7 jours). Renseignez **Min sessions** (par exemple `50`) pour qu'une version avec quelques sessions seulement ne déclenche pas la règle.
2. **B** : une expression **Threshold** sur A, **Is below** `99`.

Il n'y a pas d'étape Reduce : la valeur est déjà un nombre unique. Grafana évalue une instance d'alerte par version, avec le label `version` (et `service`) : une mauvaise version alerte donc seule. La valeur peut aussi être le % d'utilisateurs sans plantage, les sessions, les sessions plantées, les sessions avec erreur, les utilisateurs ou les utilisateurs touchés par un plantage. Une version écartée par **Min sessions** ne produit aucune instance, et une plage sans aucune session donne « No data », géré par le réglage « no data » de la règle.

Pour alerter sur l'évolution des sessions sans plantage dans la fenêtre, utilisez **Release health trend**, qui renvoie une série temporelle : ajoutez une expression **Reduce** dessus (fonction **Last**, mode **Drop non-numeric**) et placez le **Threshold** sur celle-ci. Les requêtes tabulaires (sessions, erreurs, santé des versions, performance de l'application) ne peuvent pas servir d'alerte, car les expressions de Grafana exigent des nombres ou des séries temporelles ; pour celles-ci, utilisez les [règles d'alerte](../../tutorials/first-alert.fr.md) de Flare.

## Limites

- **Une règle d'alerte exige un nombre ou une série temporelle.** Seules **Release health value** (un nombre) et **Release health trend** (une série temporelle) en renvoient ; les requêtes tabulaires s'affichent dans les tableaux de bord, les tableaux de bord publics et les rapports, mais pas dans les conditions d'alerte.
- **7 jours au plus par requête.** Une plage plus large est réduite à ses 7 derniers jours et le panneau le signale.
- **500 sessions au plus par requête**, les plus récentes. Réduisez le service, la version ou la plage quand le panneau avertit que la liste a été tronquée.
- **Une timeline de session exige que la session soit dans la plage de temps.** La requête parcourt la plage du tableau de bord pour les spans de cette session ; placez donc la plage au moment où la session s'est déroulée (la requête Sessions la liste).
- **La santé des versions suit la définition de session de Flare** : une session est plantée quand elle contient un `app.unhandled_exception` échappé, et les utilisateurs sont les valeurs distinctes de `user.id`, comme dans la vue Releases de Flare.

Notes de conception : [ADR-0254](../../../docs-internal/adr/0254-grafana-data-source-plugin.md), [ADR-0255](../../../docs-internal/adr/0255-grafana-plugin-session-timeline-performance-dashboard.md), [ADR-0256](../../../docs-internal/adr/0256-grafana-plugin-go-backend.md).
