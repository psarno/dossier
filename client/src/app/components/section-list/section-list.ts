import {
  Component,
  inject,
  signal,
  computed,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { DataService } from '../../services/data.service';

@Component({
  selector: 'app-section-list',
  standalone: true,
  imports: [RouterLink, FormsModule],
  templateUrl: './section-list.html',
})
export class SectionListComponent {
  private data = inject(DataService);

  sections = computed(() => this.data.sections.value().filter((s) => s.docType === 'summary'));
  activeTag = signal<string>('');
  loading = this.data.sections.isLoading;

  filteredSections = computed(() => {
    const tag = this.activeTag();
    if (!tag) return this.sections();
    return this.sections().filter((s) => {
      try {
        const tags: string[] = JSON.parse(s.tagsPresent);
        return tags.includes(tag);
      } catch {
        return false;
      }
    });
  });

  allTags = computed(() => {
    const tagSet = new Set<string>();
    for (const s of this.sections()) {
      try {
        const tags: string[] = JSON.parse(s.tagsPresent);
        tags.forEach((t) => tagSet.add(t));
      } catch {
        /* skip */
      }
    }
    return Array.from(tagSet).sort();
  });

  setTag(tag: string) {
    this.activeTag.set(this.activeTag() === tag ? '' : tag);
  }

  tagClass(tag: string): string {
    const map: Record<string, string> = {
      CONFIRMED: 'tag-confirmed',
      CORROBORATED: 'tag-corroborated',
      'DOCUMENTED CLAIM': 'tag-documented-claim',
      'CONFIRMED GOVT ACTION': 'tag-govt-action',
      ANOMALOUS: 'tag-anomalous',
    };
    return map[tag] ?? '';
  }

  parseTags(json: string): string[] {
    try {
      return JSON.parse(json);
    } catch {
      return [];
    }
  }

  tagLabel(tag: string): string {
    const map: Record<string, string> = {
      CONFIRMED: 'Confirmed',
      CORROBORATED: 'Corroborated',
      'DOCUMENTED CLAIM': 'Documented Claim',
      'CONFIRMED GOVT ACTION': 'Confirmed Govt Action',
      ANOMALOUS: 'Anomalous',
    };
    return map[tag] ?? tag;
  }
}
