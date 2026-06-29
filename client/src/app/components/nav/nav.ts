import { DOCUMENT, isPlatformBrowser, NgIf } from '@angular/common';
import {
  Component,
  OnInit,
  PLATFORM_ID,
  inject,
  signal,
} from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { DataService } from '../../services/data.service';

type Theme = 'light' | 'dark';

const THEME_STORAGE_KEY = 'dossier-theme';

@Component({
  selector: 'app-nav',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, NgIf],
  templateUrl: './nav.html',
})
export class NavComponent implements OnInit {
  private readonly document = inject(DOCUMENT);
  private readonly platformId = inject(PLATFORM_ID);
  private readonly data = inject(DataService);

  readonly theme = signal<Theme>('light');
  readonly config = this.data.config.value;

  ngOnInit(): void {
    this.applyTheme(this.getInitialTheme());
  }

  toggleTheme(): void {
    this.applyTheme(this.theme() === 'dark' ? 'light' : 'dark');
  }

  protected isDarkTheme(): boolean {
    return this.theme() === 'dark';
  }

  private getInitialTheme(): Theme {
    const presetTheme = this.document.documentElement.getAttribute('data-theme');
    if (presetTheme === 'dark' || presetTheme === 'light') {
      return presetTheme;
    }

    if (!isPlatformBrowser(this.platformId)) {
      return 'light';
    }

    const storedTheme = window.localStorage.getItem(THEME_STORAGE_KEY);
    if (storedTheme === 'dark' || storedTheme === 'light') {
      return storedTheme;
    }

    return 'light';
  }

  private applyTheme(theme: Theme): void {
    this.theme.set(theme);
    this.document.documentElement.setAttribute('data-theme', theme);

    if (isPlatformBrowser(this.platformId)) {
      window.localStorage.setItem(THEME_STORAGE_KEY, theme);
    }
  }
}
