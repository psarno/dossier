import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DataService } from '../../services/data.service';
import { SafeHtmlPipe } from '../../pipes/safe-html.pipe';

@Component({
  selector: 'app-framework',
  standalone: true,
  imports: [RouterLink, SafeHtmlPipe],
  templateUrl: './framework.html',
})
export class FrameworkComponent {
  private data = inject(DataService);

  private sectionResource = this.data.sectionResource(signal('analytical-framework'));

  framework = computed(() => {
    const s = this.sectionResource.value();
    return s?.docType === 'analytical_framework' ? s : null;
  });
  loading = this.sectionResource.isLoading;
  notFound = computed(() => {
    if (this.sectionResource.status() === 'error') return true;
    const s = this.sectionResource.value();
    return s != null && s.docType !== 'analytical_framework';
  });
}
