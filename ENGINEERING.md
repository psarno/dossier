# Engineering Notes

This document collects the implementation details that are useful to engineers and reviewers. The root [README.md](README.md) stays focused on the investigative use case.

---

## Stack

- Angular 22 SSR on the client.
- ASP.NET on the API.
- SQLite for the content store and full-text search.
- LLM-assisted ingestion with a deterministic source path.

The repository is intentionally built to show a modern SSR stack rather than a legacy client-side app.

---

## Ingestion pipeline

The pipeline (`api/Services/PipelineService.cs`) is the heart of the system. An admin uploads markdown; everything downstream is automatic. It is deliberately built so that a failed or cancelled run cannot corrupt the live site - the running database is never mutated in place.

1. Archive the raw uploads so the input is recoverable.
2. Parse the markdown structure into sections, entries, and metadata.
3. Enrich summary sections with an LLM for slug, title, and tag validation.
4. Resolve and dedupe cross-references against final section slugs.
5. Generate deterministic source artifacts.
6. Stage everything in a separate SQLite file.
7. Validate the staged DB before publishing.
8. Atomically swap the live database into place.
9. Optionally generate the relationship graph after the content update has already succeeded.

The pipeline runs fire-and-forget under `CancellationToken.None` so a dropped client connection does not abort work, and a single-entry lock prevents concurrent uploads from racing.

---

## LLM cache

LLM calls are cached by content hash, not by position. The cache key is:

```text
SHA256(section heading + body) | provider | model | prompt-version
```

That means re-uploading a document where only a few sections changed re-bills only those sections. Changing the model or prompt version invalidates the right entries without a manual flush.

The provider is selected through `AI_PROVIDER`, with Anthropic and OpenRouter supported. The subject string and tag vocabulary are injected into prompts from `research.config.json`.

---

## Runtime config

`research.config.json` is loaded once at API startup and served read-only at `GET /api/config`. The same file drives:

- the parser's recognized tag set,
- the tags embedded in LLM prompts,
- the front-end branding and labels,
- the graph hub entity and aliases.

That keeps the parser, prompts, and UI aligned from one source of truth.

---

## SSR and hydration

The client uses Angular SSR with runtime `/api/*` calls, including `/api/config`. The key implementation is `DataService` in `client/src/app/services/data.service.ts`:

- Render-on-load reads use `httpResource` signals.
- Those reads participate in Angular's HTTP transfer cache.
- The server serializes the data into the HTML.
- The client hydrates from the cached payload without refetching.

That gives a real server-rendered page with no placeholder flash and no second fetch for the initial render.

A server-side HTTP interceptor rewrites relative `/api/*` calls to the internal API base URL so the same code path works during SSR and hydration.

---

## Angular 22 features in use

- `httpResource` plus the transfer cache for render-on-load data.
- Signal-based state with `OnPush` components.
- `@Service` for singleton services.
- Signal Forms for the admin upload form.
- `injectAsync` for code-splitting the graph route.
- Incremental hydration via `provideClientHydration(withEventReplay())`.

---

## Markdown rendering

`SafeHtmlPipe` turns stored markdown into display HTML and applies evidentiary tag presentation in a post-process pass. The rendered content originates from the internal pipeline, so the trust boundary is drawn at ingestion rather than at render time.

---

## Design tradeoffs

- Atomic file swap over in-place migration.
- Content-addressed AI caching over time-based caching.
- Runtime config over build-time config.
- Non-fatal graph extraction that runs last.
- Deterministic source extraction separate from the AI path.

---

## Further reading

- [source-docs/README.md](source-docs/README.md) for the markdown structure.
- [client/README.md](client/README.md) for the default Angular CLI project notes.
