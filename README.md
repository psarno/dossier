# Dossier

**A markdown-to-database pipeline for publishing searchable, evidence-tagged research records with .NET 10, Angular 22 SSR, and SQLite.**

Dossier is built for investigative work: you write structured markdown, the pipeline turns it into a sourced record, and the site presents that record in a searchable interface. 

For the technical implementation details, see [ENGINEERING.md](ENGINEERING.md). For the source-document format, see [source-docs/README.md](source-docs/README.md).

---

## Built with

- Angular 22 SSR on the front end
- Signal-based state, `httpResource`, and hydration-friendly data loading
- ASP.NET Core on .NET 10 and SQLite FTS5 on the server side

The stack is current, but the README stays at the system level. The implementation detail lives in the engineering notes.

---

## Example automatically-generated connections graph

<img width="400" alt="dossier-graph" src="https://github.com/user-attachments/assets/d7b0286d-9ff7-48d5-a068-d466342afce8" />

---

## Implementation highlights

- The ingestion pipeline stages content into a separate SQLite file, validates it, then atomically swaps it into place so a bad run does not corrupt the live site.
- LLM enrichment is cached by content hash plus provider, model, and prompt version, so unchanged sections do not get reprocessed or rebilled.
- Runtime config is single-sourced through `research.config.json`, keeping the parser, prompts, and UI aligned from one file.
- Source extraction is deterministic and separate from the AI path, so the citation catalog is reproducible.
- SSR plus Angular transfer-cache hydration delivers fully rendered pages without an extra client-side fetch on first render.

---

## Who this is for

- **Investigative journalists and researchers** who want to publish a sourced record without touching application code.
- **People evaluating the platform** who want a concise overview before reading the engineering notes.

---

## What you get

- A searchable evidence database backed by SQLite full-text search.
- Explicit evidentiary tags so readers can see the strength of each claim.
- Three coordinated markdown documents for the record structure.
- Runtime configuration through one JSON file.
- Server-rendered pages that arrive complete and avoid client-side flash.
- A deterministic source catalog separate from the AI path.
- Optional LLM-assisted enrichment and relationship extraction.
- An Angular 22 SSR front end with hydration-aware data loading.

---

## The record model

The repository ships with a worked example so you can inspect the full system before replacing it with your own subject.

You author three markdown files:

- a summary document for the main claims,
- a names index for people and organizations,
- an analytical framework for the themes and patterns in the record.

Claims are tagged inline in markdown and backed by source citations. The tag legend and file format are documented in [source-docs/README.md](source-docs/README.md).

At a high level, the authoring flow is:

1. Write or edit the three source documents in markdown.
2. Upload them to the API.
3. Let the pipeline parse, enrich, validate, and publish a new database snapshot.
4. Serve the updated record through the SSR front end.

---

## Getting started

1. Copy `research.config.example.json` to `research.config.json` and edit it for your subject.
2. Place the three markdown source documents in `source-docs/`.
3. Run the API and the Angular client.
4. Upload the documents through the admin endpoint or UI.

The full implementation notes live in [ENGINEERING.md](ENGINEERING.md). That document covers the pipeline, cache design, hydration model, and related engineering tradeoffs in more detail.

The source-document rules are documented in [source-docs/README.md](source-docs/README.md).

---

## License

MIT
