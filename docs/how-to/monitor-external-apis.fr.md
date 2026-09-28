# Comment surveiller les API externes

La page **External APIs** de Flare liste tous les hôtes externes qu'appellent
vos services, comme Stripe, Twilio ou l'API d'une autre équipe. Pour chaque
domaine, vous voyez le débit, le taux d'erreur et la latence, puis le détail
par endpoint, les codes de statut, les erreurs les plus fréquentes et les
services qui émettent les appels.

La page s'appuie sur les spans client que vos applications envoient déjà.
Flare n'a besoin ni d'un agent ni d'une modification de l'ingestion, et la
page fonctionne sur des spans stockés avant que vous ne l'ouvriez.

## Prérequis

- Une instance Flare qui reçoit les traces de vos applications.
- Des appels HTTP ou gRPC sortants instrumentés avec OpenTelemetry. Les
  spans doivent être des spans `CLIENT` portant `server.address`
  (conventions sémantiques actuelles), `net.peer.name` (anciennes
  conventions) ou une URL complète (`url.full` ou `http.url`).

## Envoyer des spans client

Pour `HttpClient`, ajoutez l'instrumentation HTTP à votre traceur :

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());
```

Dirigez l'exportateur vers le point de terminaison OTLP de Flare, comme
d'habitude. Si votre projet utilise le modèle de service defaults de .NET
Aspire, l'instrumentation de `HttpClient` est déjà active. Les clients gRPC
basés sur `HttpClient` (`Grpc.Net.Client`) sont couverts par la même
instrumentation.

## Lire la page External APIs

Ouvrez **External APIs** dans la barre de navigation. Chaque ligne
correspond à un domaine :

| Colonne | Signification |
|---|---|
| Domain | `server.address`, sinon `net.peer.name`, sinon l'hôte de l'URL. |
| Port | Les ports appelés : `server.port`, sinon `net.peer.port`, sinon le port de l'URL, sinon 443 pour `https` et 80 pour `http`. Cinq au plus sont affichés. |
| Rate | Appels par seconde sur la fenêtre. Survolez pour voir le total. |
| Error rate | Part des appels dont le statut du span est `Error`. |
| p95 / p99 | Percentiles de durée des appels, mesurés côté appelant. |
| Endpoints | Nombre de paires méthode/endpoint distinctes appelées sur ce domaine. |
| Last seen | Début du dernier appel de la fenêtre. |
| Services | Nombre de vos services qui l'ont appelé. |

Utilisez **Calling service** pour n'afficher que les appels d'un service,
et le sélecteur de fenêtre pour choisir entre 5 minutes et 24 heures. La
page se charge à la demande ; sélectionnez **Refresh** pour la mettre à
jour.

Les appels de base de données (`db.system` renseigné) et de messagerie
(`messaging.system` renseigné) ne sont pas listés, même s'ils portent
`server.address`. Ils apparaissent dans l'onglet **Database calls** de la
ventilation des services et sur la
[page Messaging](monitor-message-queues.fr.md).

### Explorer un domaine

Sélectionnez un domaine pour ouvrir son détail :

- **Over time** : trois graphiques sur la fenêtre : requêtes par
  seconde, erreurs et latence p95. Sélectionnez un point pour ouvrir
  **Traces** sur les appels de cet intervalle. Depuis le graphique p95,
  les traces sont triées de la plus lente à la plus rapide. Un intervalle
  dure environ une minute pour une fenêtre d'une heure et suit la fenêtre
  (10 secondes pour 5 minutes, 24 minutes pour 24 heures).
- **Status codes** : appels par code de statut HTTP
  (`http.response.status_code`, sinon `http.status_code`).
- **Endpoints** : débit, taux d'erreur, p50/p95/p99 et dernier appel par
  méthode et endpoint. Chaque colonne est triable.
- **Top errors** : appels en échec regroupés par endpoint, code de statut
  et `error.type`, avec un exemple de message de statut. Une ligne sans
  code de statut n'a jamais reçu de réponse, par exemple à cause d'un
  délai dépassé ou d'une connexion refusée.
- **Calling services** : vos services qui appellent ce domaine, et les
  performances de leurs appels.

Sélectionnez un nom de domaine, un endpoint, un code de statut, un taux
d'erreur, une ligne d'erreur ou un service pour ouvrir **Traces**, filtré
sur les traces contenant des appels correspondants. Le filtre est une
requête structurelle (**Structure A**), car l'appel est un span enfant de
chaque trace. Voir
[Trouver des traces par leur structure](find-traces-by-structure.fr.md).

### Comment les endpoints sont nommés

Flare nomme un endpoint avec la première règle qui s'applique :

1. L'attribut `url.template` du span, tel quel. Seules certaines
   instrumentations le renseignent.
2. Le chemin de `url.full` (ou `http.url`), où chaque segment qui
   ressemble à un identifiant est remplacé par `{id}`. Un segment est
   considéré comme un identifiant s'il ne contient que des chiffres, s'il
   s'agit d'un UUID ou d'une chaîne hexadécimale d'au moins 16 caractères,
   ou de tout segment d'au moins 16 caractères contenant un chiffre. Par
   exemple, `/v1/customers/cus_NffrFeUfNV2Hib` devient
   `/v1/customers/{id}`. La chaîne de requête est ignorée.
3. `rpc.service/rpc.method`, pour gRPC.
4. Le nom du span.

La règle 2 est une heuristique. Un identifiant qui n'y correspond pas,
comme un slug court ou un nom d'utilisateur, reste dans le chemin, et
chaque valeur devient son propre endpoint. Si la liste devient illisible,
renseignez `url.template` dans votre instrumentation.

## La ventilation des services utilise les mêmes domaines

Dans **Traces > Services > Map**, cliquer sur un service ouvre la
ventilation de ce qu'il appelle. Son onglet **External calls** regroupe
par `peer.service` quand un span le renseigne, et sinon par domaine,
déterminé comme ci-dessus. Avant ce changement, les appels `HttpClient`,
qui ne renseignent jamais `peer.service`, n'y apparaissaient pas du tout.
Seuls les appels stockés après la mise à jour sont regroupés par domaine
dans les données pré-agrégées de la ventilation ; les lignes plus
anciennes gardent leur regroupement par `peer.service` jusqu'à leur
expiration.

## Les hôtes externes sur la Service Map

**Traces > Services > Map** affiche aussi les hôtes externes, sous forme
de nœuds feuilles avec une icône de globe et la mention **External**. Un
hôte obtient un nœud quand vos services l'appellent et qu'aucun span
instrumenté ne répond à l'appel. Un appel vers l'un de vos propres
services instrumentés reçoit la réponse du span serveur de ce service : il
reste donc une arête de service à service normale et ne devient jamais un
nœud de nom d'hôte. Les appels qui définissent `peer.service` apparaissent
toujours sous ce nom, comme avant.

Un nœud d'hôte compte les appels qu'il reçoit, qui sont aussi comptés dans
le nœud du service appelant. Sélectionnez-le pour ouvrir l'hôte sur la page
**External APIs** avec la même fenêtre. La Map affiche au plus 50 arêtes
appelant-hôte, les plus actives d'abord.

Seuls les appels stockés après la mise à jour apparaissent : la fenêtre de
24 heures de la Map se remplit au cours du premier jour. Avec des filtres
d'attributs de ressource, la Map trouve les hôtes en parcourant directement
`spans`, ce qui est plus lent sur un gros volume.

## Dépannage

- **La page est vide.** Vérifiez que vos appels produisent des spans
  `CLIENT` : ouvrez une trace dans **Traces** et cherchez un span enfant
  de type `Client` avec un attribut `server.address`. S'il n'y en a pas,
  l'instrumentation HTTP n'est pas enregistrée auprès du traceur qui
  exporte vers Flare.
- **Le lien vers les traces d'un domaine ne trouve rien.** Le lien filtre
  sur `server.address`. Les spans qui ne nomment leur hôte que via
  `net.peer.name` ou l'URL sont comptés sur la page, mais le lien ne les
  trouve pas.
- **Les appels vers mes propres services sont listés.** C'est normal. La
  page liste tous les hôtes qu'appellent vos services, internes compris.
  La Service Map montre la vue de service à service.
- **Un service interne apparaît comme hôte sur la Service Map.** Son span
  serveur manquait pour ces appels : il a été écarté par l'échantillonnage,
  il n'était pas encore écrit (appels des dernières secondes de la
  fenêtre), ou le service appelé n'est pas instrumenté.
