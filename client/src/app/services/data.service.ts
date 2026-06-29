import { Service, Signal, inject } from '@angular/core';
import { HttpClient, HttpResourceRef, httpResource } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Section, SectionSummary } from '../models/section.model';
import { Entry, EntrySummary } from '../models/entry.model';
import { SearchResult } from '../models/search-result.model';
import { Metadata } from '../models/metadata.model';
import { ResearchConfig } from '../models/research-config.model';
import { GeneratedSourcesGroup, GeneratedSourcesSummary } from '../models/generated-sources.model';

/**
 * Data access for the public record.
 *
 * Render-on-load reads are exposed as `httpResource`s. Because `httpResource`
 * issues normal `HttpClient` GETs, they participate in Angular's HTTP transfer
 * cache (enabled by `provideClientHydration`): the server fetch is serialized
 * and replayed synchronously on the client during hydration, so there is no
 * double-fetch and no flash. This replaces the hand-rolled `TransferState`
 * plumbing (`makeStateKey`/get/remove/set) that previously did the same job.
 *
 * Event-driven endpoints (search + generated-sources group/search) stay
 * imperative `HttpClient` calls — they are never given a manual transfer-state
 * key, exactly as before. Their default HTTP-transfer-cache behaviour is
 * unchanged from the pre-refactor app.
 */
@Service()
export class DataService {
  private http = inject(HttpClient);

  // ── Render-on-load singletons (transfer-cached → no flash, no double-fetch) ──
  readonly config = httpResource<ResearchConfig>(() => '/api/config');
  readonly metadata = httpResource<Metadata>(() => '/api/metadata');
  readonly sections = httpResource<SectionSummary[]>(() => '/api/sections', { defaultValue: [] });
  readonly entries = httpResource<EntrySummary[]>(() => '/api/entries', { defaultValue: [] });
  readonly generatedSources = httpResource<GeneratedSourcesSummary>(() => '/api/generated-sources');

  // ── Parameterized render-on-load reads ──────────────────────────────────────
  // Created by consumers in an injection context (component field initializers).
  // A falsy slug yields an `undefined` request, leaving the resource idle.
  sectionResource(slug: Signal<string>): HttpResourceRef<Section | undefined> {
    return httpResource<Section>(() => {
      const s = slug();
      return s ? `/api/sections/${s}` : undefined;
    });
  }

  entryResource(slug: Signal<string>): HttpResourceRef<Entry | undefined> {
    return httpResource<Entry>(() => {
      const s = slug();
      return s ? `/api/entries/${s}` : undefined;
    });
  }

  /**
   * Load-driven search (e.g. "mentioned in summary" on a name page). Reactive on
   * `query`; idle until a query is present. Search is intentionally never given a
   * manual transfer-state key — it stays a fresh read, same as the imperative
   * `search()` below.
   */
  searchResource(query: Signal<string>, type = 'all'): HttpResourceRef<SearchResult[]> {
    return httpResource<SearchResult[]>(
      () => {
        const q = query().trim();
        return q ? `/api/search?q=${encodeURIComponent(q)}&type=${type}` : undefined;
      },
      { defaultValue: [] },
    );
  }

  // ── Event-driven / always-fresh imperative calls ────────────────────────────
  search(q: string, type = 'all'): Observable<SearchResult[]> {
    return this.http.get<SearchResult[]>(`/api/search?q=${encodeURIComponent(q)}&type=${type}`);
  }

  getGeneratedSourcesGroup(key: string): Observable<GeneratedSourcesGroup> {
    return this.http.get<GeneratedSourcesGroup>(
      `/api/generated-sources/groups/${encodeURIComponent(key)}`,
    );
  }

  searchGeneratedSources(q: string): Observable<GeneratedSourcesGroup[]> {
    return this.http.get<GeneratedSourcesGroup[]>(
      `/api/generated-sources/search?q=${encodeURIComponent(q)}`,
    );
  }
}
