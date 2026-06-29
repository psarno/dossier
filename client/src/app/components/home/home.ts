import { Component, effect, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { Title } from '@angular/platform-browser';
import { DataService } from '../../services/data.service';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [RouterLink, DatePipe],
  templateUrl: './home.html',
})
export class HomeComponent {
  private data = inject(DataService);
  private titleService = inject(Title);
  metadata = this.data.metadata.value;
  config = this.data.config.value;

  constructor() {
    effect(() => {
      const title = this.config()?.branding?.siteTitle;
      if (title) {
        this.titleService.setTitle(title);
      }
    });
  }

  private readonly tagClassMap: Record<string, string> = {
    CONFIRMED: 'confirmed',
    CORROBORATED: 'corroborated',
    'DOCUMENTED CLAIM': 'documented-claim',
    'CONFIRMED GOVT ACTION': 'govt-action',
    ANOMALOUS: 'anomalous',
  };

  tagCssClass(key: string): string {
    return (
      this.tagClassMap[key] ??
      key
        .toLowerCase()
        .replace(/\s+/g, '-')
        .replace(/[^a-z0-9-]/g, '')
    );
  }
}
