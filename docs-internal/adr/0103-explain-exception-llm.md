# ADR-0103: "Explain this exception" LLM action

Status: accepted

## Context

Stack frames link to source and show the throw site inline (ADR-0095, ADR-0096). The next step is
asking a model to explain the exception. That sends code and error text to a third party, so it
needs guard rails; the roadmap's constraints for every AI feature are: off by default, bring your
own model, redaction before anything leaves the box, a record of what was sent, a bounded token
budget, and never blocking anything else.

## Decision

- **Off by default, config-only.** `Ai__Enabled`, `Ai__Endpoint`, `Ai__Model`, `Ai__ApiKey`
  (plus `Ai__MaxInputChars`, `Ai__MaxOutputTokens`, `Ai__TimeoutSeconds`) on `Flare.Api`. No UI or
  database row: this is a deployment decision about data leaving the host, so it belongs with the
  operator, like `Query__*`. `GET /api/ai/status` tells the dashboard whether to show the action.
- **Bring your own model.** Any OpenAI-compatible `POST {Endpoint}/chat/completions` (OpenAI,
  Ollama, vLLM, LiteLLM). One non-streaming call; no provider SDK.
- **Redaction before send** (`AiRedactor`): JWTs, bearer/basic tokens, URL credentials,
  `password=`/`token=`/`apiKey=`-style assignments (incl. JSON), private-key blocks, emails, IPv4
  addresses, and long digit-bearing tokens. Applied to the type, message, stack trace and source.
  It is pattern matching, not a guarantee, and the docs say so.
- **The server builds the prompt.** The client sends the exception and a frame reference; the API
  fetches the source itself through `ISourceSnippetService` (so the ADR-0096 bounds and the repo
  token stay server-side) and never accepts client-supplied source text. A source failure drops the
  source, not the request.
- **Bounded.** The prompt is capped at `MaxInputChars` (stack trace first, source gets the rest),
  `max_tokens` is `MaxOutputTokens`, the call has its own timeout, and redirects are not followed.
  The standard resilience handler is removed for this client: its 10 s attempt timeout would cut
  off a slow model and its retries would re-send a non-idempotent request.
- **Recorded.** The redacted prompt is logged at Debug and its size, model and whether source was
  included at Information; the request is in the audit log (ADR-0079) as `ai-explain-exception`.
- **On demand, plain text.** A button per occurrence, never automatic. The answer is rendered as
  plain text, not markdown/HTML.

## Consequences

- Redacted-but-still-sensitive code can reach a hosted model if an operator points `Ai__Endpoint`
  at one; a local Ollama keeps everything on the box.
- Prompts are in the log only at Debug; there is no durable per-request store of what was sent.
  The audit-log entry records who asked and about which service, not the prompt.
- The AI constraints now exist in code; the incident-summary and natural-language-filter items can
  reuse `AiOptions`, `AiRedactor` and the client.
