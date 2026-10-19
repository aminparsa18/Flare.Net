# Tracer les sessions, erreurs et la santé des versions de Flare dans Grafana

Les API [Prometheus](query-with-prometheus-api.fr.md), [Loki](query-with-loki-api.fr.md) et [Tempo](query-with-tempo-api.fr.md) permettent aux sources de données natives de Grafana de lire les métriques, logs et traces de Flare. Une partie de ce que Flare connaît n'a pas sa place dans ces langages de requête : les **sessions** de l'application cliente derrière un plantage, les **groupes d'erreurs** et leur état de suivi, la **santé des versions** (release health) par version, et les **releases** que vous avez marquées. Le plugin de source de données Flare les lit, pour les placer à côté de vos autres panneaux.

Il s'exécute dans le navigateur et appelle Flare.Api via le proxy de sources de données de Grafana : c'est donc le **serveur Grafana** qui doit joindre Flare, et votre jeton n'atteint jamais le navigateur.

## Installer le plugin

Le plugin n'est pas encore dans le catalogue Grafana ; installez-le depuis le dépôt. Il nécessite Grafana 13.2 ou plus récent.

```bash
cd src/grafana-datasource
npm ci && npm run build
```

Copiez le dossier `dist/` obtenu dans le répertoire des plugins de Grafana sous le nom `apardev-flare-datasource`, autorisez son chargement sans signature, puis redémarrez Grafana :

```bash
cp -r dist /var/lib/grafana/plugins/apardev-flare-datasource
export GF_PLUGINS_ALLOW_LOADING_UNSIGNED_PLUGINS=apardev-flare-datasource
```

Avec Docker, montez `dist/` sur `/var/lib/grafana/plugins/apardev-flare-datasource` et définissez la même variable d'environnement.

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
| **Errors** | Les groupes d'exceptions classés par nombre d'occurrences, avec première et dernière apparition, services touchés et responsable | Service, top N |
| **Error issues** | L'état de suivi des groupes d'erreurs : statut (ouvert, résolu, ignoré, régressé), responsable, version de la régression et lien du ticket | Aucune |
| **Release health** | Une ligne par version de l'application : sessions, sessions plantées et en erreur, utilisateurs, et taux de sessions et d'utilisateurs sans plantage | Service |
| **Release health trend** | Une série par version dans le temps | Service, série (% de sessions sans plantage, sessions ou sessions plantées), largeur de bucket |
| **Releases** | Les déploiements que vous avez marqués, avec commit, URL, notes et nombres de nouvelles erreurs et de régressions | Service |

**Service** accepte un nom ou plusieurs séparés par des virgules, et une variable à valeurs multiples fonctionne. Vide, il désigne tous les services. Les requêtes de version et de santé des versions avec plusieurs services s'exécutent une fois par service et ajoutent une colonne ou un label Service.

## Variables de modèle

Créez une variable de type **Query**, choisissez la source de données Flare et indiquez ce qu'elle liste : les services ayant des sessions, les versions d'application ayant des sessions (éventuellement pour un service), les services ayant des erreurs, ou les versions de release. Utilisez la variable dans le champ Service d'un panneau, par exemple `$service`.

## Afficher les déploiements sur un panneau

Ajoutez une requête d'annotation sur la source de données Flare avec la requête **Releases**. Les réglages d'annotation de Grafana associent les champs `time`, `title` et `text`, que la requête fournit : une ligne en pointillés marque chaque déploiement. Pour des marqueurs de release sur des panneaux Prometheus, utilisez les métriques `flare_release_*` et les métriques d'annotation décrites dans [le guide de l'API Prometheus](query-with-prometheus-api.fr.md#annotations).

## Limites

- **Pas d'alertes, de tableaux de bord publics ni d'expressions côté serveur.** Les requêtes s'exécutent dans le navigateur ; Grafana indique donc l'alerting comme « Not supported » pour cette source. Pour alerter sur la santé des versions, utilisez les métriques `flare_release_*` via la source Prometheus, ou les [règles d'alerte](../../tutorials/first-alert.fr.md) de Flare.
- **7 jours au plus par requête.** Une plage plus large est réduite à ses 7 derniers jours et le panneau le signale.
- **500 sessions au plus par requête**, les plus récentes. Réduisez le service, la version ou la plage quand le panneau avertit que la liste a été tronquée.
- **La santé des versions suit la définition de session de Flare** : une session est plantée quand elle contient un `app.unhandled_exception` échappé, et les utilisateurs sont les valeurs distinctes de `user.id`, comme dans la vue Releases de Flare.

Notes de conception : [ADR-0254](../../../docs-internal/adr/0254-grafana-data-source-plugin.md).
