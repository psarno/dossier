import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { DataService } from '../../services/data.service';
import { SafeHtmlPipe } from '../../pipes/safe-html.pipe';

@Component({
  selector: 'app-section-detail',
  standalone: true,
  imports: [RouterLink, SafeHtmlPipe],
  templateUrl: './section-detail.html',
})
export class SectionDetailComponent {
  private route = inject(ActivatedRoute);
  private data = inject(DataService);

  private slug = signal(this.route.snapshot.paramMap.get('slug') ?? '');
  private sectionResource = this.data.sectionResource(this.slug);

  section = this.sectionResource.value;
  loading = this.sectionResource.isLoading;
  notFound = computed(() => this.sectionResource.status() === 'error');

  tags = computed<string[]>(() => {
    try {
      return JSON.parse(this.section()?.tagsPresent ?? '[]');
    } catch {
      return [];
    }
  });

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
