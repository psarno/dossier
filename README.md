# Dossier

**A markdown → SQLite → server-rendered web pipeline for building searchable, evidence-tagged research records on any subject.**

You write three structured markdown documents about a subject - a person, an organization, a topic - and edit one config file. The pipeline parses them, enriches each section with an LLM, builds a full-text-searchable SQLite database, and serves it through a server-side-rendered Angular front end. Every claim carries a primary source and an evidentiary-weight tag. The result is a public record that is *too well-sourced to ignore and too organized to bury.*

This repository is the generalized, open-source version of the engine behind a live investigative site. It ships seeded with a worked example so you can stand the whole thing up, see it populated, and then swap in your own subject by editing a single JSON file and three markdown documents.

---

## Who this is for

- **Researchers / investigative journalists** who want to publish a sourced record without touching code - start at [The evidentiary model](#the-evidentiary-model) and [Getting started](#getting-started). You will spend your time in markdown and one JSON file, never in C# or TypeScript.

- **Engineers** evaluating the architecture - start at [Architecture](#architecture). The interesting parts are the zero-downtime database swap, the content-addressed LLM cache, and the SSR hydration model that renders config- and data-driven pages with no client-side flash.

---

## What you get

- **A searchable evidence database** - SQLite with FTS5 full-text search across every section and named entity.
- **Evidentiary tagging** - five locked categories (Confirmed → Anomalous) that make the *weight* of every claim visible at a glance, instead of forcing the reader to re-derive it on each read.
- **Three coordinated document types** - a thematic summary, a names/entity index with prominence tiers, and an analytical framework.
- **Server-side rendering** - pages arrive fully formed from the server (no spinner, no layout shift, crawlable), then hydrate without re-fetching what the server already sent.
- **A deterministic source catalog** - citations are extracted from your markdown into a structured, reviewable artifact, separate from the AI path.
- **An optional relationship graph** - an LLM-extracted node/edge network centered on a configurable hub entity.
- **One-file reconfiguration** - branding, subject, document labels, tag taxonomy, and the graph hub all live in `research.config.json`, served to the front end at runtime. No rebuild required to change them.
- **Container-native deploy** - two Dockerfiles, portable to anything that runs containers.

### The ingestion pipeline

The pipeline (`api/Services/PipelineService.cs`) is the heart of the system. An admin uploads markdown; everything downstream is automatic. It is deliberately built so that **a failed or cancelled run cannot corrupt the live site** - the running database is never mutated in place.

1. **Archive** - raw uploads are timestamped and written to disk before anything is parsed, so the input is always recoverable.
2. **Parse** - `MarkdownParser` splits the summary on `##`, the names index on `###` with Tier 1/2 boundary detection, and the framework on its title. Version strings are lifted from filenames.
3. **Enrich** - each summary section is sent to an LLM for a clean slug, a normalized display title, and a validated tag list (see [LLM enrichment](#llm-enrichment-and-the-content-addressed-cache)). Runs **five concurrent** with a semaphore; per-section failures fall back to parsed values rather than aborting the run.
4. **Resolve & dedupe** - entity cross-references are resolved against the *final* section slugs (exact match, then shortest containing match, then dropped - a missing link beats a broken one). Slug collisions are de-duplicated deterministically.
5. **Generate sources** - the deterministic source catalog extracts citations into JSON + markdown artifacts (no LLM in this path).
6. **Stage** - everything is written to a **separate staging database file**, not the live one.
7. **Validate** - the staging DB must pass minimum section/entity counts, non-empty bodies, a populated FTS index, and present metadata. **Fail here and the live site is untouched.**
8. **Atomic swap** - connection pools are cleared, WAL/SHM sidecars are reconciled, the live DB is copied to a `.bak`, and the staged file is moved into place. The swap operates on a single concrete file to avoid WAL handoff hazards. A one-step `rollback` restores the `.bak`.
9. **Graph (optional, non-fatal)** - if requested, relationship extraction runs *after* the swap has already succeeded, so a graph failure can never fail the content update.

The pipeline runs fire-and-forget under a `CancellationToken.None` so a dropped client connection never aborts an in-flight enrichment, and it is guarded by a single-entry lock so two uploads can't race.

### LLM enrichment and the content-addressed cache

LLM calls are the slow, costly part of ingestion, so they are cached by **content hash, not by position**. The cache key is:

```
SHA256(section heading + body) | provider | model | prompt-version
```

Re-uploading a document where only a few sections changed re-bills only those sections; everything else is a cache hit. Changing the model or bumping the prompt version invalidates cleanly without a manual flush. The cache lives in the database and is swapped atomically with the rest of the content.

The provider is pluggable behind `IAiClient` - Anthropic (Claude) and OpenRouter ship in the box, selected by `AI_PROVIDER`. The subject string and the tag vocabulary are injected into every prompt from `research.config.json`, so the model is always grounded in *your* subject and *your* taxonomy, never hardcoded.

### Runtime config, single-sourced

There is no build-time data injection anywhere in the front end. `research.config.json` is loaded once at API startup as a singleton and served read-only at `GET /api/config`. The same JSON drives:

- the markdown parser's recognized tag set,
- the tag list embedded in the LLM prompts,
- the front-end legend, nav brand, document labels, and page title,
- the graph's central hub entity and its aliases.

One edit to that file changes the parser, the prompts, and the UI together - they cannot drift out of sync, because they read the same source. Swapping subjects is editing JSON and markdown, then re-running the pipeline. No recompile.

### SSR and the no-flash hydration model

The client is Angular 22 with SSR via an Express server. Every view renders from runtime `/api/*` calls, including `/api/config` - there is no data baked into the bundle. The trick that makes this fast and flash-free is `DataService` (`client/src/app/services/data.service.ts`):

- Render-on-load reads are exposed as **`httpResource`** signals (`config`, `metadata`, `sections`, `entries`, `generatedSources`). Because `httpResource` issues ordinary `HttpClient` GETs, they participate in Angular's **HTTP transfer cache** (enabled by `provideClientHydration`).
- On the **server**, each response is serialized into the transfer cache and shipped inline with the HTML.
- On the **client**, the transfer-cached read **resolves synchronously on first render**, so the hydrated DOM is never torn down and re-fetched.

The result: the server renders the real, config-driven page, ships the data inline with the HTML, and the browser hydrates from that payload without a second round-trip. Components read every render-on-load value as a **signal**, so the title, branding, and tag legend are present in view-source - no placeholder flicker. This replaces the hand-rolled `TransferState` plumbing (`makeStateKey`/get/remove/set) the app previously used to do the same job by hand. Search is intentionally an always-fresh read - it is never given a manual transfer-state key.

A server-side HTTP interceptor rewrites relative `/api/*` calls to the internal API base URL, so the browser never sees the API host and the same component code works rendered or hydrated.

### Angular 22 front end

The client tracks the current Angular release (**22**, on TypeScript 6 and Node 22) and uses the modern idioms rather than legacy equivalents:

- **`httpResource` + the HTTP transfer cache** for all render-on-load data, replacing hand-rolled `TransferState` keys (see above).
- **Signal-based state and zoneless-style change detection** - every component runs **OnPush**; render-on-load values are read as signals end to end.
- **`@Service` decorator** for the singleton `DataService` (the v22 replacement for `@Injectable({ providedIn: 'root' })`).
- **Signal Forms** (`@angular/forms/signals`) for the admin upload form, including the cross-field "summary XOR names, or framework alone" rule as a declarative tree validator.
- **`injectAsync` code-splitting** in the `/connections` route: the large d3 vendor chunk is dynamically imported only when the relationship graph is actually instantiated, so it never weighs down the rest of the app.
- **Incremental hydration on by default** (`provideClientHydration(withEventReplay())`).

### Markdown rendering and the tag pipeline

`SafeHtmlPipe` (`client/src/app/pipes/safe-html.pipe.ts`) turns stored markdown into display HTML and does the evidentiary-tag presentation in a post-process pass: `` `[CONFIRMED]` `` code spans become styled badge spans, compound `[A][B]` tags are split, `[CONFIRMED …]` variants fall through a prefix rule, `*[Context: …]*` italics become analysis callouts, and `##`/`###` headings get slug `id` anchors for deep linking. Content originates solely from the internal pipeline, so `bypassSecurityTrustHtml` is used deliberately - see [Design decisions](#design-decisions-and-deliberate-tradeoffs).

### Data model

| Type | Source doc | Parse rule | Surfaced at |
|------|-----------|-----------|-------------|
| **Section** | Summary | one per `##` | `/summary`, `/summary/:slug` |
| **Section** | Framework | one per `##` under a single `#` title | `/framework` |
| **Entry** | Names index | one per `###`, split into Tier 1/2 by heading | `/names`, `/names/:slug` |
| **Metadata** | pipeline | versions, build time, source counts | `/api/metadata` |
| **Generated sources** | summary + names | deterministic citation extraction | `/api/generated-sources` |
| **Graph** | summary + names | LLM node/edge extraction (optional) | `/api/graph` |

Sections and entries each have a mirrored FTS5 table; search hits return ranked snippets with `<mark>` highlighting.

---

## The evidentiary model

The point of this tool is not to publish opinions - it is to publish a record where the *strength* of each claim is explicit and traceable. Every claim in your markdown carries one of five tags, wrapped in backticks (`` `[CONFIRMED]` ``) so they survive markdown rendering and become badges in the UI:

| Tag | Meaning |
|-----|---------|
| **Confirmed** | In primary documents, court records, or official filings. Directly verifiable against the source. |
| **Corroborated** | Assessed as credible across multiple independent reviews. A distinct investigative category, not "probably true." |
| **Documented Claim** | Stated in an official document (deposition, sworn testimony, filing) but not independently corroborated. |
| **Confirmed Govt Action** | A government action that is confirmed even where the underlying motive is unknown. |
| **Anomalous** | A confirmed fact that is procedurally or legally extraordinary on its face. The anomaly is visible in the record itself. |

The tags are configurable in label and description, but the five-category structure and their CSS colors are intentionally stable - a reader who learns the legend once can read any site built on this engine. **An item enters the record only when a specific source supports it and the underlying claim can be verified against that source.** Rumor and speculation are out of scope by construction.

You author claims and citations in plain markdown; the structure the parser expects is documented in [`source-docs/README.md`](source-docs/README.md).

---

## Repository layout

```
research.config.json          Single source of truth (gitignored - your copy)
research.config.example.json  Template config you copy from
source-docs/                  Your three markdown documents (+ a structure guide)
api/                          C# / ASP.NET minimal API, .NET 10
  Services/                   Pipeline, parser, AI clients, DB, validation, graph, sources
  Models/                     DTOs
  Dockerfile                  Multi-stage .NET image
client/                       Angular 22 SSR app
  src/app/services/           DataService (httpResource + transfer-cache hydration)
  src/app/pipes/              Markdown + evidentiary-tag rendering
  src/app/components/         home, sections, names, search, connections (graph), admin
  Dockerfile                  Multi-stage Node image
```

---

## Getting started

### Prerequisites

- **Node 22+** (Angular SSR build)
- **.NET 10+** (the API)
- **Docker** (for container deploy)
- An **Anthropic API key** *or* an **OpenRouter key** (exactly one - the app refuses both)

### 1. Clone and install

```bash
git clone <repo>
cd dossier
cd client && npm install && cd ..
```

### 2. Configure your subject

Copy the example config and edit it:

```bash
cp research.config.example.json research.config.json
```

`research.config.json` is gitignored - it is *your* deployment's data, not template content. Key fields:

```json
{
  "subject": "the subject described in plain language - injected into every LLM prompt",
  "branding": {
    "siteTitle": "Your Title",
    "navBrand": "Your Brand",
    "tagline": "Primary-source analysis of the public record.",
    "contactEmail": "you@example.org",
    "domain": "yourdomain.org"
  },
  "centralNode": {
    "id": "main-subject-slug",
    "aliases": ["Alternative", "Names", "For", "The Hub Entity"]
  },
  "documents": [
    { "type": "summary",   "label": "Summary Document",     "route": "/summary" },
    { "type": "names",     "label": "Names Index",          "route": "/names" },
    { "type": "framework", "label": "Analytical Framework", "route": "/framework" }
  ],
  "tags": [ /* five entries: key / label / description - see the example file */ ],
  "sourceCitation": { "enabled": true, "label": "Source" }
}
```

### 3. Write your three documents

Place them in `source-docs/`. The parser has strict structural expectations - heading conventions, tier markers, `**Source:**` lines, backtick-wrapped tags. **Read [`source-docs/README.md`](source-docs/README.md) before writing**; it has the full spec and worked examples for all three document types.

### 4. Run locally

**API:**

```bash
cd api

# Provider selection is mandatory. Set exactly one provider key.
export AI_PROVIDER=anthropic                 # or: openrouter
export ANTHROPIC_API_KEY=<your-key>          # or: OPENROUTER_API_KEY=<your-key>
export AI_MODEL=claude-haiku-4-5-20251001

export DB_PATH=/tmp/dossier-data/dossier.db
export ADMIN_KEY=local-test-key
export PIPELINE_MIN_SECTIONS=5               # lower thresholds for small local docs
export PIPELINE_MIN_ENTRIES=5
export CORS_ORIGIN=http://localhost:4200

dotnet run            # listens on http://localhost:5000 in dev
```

> The API validates provider config at startup and will refuse to boot if `AI_PROVIDER`/`AI_MODEL` are missing, if no matching key is set, or if **both** provider keys are set. This is intentional fail-fast behavior.

**Run the pipeline:**

```bash
# Summary + names must be uploaded together; framework is independent.
curl -X POST http://localhost:5000/api/admin/upload \
  -H "X-Admin-Key: local-test-key" \
  -F "summary=@source-docs/summary.md" \
  -F "names=@source-docs/names.md" \
  -F "framework=@source-docs/framework.md"

# Watch the API logs - enrichment calls the LLM per section and takes a few minutes.
# Poll: curl http://localhost:5000/api/admin/status -H "X-Admin-Key: local-test-key"
```

**Client:**

```bash
cd client

# Dev server (proxies /api to the API):
ng serve --proxy-config=proxy.conf.json     # http://localhost:4200

# Or exercise the real SSR build:
API_BASE_URL=http://localhost:5000 npm run build
ADMIN_KEY=local-test-key node dist/dossier/server/server.mjs   # http://localhost:4000
```

A `proxy.conf.json` mapping `/api` → `http://localhost:5000` is included for `ng serve`.

### 5. Deploy (containers)

Two services, one repo - build each Dockerfile and run them as separate containers on any
container host (Kubernetes, Fly, Render, a plain Docker host, etc.). The only
requirements are a persistent volume for the API's SQLite file and private network reachability
from the client to the API. Configure each service with the env vars below.

**API service** (`api/Dockerfile`):

```
AI_PROVIDER=anthropic
ANTHROPIC_API_KEY=<your-key>
AI_MODEL=claude-haiku-4-5-20251001
DB_PATH=/data/dossier.db            # mount a persistent volume at /data
ADMIN_KEY=<strong-random-secret>
PIPELINE_MIN_SECTIONS=30
PIPELINE_MIN_ENTRIES=50
ASPNETCORE_URLS=http://+:8080
CORS_ORIGIN=https://<your-client-host>
```

**Client service** (`client/Dockerfile`):

```
PORT=4000
API_BASE_URL=http://<api-internal-host>:8080   # the http:// scheme is required
ADMIN_KEY=<strong-random-secret>               # gates the SSR /proxy-diagnostics route
```

> ⚠️ The single most common deploy failure: omitting `http://` on `API_BASE_URL`. The server-side interceptor needs a full scheme to rewrite `/api/*` calls. A bare `host:8080` will silently fail every server-rendered fetch.

> `ADMIN_KEY` must be set on the **Node SSR container** (`client/src/server.ts`), not only on the API. The Express SSR server exposes a `/proxy-diagnostics` route that **fails closed**: it returns `401` unless the request carries an `X-Admin-Key` header matching `ADMIN_KEY`, and rejects every request if `ADMIN_KEY` is unset. Check it with `curl -H "X-Admin-Key: <secret>" https://<client>/proxy-diagnostics`.

Deploy both, then visit `https://<your-app>/admin`, authenticate with `ADMIN_KEY`, and upload your documents to run the first pipeline.

---

## Customization

- **Favicon** - replace `client/public/favicon.ico`. (A `TODO` comment marks the `<link>` in `index.html`.)
- **Custom domain** - attach it to the client service at your host and set `branding.domain` in your config for internal links.
- **Long-form home copy** - the "Why This Record Exists" and "About This Document" prose is directly editable markup in `client/src/app/components/home/home.html`. It is intentionally *not* in JSON; overwrite it with your own narrative.
- **Tag colors / layout** - `client/src/styles.css`. The five `tag-*` classes are stable; colors are presentation, not data.
- **Tag taxonomy text** - labels and descriptions in `research.config.json`. (Changing the *set* of tags also means touching the CSS classes and the rendering pipe - out of scope for a normal redeploy.)

---

## API reference

| Endpoint | Method | Auth | Purpose |
|----------|--------|------|---------|
| `/api/config` | GET | - | Runtime config (branding, doc labels, tags, central node) |
| `/api/metadata` | GET | - | DB metadata (versions, build time, source counts) |
| `/api/sections` | GET | - | All sections (summary + framework) |
| `/api/sections/{slug}` | GET | - | Single section detail |
| `/api/entries` | GET | - | All names-index entries |
| `/api/entries/{slug}` | GET | - | Single entry detail |
| `/api/search?q=&type=all\|sections\|entries` | GET | - | FTS5 full-text search with ranked snippets |
| `/api/generated-sources` | GET | - | Deterministic citation catalog summary |
| `/api/generated-sources/groups/{key}` | GET | - | One citation group |
| `/api/generated-sources/search?q=` | GET | - | Search the citation catalog |
| `/api/graph` | GET | - | Relationship graph JSON (if generated) |
| `/health` | GET | - | Liveness probe |
| `/api/admin/status` | GET | `X-Admin-Key` | Pipeline status + current versions |
| `/api/admin/logs` | GET | `X-Admin-Key` | In-memory pipeline log |
| `/api/admin/upload` | POST | `X-Admin-Key` | Multipart upload (`summary`/`names`/`framework`); starts pipeline |
| `/api/admin/cancel` | POST | `X-Admin-Key` | Cancel the running pipeline |
| `/api/admin/rollback` | POST | `X-Admin-Key` | Restore the previous database |

Upload rule: `summary` and `names` must arrive together; `framework` is independent. Sending only one of summary/names is rejected.

---

## Design decisions and deliberate tradeoffs

A few choices that are intentional, not accidental - and the reasoning, since the reasoning is the point:

- **Atomic file swap over in-place migration.** The live SQLite file is never written during a pipeline run. A bad upload, a validation failure, or a mid-run cancel leaves production exactly as it was. The cost is transient double disk usage during staging; the benefit is that "deploy new data" has the same safety profile as "deploy new code."
- **Content-addressed AI cache over time-based.** Caching on `hash(content)+model+prompt` rather than timestamps means correctness is structural: identical input can never produce a stale result, and a prompt or model change invalidates exactly what it should. Re-ingesting a lightly-edited document is cheap.
- **Runtime config over a build define.** Serving `research.config.json` at `/api/config` (and hydrating it through the SSR HTTP transfer cache) means rebranding or re-subjecting the site is a config edit and an API restart - no Angular rebuild. It also keeps the parser, the prompts, and the UI reading one source so they cannot disagree.
- **`bypassSecurityTrustHtml` is a considered choice, not a gap.** All rendered HTML originates from the internal pipeline and your own markdown - there is no user-generated content path. Rather than ship a sanitizer to defend against an input vector that doesn't exist, the trust boundary is drawn at ingestion. If you ever open authoring to untrusted parties, that boundary moves and a sanitizer goes in front of the pipe.
- **Graph extraction is non-fatal and runs last.** The relationship graph is the least deterministic feature (it's LLM-driven), so it executes only after the content swap has already committed. A graph failure degrades to "no graph," never to "no update."
- **Deterministic source catalog separate from the AI path.** Citations are extracted by rule, not by model, so the canonical list of sources never depends on LLM behavior and is fully reproducible.

---

## License

MIT
