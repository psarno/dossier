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
  selector: 'app-names-list',
  standalone: true,
  imports: [RouterLink, FormsModule],
  templateUrl: './names-list.html',
})
export class NamesListComponent {
  private data = inject(DataService);

  entries = this.data.entries.value;
  filterText = signal('');
  activeTier = signal<number | null>(null);
  loading = this.data.entries.isLoading;

  filtered = computed(() => {
    const text = this.filterText().toLowerCase();
    const tier = this.activeTier();
    return this.entries().filter((e) => {
      const matchesTier = tier === null || e.tier === tier;
      const matchesText = !text || e.name.toLowerCase().includes(text);
      return matchesTier && matchesText;
    });
  });

  tier1 = computed(() =>
    this.filtered()
      .filter((e) => e.tier === 1)
      .sort((a, b) => a.name.localeCompare(b.name)),
  );
  tier2 = computed(() =>
    this.filtered()
      .filter((e) => e.tier === 2)
      .sort((a, b) => a.name.localeCompare(b.name)),
  );

  setFilter(text: string) {
    this.filterText.set(text);
  }
}
