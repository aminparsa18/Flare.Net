# Comment surveiller les appels LLM

La page **LLM** de Flare montre comment vos services utilisent les modèles de
langage. Pour chaque fournisseur et modèle, vous voyez le débit d'appels, le
taux d'erreur, la latence et les tokens d'entrée et de sortie, et un clic
ouvre les traces correspondantes.

La page s'appuie sur les spans que vos applications envoient déjà. Flare n'a
besoin ni d'un agent ni d'une modification de l'ingestion, et la page
fonctionne sur des spans stockés avant que vous ne l'ouvriez.

## Prérequis

- Une instance Flare qui reçoit les traces de vos applications.
- Des appels de modèle instrumentés selon les conventions GenAI d'OpenTelemetry.
  Chaque appel doit être un span dont `gen_ai.operation.name` vaut `chat`,
  `text_completion`, `generate_content` ou `embeddings`, ou sans opération mais
  avec un `gen_ai.request.model`.

## Envoyer les spans d'appel de modèle

### Microsoft.Extensions.AI

Enveloppez votre client de chat avec `UseOpenTelemetry()` et abonnez le tracer
au nom de source que vous lui donnez :

```csharp
IChatClient client = new ChatClientBuilder(innerClient)
    .UseOpenTelemetry(loggerFactory, sourceName: "Microsoft.Extensions.AI")
    .Build();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Microsoft.Extensions.AI")
        .AddOtlpExporter());
```

Les générateurs d'embeddings fonctionnent de la même façon, via
`EmbeddingGeneratorBuilder.UseOpenTelemetry()`.

### Semantic Kernel

Les connecteurs de Semantic Kernel n'émettent ces spans que si vous activez son
interrupteur de diagnostics expérimental, et votre tracer doit s'abonner à ses
sources :

```csharp
AppContext.SetSwitch("Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnostics", true);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Microsoft.SemanticKernel*")
        .AddOtlpExporter());
```

L'ancien `ITextEmbeddingGenerationService` n'émet aucun span. Pour les
embeddings, utilisez `IEmbeddingGenerator` de Microsoft.Extensions.AI.

Pointez l'exportateur vers le point de terminaison OTLP de Flare comme d'habitude.

## Lire la page LLM

Ouvrez le menu **⋯** en haut à droite et choisissez **LLM**. Chaque ligne
correspond à un fournisseur et un modèle :

| Colonne | Signification |
|---|---|
| Model | `gen_ai.request.model`, sinon `gen_ai.response.model`. |
| Provider | `gen_ai.provider.name`, sinon l'ancien `gen_ai.system`. |
| Rate | Appels par seconde sur la fenêtre. Survolez pour voir le total. |
| Error rate | Part des appels dont le statut du span est `Error`. |
| p95 / p99 | Percentiles de durée des appels, tels que l'appelant les mesure. |
| Input tokens / Output tokens | Sommes de `gen_ai.usage.input_tokens` et `gen_ai.usage.output_tokens`. Les anciens noms `prompt_tokens` et `completion_tokens` sont lus aussi. |
| Last seen | Le début du dernier appel dans la fenêtre. |
| Services | Combien de vos services ont appelé le modèle. |

Utilisez **Calling service** pour n'afficher que les appels d'un service, et le
sélecteur de fenêtre pour choisir de 5 minutes à 24 heures. La page se charge à
la demande ; sélectionnez **Refresh** pour l'actualiser. **View traces** ouvre
l'explorateur de traces filtré sur les appels à ce modèle.

## Ce que la page laisse de côté

- **Les spans d'agent et d'outil.** Les spans `invoke_agent`, `create_agent` et
  `execute_tool` ne sont pas comptés. Un span d'agent peut reprendre les tokens
  des appels de modèle qu'il contient ; les compter tous doublerait les totaux.
- **Le coût facturé.** **Est. cost** multiplie le nombre de tokens par un prix
  au million de tokens : un tarif public intégré pour les modèles courants
  d'OpenAI, Anthropic et Gemini (les versions datées comme
  `gpt-4o-2024-08-06` prennent le prix de leur famille), ou celui qu'un
  administrateur définit avec le crayon à côté du coût. Les modèles sans prix
  affichent **No price**. Flare ne voit pas les tokens en cache ni remisés :
  considérez le chiffre comme une estimation haute.
- **La correspondance sur le modèle demandé.** **View traces** filtre sur
  `gen_ai.request.model` : un appel qui n'a renseigné que
  `gen_ai.response.model` apparaît dans le tableau mais pas dans cette liste de
  traces.
- **Les prompts et les réponses.** Leur contenu n'est pas agrégé. Ouvrez une
  trace pour voir ce que votre instrumentation a enregistré sur le span.
