import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { DataService } from '../../services/data.service';
import { SafeHtmlPipe } from '../../pipes/safe-html.pipe';

@Component({
  selector: 'app-names-detail',
  standalone: true,
  imports: [RouterLink, SafeHtmlPipe],
  templateUrl: './names-detail.html',
})
export class NamesDetailComponent {
  private route = inject(ActivatedRoute);
  private data = inject(DataService);

  private slug = signal(this.route.snapshot.paramMap.get('slug') ?? '');
  private entryResource = this.data.entryResource(this.slug);

  entry = this.entryResource.value;
  loading = this.entryResource.isLoading;
  notFound = computed(() => this.entryResource.status() === 'error');

  sectionRefs = computed<string[]>(() => {
    try {
      return JSON.parse(this.entry()?.sectionRefs ?? '[]');
    } catch {
      return [];
    }
  });

  private sectionTitleMap = computed(() => {
    const map = new Map<string, string>();
    for (const s of this.data.sections.value()) {
      map.set(s.slug, s.title);
    }
    return map;
  });

  // "Mentioned in summary" — a fresh, load-driven search keyed on the entry name.
  // Idle until the entry resolves, mirroring the old subscribe-after-load chain.
  private entryName = computed(() => this.entry()?.name ?? '');
  private summaryHitsResource = this.data.searchResource(this.entryName, 'sections');
  summaryHits = this.summaryHitsResource.value;
  searchDone = computed(() => {
    if (!this.entryName()) return false;
    const status = this.summaryHitsResource.status();
    return status === 'resolved' || status === 'error';
  });

  sectionTitle(slug: string): string {
    return this.sectionTitleMap().get(slug) ?? slug;
  }
}
