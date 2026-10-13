# Clés de configuration du live-tail

Clés de configuration exactes des points de terminaison WebSocket live-tail (`GET /api/logs/tail`, statistiques d'hôte, graphe de ressources), à définir sur `Flare.Api`.

| Clé | Variable d'environnement | Type | Défaut | Signification |
|---|---|---|---|---|
| `LiveTail:PollDelay` | `LiveTail__PollDelay` | timespan | `250ms` | Attente entre deux lectures du flux Redis quand rien de nouveau n'est arrivé. |
| `LiveTail:ReadBatchSize` | `LiveTail__ReadBatchSize` | int | `1000` | Nombre maximal d'entrées lues par lecture du flux. |
| `LiveTail:SubscriberChannelCapacity` | `LiveTail__SubscriberChannelCapacity` | int | `500` | Tampon par connexion. Un client trop lent perd des événements au-delà de cette limite au lieu de ralentir les autres. |
| `LiveTail:MaxClientMessageBytes` | `LiveTail__MaxClientMessageBytes` | int | `65536` | Plus grand message qu'un client peut envoyer (somme des fragments). Un message plus grand ferme le socket avec le code 1009. |
| `LiveTail:MaxConnectionsPerUser` | `LiveTail__MaxConnectionsPerUser` | int | `10` | Sockets live-tail simultanés par utilisateur. Les appelants anonymes partagent un seul compteur. Au-delà, la mise à niveau est refusée avec 429. |
| `LiveTail:MaxConnections` | `LiveTail__MaxConnections` | int | `200` | Sockets live-tail simultanés, tous utilisateurs confondus. Au-delà, la mise à niveau est refusée avec 429. |
| `LiveTail:MaxSocketLifetime` | `LiveTail__MaxSocketLifetime` | timespan | `00:05:00` | Durée maximale d'ouverture d'un socket avant que le serveur le ferme et que le tableau de bord se reconnecte. Zéro ou négatif désactive la limite. Voir [Durée de vie du socket](#durée-de-vie-du-socket). |
| `LiveTail:MaxFilterItems` | `LiveTail__MaxFilterItems` | int | `100` | Nombre maximal d'éléments dans un filtre : services, sévérités, noms de portée, filtres d'attributs et de corps JSON, et leurs valeurs `in`/`not-in` cumulées. |
| `LiveTail:MaxFilterStringLength` | `LiveTail__MaxFilterStringLength` | int | `1024` | Longueur maximale de toute chaîne d'un filtre (texte de recherche, valeurs, clés, chemins). |
| `LiveTail:MaxRegexPatterns` | `LiveTail__MaxRegexPatterns` | int | `10` | Nombre maximal d'opérateurs `regex`/`not-regex` dans un filtre. |
| `LiveTail:MaxRegexPatternLength` | `LiveTail__MaxRegexPatternLength` | int | `256` | Longueur maximale d'un motif regex. |
| `LiveTail:RegexMatchTimeout` | `LiveTail__RegexMatchTimeout` | timespan | `50ms` | Délai d'expiration par correspondance. Un dépassement compte comme une absence de correspondance. |

## Vérification de l'origine

Les navigateurs n'appliquent pas CORS aux WebSockets : `Flare.Api` rejette donc avec 403 une mise à niveau dont l'en-tête `Origin` n'est pas listé dans `Cors:AllowedOrigins` (variables `Cors__AllowedOrigins__0`, `__1`, ...). Les requêtes sans en-tête `Origin`, comme celles de la CLI `flare`, sont acceptées. Si le tableau de bord est servi depuis une origine absente de cette liste, le live-tail ne se connecte pas.

## Durée de vie du socket

L'autorisation n'est vérifiée qu'à l'ouverture du socket. `LiveTail__MaxSocketLifetime` ferme chaque socket live-tail, de statistiques d'hôte et de graphe de ressources après cette durée et le tableau de bord se reconnecte : une session révoquée, un utilisateur désactivé, un jeton d'accès personnel expiré ou une appartenance de projet retirée cessent donc de recevoir le flux dans cette fenêtre.

## Motifs regex

Les regex du live-tail s'exécutent en temps linéaire (`RegexOptions.NonBacktracking`). Les assertions avant/arrière et les rétro-références sont rejetées à l'envoi du filtre : une vue enregistrée qui les utilise cesse de fonctionner en live-tail et doit être réécrite. Les requêtes de recherche et d'agrégation ne sont pas concernées.

Les valeurs par défaut supposent un socket live-tail par onglet de tableau de bord ouvert, plus un socket de statistiques d'hôte et un de graphe de ressources sur la page Resources : 10 par utilisateur couvre donc un utilisateur avancé avec plusieurs onglets. Le socket résiduel d'un onglet fermé libère sa place quand le serveur détecte la déconnexion ou à `MaxSocketLifetime`. Augmentez `MaxConnectionsPerUser` si des utilisateurs reçoivent 429 à la reconnexion, et `MaxConnections` au-delà d'environ 20 utilisateurs simultanés. Les limites de filtre sont bien supérieures à ce que produit le tableau de bord ; ne les augmentez que pour des clients scriptés. Les statistiques d'hôte et le graphe de ressources appliquent aussi `MaxConnectionsPerUser`, `MaxConnections` et `MaxClientMessageBytes`, chaque point de terminaison avec son propre compteur, de sorte qu'ils ne privent pas le live-tail de connexions. Leurs trames entrantes sont ignorées : seuls le nombre de sockets et la taille des messages comptent.

## Voir aussi

- [Configuration de l'authentification](authentication-config.fr.md)
