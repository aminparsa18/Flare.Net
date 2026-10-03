# Comment relier les traces de pile d'exception à votre code source

Dans la page **Exceptions**, ouvrez un groupe pour voir ses occurrences
d'exemple. Dès qu'un dépôt de sources est configuré pour un service,
l'emplacement de fichier de chaque frame de la pile devient un lien vers ce
fichier et cette ligne dans votre dépôt, au commit à partir duquel le service
a été construit.

## Prérequis

- Des traces de pile avec des emplacements de fichiers. .NET affiche
  `in /src/File.cs:line 42` quand le build embarque des PDB portables, ce qui
  est le cas par défaut.
- Vous êtes administrateur, ou l'authentification est désactivée. Les
  lecteurs et les membres voient les liens mais ne peuvent pas modifier la
  configuration.

## Marquer le build avec son commit

Flare choisit le commit dans les attributs de ressource du span, dans cet
ordre :

1. `vcs.ref.head.revision`
2. `vcs.revision`
3. `service.version`

Pour .NET, le plus simple est SourceLink, qui place le commit dans la version
informationnelle (`1.2.3+abc1234`) ; Flare lit la partie après le `+`. Si
`service.version` vaut simplement `1.2.3`, elle ne désigne aucun commit et
Flare se rabat sur la branche ou le tag par défaut configuré ci-dessous.

## Configurer le dépôt

1. Dans la ligne d'une occurrence, cliquez sur l'icône de lien à côté de
   **Show stack trace**.
2. Choisissez l'hébergeur : GitHub, GitLab ou Azure DevOps.
3. Saisissez l'URL du dépôt, par exemple `https://github.com/acme/shop`. Pour
   Azure DevOps : `https://dev.azure.com/org/project/_git/repo`.
4. Si vous le souhaitez, indiquez une branche ou un tag de repli, par exemple
   `main`.
5. Définissez le **préfixe de chemin** si le chemin de build n'est pas relatif
   au dépôt. C'est le répertoire dans lequel le build s'est exécuté, que Flare
   retire de chaque frame. Par exemple `/src/` dans un build Docker, ou
   `/home/runner/work/shop/shop/` sur GitHub Actions.
6. Cliquez sur **Save**.

Les builds avec `<Deterministic>` et `ContinuousIntegrationBuild` réécrivent
déjà les chemins pour qu'ils commencent par `/_/` ; Flare le retire sans
préfixe.

## Afficher les lignes en cause directement

Quand une frame renvoie vers votre dépôt, un bouton **Show source** apparaît sous
la trace de pile. Il affiche les lignes autour du point de levée (la première
frame pouvant être liée).

L'API de Flare récupère le fichier chez votre hébergeur de dépôt ; un dépôt privé
exige donc un jeton d'accès en lecture seule. Saisissez-le dans le même formulaire
de l'icône de lien :

- GitHub : un jeton à granularité fine avec accès en lecture à **Contents**.
- GitLab : un jeton avec la portée `read_repository`.
- Azure DevOps : un jeton d'accès personnel avec **Code (Read)**.

Le jeton est en écriture seule : Flare ne l'affiche plus, et laisser le champ vide
conserve le jeton enregistré. Les dépôts publics fonctionnent sans jeton. Flare ne
suit pas les redirections, ignore les fichiers de plus de 2 Mo et met un fichier en
cache pendant 10 minutes.

## Quand une frame n'est pas liée

Flare laisse la frame en texte brut plutôt que de deviner :

- le chemin est absolu et ne commence pas par le préfixe configuré ;
- l'emplacement est un simple nom de fichier sans répertoire, comme les
  frames Java ;
- l'occurrence n'a pas de commit et le service n'a pas de branche de repli.

## Voir aussi

- [Décision d'architecture : ADR-0095](../../docs-internal/adr/0095-exception-source-links.md)
- [ADR-0096](../../docs-internal/adr/0096-inline-exception-source.md)
