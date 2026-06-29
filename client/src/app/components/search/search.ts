import { Component, inject, signal, computed } from '@angular/core';
import { RouterLink, ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { DataService } from '../../services/data.service';
import { SearchResult } from '../../models/search-result.model';

@Component({
  selector: 'app-search',
  standalone: true,
  imports: [RouterLink, FormsModule],
  templateUrl: './search.html',
})
export class SearchComponent {
  private data = inject(DataService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);

  query = signal('');
  type = signal<'all' | 'sections' | 'entries'>('all');
  results = signal<SearchResult[]>([]);
  loading = signal(false);
  searched = signal(false);

  sectionResults = computed(() => this.results().filter((r) => r.resultType === 'section'));
  entryResults = computed(() => this.results().filter((r) => r.resultType === 'entry'));

  constructor() {
    // Support ?q= from URL on load
    const q = this.route.snapshot.queryParamMap.get('q');
    if (q) {
      this.query.set(q);
      this.doSearch();
    }
  }

  onSubmit() {
    if (!this.query().trim()) return;
    this.router.navigate([], { queryParams: { q: this.query() }, replaceUrl: true });
    this.doSearch();
  }

  private doSearch() {
    const q = this.query().trim();
    if (!q) return;
    this.loading.set(true);
    this.searched.set(true);
    this.data.search(q, this.type()).subscribe({
      next: (results) => {
        this.results.set(results);
        this.loading.set(false);
      },
      error: () => {
        this.results.set([]);
        this.loading.set(false);
      },
    });
  }
}
