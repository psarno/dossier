import {
  Component,
  OnInit,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Subject, catchError, debounceTime, distinctUntilChanged, map, of, switchMap } from 'rxjs';
import { DataService } from '../../services/data.service';
import {
  GeneratedSourceItem,
  GeneratedSourcesGroup,
  GeneratedSourcesGroupSummary,
  SourceOccurrence,
} from '../../models/generated-sources.model';
import { SafeHtmlPipe } from '../../pipes/safe-html.pipe';

@Component({
  selector: 'app-generated-sources',
  standalone: true,
  imports: [RouterLink, SafeHtmlPipe, FormsModule],
  templateUrl: './generated-sources.html',
})
export class GeneratedSourcesComponent implements OnInit {
  private data = inject(DataService);
  private searchTerms = new Subject<string>();

  summary = this.data.generatedSources.value;
  sections = computed(() => this.data.sections.value().filter((s) => s.docType === 'summary'));
  loading = this.data.generatedSources.isLoading;
  notFound = computed(() => this.data.generatedSources.status() === 'error');
  searchLoading = signal(false);
  searchQuery = signal('');
  expandedGroups = signal<Record<string, boolean>>({});
  loadedGroups = signal<Record<string, GeneratedSourcesGroup>>({});
  loadingGroups = signal<Record<string, boolean>>({});
  searchResults = signal<GeneratedSourcesGroup[] | null>(null);

  filteredGroups = computed(() => {
    const searchResults = this.searchResults();
    if (searchResults) return searchResults;
    return this.summary()?.groups ?? [];
  });

  totalVisibleItems = computed(() =>
    this.filteredGroups().reduce((sum, group) => sum + group.itemCount, 0),
  );

  constructor() {
    // Initialise the collapsed-by-default expansion state once the summary loads
    // (server resolves it before serialization; client resolves it synchronously
    // from the transfer cache, so the rendered state matches across hydration).
    effect(() => {
      const summary = this.data.generatedSources.value();
      if (summary) {
        this.expandedGroups.set(this.defaultExpandedState(summary.groups));
      }
    });
  }

  ngOnInit() {
    this.searchTerms
      .pipe(
        debounceTime(250),
        distinctUntilChanged(),
        switchMap((query) => {
          const trimmed = query.trim();
          if (!trimmed) {
            this.searchLoading.set(false);
            return of({ query: '', groups: null as GeneratedSourcesGroup[] | null });
          }

          this.searchLoading.set(true);
          return this.data.searchGeneratedSources(trimmed).pipe(
            map((groups) => ({ query: trimmed, groups })),
            catchError(() => of({ query: trimmed, groups: [] })),
          );
        }),
      )
      .subscribe((result) => {
        if (!result.query) {
          this.searchResults.set(null);
          return;
        }

        this.searchResults.set(result.groups);
        this.expandedGroups.update((state) => {
          const next = { ...state };
          for (const group of result.groups ?? []) {
            if (next[group.key] === undefined) {
              next[group.key] = true;
            }
          }
          return next;
        });
        this.searchLoading.set(false);
      });
  }

  updateSearch(value: string): void {
    this.searchQuery.set(value);
    this.searchTerms.next(value);
  }

  clearSearch(): void {
    this.searchQuery.set('');
    this.searchTerms.next('');
  }

  toggleGroup(group: GeneratedSourcesGroupSummary): void {
    const willExpand = !this.isGroupExpanded(group.key);
    if (willExpand && !this.searchQuery().trim()) {
      this.fetchGroup(group.key);
    }

    this.expandedGroups.update((state) => ({
      ...state,
      [group.key]: willExpand,
    }));
  }

  isGroupExpanded(key: string): boolean {
    const state = this.expandedGroups()[key];
    if (state !== undefined) return state;
    return this.searchQuery().trim() ? true : false;
  }

  isGroupLoading(key: string): boolean {
    return this.loadingGroups()[key] ?? false;
  }

  groupItems(key: string): GeneratedSourceItem[] {
    if (this.searchQuery().trim()) {
      return this.searchResults()?.find((group) => group.key === key)?.items ?? [];
    }

    return this.loadedGroups()[key]?.items ?? [];
  }

  displayLabel(item: GeneratedSourceItem): string {
    return this.highlightSearchTerms(this.rawDisplayLabel(item));
  }

  displayDateText(item: GeneratedSourceItem): string | null {
    return item.dateText ? this.highlightSearchTerms(item.dateText) : null;
  }

  displayOccurrenceSummary(item: GeneratedSourceItem): string {
    return this.highlightSearchTerms(this.occurrenceSummary(item));
  }

  displaySectionTitle(title: string): string {
    return this.highlightSearchTerms(title);
  }

  private rawDisplayLabel(item: GeneratedSourceItem): string {
    const title = this.extractShortTitle(item.normalizedLabel || item.raw);
    return title || this.cleanSourceText(item.normalizedLabel || item.raw);
  }

  displayNote(item: GeneratedSourceItem): string | null {
    const note = item.raw;

    const cleanedNote = this.cleanSourceText(note);
    if (!cleanedNote) return null;

    const title = this.cleanSourceText(this.rawDisplayLabel(item));
    const normalizedNote = this.normalizeForComparison(cleanedNote);
    const normalizedTitle = this.normalizeForComparison(title);

    if (normalizedNote === normalizedTitle) return null;
    if (
      normalizedNote.startsWith(normalizedTitle) &&
      normalizedNote.length <= normalizedTitle.length + 24
    ) {
      return null;
    }

    return this.highlightSearchTerms(cleanedNote);
  }

  dedupedSectionRefs(item: GeneratedSourceItem): Array<{ slug: string; title: string }> {
    const seen = new Set<string>();
    const refs: Array<{ slug: string; title: string }> = [];

    for (const occ of item.occurrences) {
      if (occ.document !== 'summary') continue;
      const slug = this.slugForContext(occ.contextLabel);
      if (!slug || seen.has(slug)) continue;
      seen.add(slug);
      refs.push({ slug, title: this.sectionTitle(slug, occ) });
    }

    return refs;
  }

  occurrenceSummary(item: GeneratedSourceItem): string {
    const summaryCount = item.occurrences.filter((o) => o.document === 'summary').length;
    const namesCount = item.occurrences.filter((o) => o.document === 'names').length;
    const parts: string[] = [];
    if (summaryCount > 0)
      parts.push(`${summaryCount} summary mention${summaryCount === 1 ? '' : 's'}`);
    if (namesCount > 0) parts.push(`${namesCount} names mention${namesCount === 1 ? '' : 's'}`);
    return parts.join(' · ');
  }

  private slugForContext(contextLabel: string): string | null {
    if (!contextLabel) return null;
    const exact = this.sections().find((s) => s.title === contextLabel);
    if (exact) return exact.slug;

    const normalized = contextLabel.toLowerCase();
    const fuzzy = this.sections().find((s) => s.title.toLowerCase() === normalized);
    return fuzzy?.slug ?? null;
  }

  private sectionTitle(slug: string, occurrence: SourceOccurrence): string {
    const section = this.sections().find((s) => s.slug === slug);
    return section?.title ?? occurrence.contextLabel ?? slug;
  }

  private fetchGroup(key: string): void {
    if (this.loadedGroups()[key] || this.loadingGroups()[key]) return;

    this.loadingGroups.update((state) => ({ ...state, [key]: true }));
    this.data.getGeneratedSourcesGroup(key).subscribe({
      next: (group) => {
        this.loadedGroups.update((state) => ({ ...state, [key]: group }));
        this.loadingGroups.update((state) => ({ ...state, [key]: false }));
      },
      error: () => {
        this.loadingGroups.update((state) => ({ ...state, [key]: false }));
      },
    });
  }

  private defaultExpandedState(groups: GeneratedSourcesGroupSummary[]): Record<string, boolean> {
    return groups.reduce<Record<string, boolean>>((state, group) => {
      state[group.key] = false;
      return state;
    }, {});
  }

  private extractShortTitle(value: string): string {
    const cleaned = this.cleanSourceText(value);
    if (!cleaned) return '';

    const separatorMatch = cleaned.match(/^(.+?)\s+[—-]\s+/);
    if (separatorMatch?.[1]) {
      return separatorMatch[1].trim();
    }

    const colonMatch = cleaned.match(/^(.+?):\s+/);
    if (colonMatch?.[1] && colonMatch[1].length <= 72) {
      return colonMatch[1].trim();
    }

    return cleaned;
  }

  private cleanSourceText(value: string | null | undefined): string {
    if (!value) return '';

    return value
      .replace(/^\s*[-*+]\s+/, '')
      .replace(/^\s{0,3}#{1,6}\s+/, '')
      .replace(/\r?\n+/g, ' ')
      .replace(/\s+/g, ' ')
      .trim();
  }

  private highlightSearchTerms(value: string): string {
    const query = this.searchQuery().trim();
    if (!query || !value) return value;

    const pattern = this.buildHighlightPattern(query);
    if (!pattern) return value;

    return value.replace(pattern, (match) => `<mark class="search-highlight">${match}</mark>`);
  }

  private buildHighlightPattern(query: string): RegExp | null {
    const terms = Array.from(
      new Set(
        query
          .split(/\s+/)
          .map((term) => term.trim())
          .filter((term) => term.length > 1)
          .map((term) => this.escapeRegExp(term)),
      ),
    );

    if (terms.length === 0) return null;
    return new RegExp(`(${terms.join('|')})`, 'gi');
  }

  private escapeRegExp(value: string): string {
    return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  }

  private normalizeForComparison(value: string): string {
    return value.replace(/[*_`]/g, '').replace(/\s+/g, ' ').trim().toLowerCase();
  }
}
