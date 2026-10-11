# Comparer deux déploiements d'un service

Flare compare deux versions d'un service pour répondre à « mon déploiement a-t-il cassé quelque chose ? » sur un seul écran. Il ne faut aucune instrumentation supplémentaire en dehors d'une version sur votre service.

## Définir la version

Définissez l'attribut de ressource `service.version`. En .NET, avec OpenTelemetry :

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("orders-api", serviceVersion: "2.4.0"));
```

N'importe quelle chaîne convient (version sémantique, numéro de build, hash de commit). Les versions sont lues dans vos spans et vos logs : un nouveau déploiement apparaît dès qu'il envoie de la télémétrie.

## Comparer

1. Ouvrez **Traces**, puis l'onglet **Services**.
2. Ouvrez un service (cliquez sur son nœud dans la vue **Map**).
3. La section **Comparaison de déploiements** se trouve sous les constats de santé du runtime. Par défaut, elle compare la version la plus récente à celle vue pour la première fois juste avant elle. Les deux sélecteurs permettent de choisir une autre paire.

La comparaison remonte sur 7 jours. Si le service n'a envoyé qu'une seule version sur cette période, ou si rien n'a de `service.version`, la section l'indique.

| Partie | Ce qu'elle affiche |
| --- | --- |
| Endpoints | Les spans serveur et consumer par nom : appels, taux d'erreur et latence p95 sous chaque version. **Dégradé** signale un taux d'erreur en hausse d'un point de pourcentage ou plus, ou un p95 en hausse d'un quart et d'au moins 20 ms. **Nouveau** et **Disparu** signalent les endpoints servis par une seule version |
| Nouveaux types d'exception | Les types d'exception vus sous la version courante et jamais sous la version de référence |
| Nouvelles dépendances sortantes | Les hôtes externes et systèmes de base de données appelés sous la version courante et jamais sous la version de référence |
| Nouveaux motifs de log | Les motifs de log Drain vus sous la version courante et jamais sous la version de référence |

Les endpoints, les types d'exception et le lien **Logs de cette version** ouvrent l'explorateur correspondant, limité à ce service et à cette version, sur la fenêtre pendant laquelle la version courante a été vue.

## Limites

- « Vue pour la première fois » se mesure dans la fenêtre de 7 jours : une version de référence qui a cessé d'émettre avant son début fait paraître tout nouveau.
- La télémétrie sans `service.version` n'est pas comparée.
- Les résultats sont calculés à partir de vos données à l'ouverture de la section. L'API sous-jacente est `POST /api/services/version-comparison`.
