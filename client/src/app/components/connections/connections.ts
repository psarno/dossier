import {
  Component,
  OnInit,
  OnDestroy,
  PLATFORM_ID,
  inject,
  injectAsync,
  signal,
  computed,
  effect,
  viewChild,
  ElementRef,
} from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import type {
  ConnectionsGraphService,
  GraphData,
  SimNode,
} from './connections-graph.service';
import { DataService } from '../../services/data.service';

@Component({
  selector: 'app-connections',
  standalone: true,
  imports: [],
  templateUrl: './connections.html',
  styleUrl: './connections.css',
})
export class ConnectionsComponent implements OnInit, OnDestroy {
  private http = inject(HttpClient);
  private platformId = inject(PLATFORM_ID);
  private dataService = inject(DataService);

  private graphServiceRef = injectAsync(() =>
    import('./connections-graph.service').then((m) => m.ConnectionsGraphService),
  );
  private graphService = signal<ConnectionsGraphService | null>(null);

  svgRef = viewChild<ElementRef<SVGSVGElement>>('graphSvg');

  // State signals
  loading = signal(true);
  error = signal('');
  searchQuery = signal('');
  filterWeight = signal('');
  filterType = signal('');
  showTier2 = signal(true);
  selectedNode = signal<SimNode | null>(null);
  egoMode = signal(false);
  pinnedNodes = signal<Set<string>>(new Set());

  // Data signals
  private graphData = signal<GraphData | null>(null);
  private graphReady = signal(false);

  // Derived
  nodeCount = computed(() => this.graphData()?.nodes.length ?? 0);
  edgeCount = computed(() => this.graphData()?.edges.length ?? 0);

  readonly legendItems = [
    { label: 'Confirmed', color: '#4ade80', weight: 'CONFIRMED' },
    { label: 'Corroborated', color: '#60a5fa', weight: 'CORROBORATED' },
    { label: 'Documented Claim', color: '#facc15', weight: 'DOCUMENTED_CLAIM' },
    { label: 'Govt Action', color: '#f97316', weight: 'CONFIRMED_GOVT_ACTION' },
    { label: 'Anomalous', color: '#e879f9', weight: 'ANOMALOUS' },
  ];

  constructor() {
    this.graphServiceRef().then((svc) => this.graphService.set(svc));

    // Effect 1: initialize graph when service, SVG ref, and data are all ready
    effect(() => {
      const svc = this.graphService();
      const svgEl = this.svgRef();
      const data = this.graphData();
      if (!svc || !svgEl || !data || !isPlatformBrowser(this.platformId) || this.graphReady())
        return;
      svc.initGraph(svgEl.nativeElement, data, this.dataService.config.value()?.centralNode, {
        selectedNode: this.selectedNode,
        egoMode: this.egoMode,
        pinnedNodes: this.pinnedNodes,
      });
      this.graphReady.set(true);
    });

    // Effect 2: apply visibility whenever any filter signal changes
    effect(() => {
      const svc = this.graphService();
      const ready = this.graphReady();
      if (!ready || !svc || !isPlatformBrowser(this.platformId)) return;
      svc.applyVisibility({
        searchQuery: this.searchQuery(),
        filterWeight: this.filterWeight(),
        filterType: this.filterType(),
        showTier2: this.showTier2(),
        selectedNode: this.selectedNode(),
        egoMode: this.egoMode(),
        pinnedNodes: this.pinnedNodes(),
      });
    });
  }

  ngOnInit(): void {
    if (!isPlatformBrowser(this.platformId)) return;
    this.http.get<GraphData>('/api/graph').subscribe({
      next: (data) => {
        this.graphData.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set('Graph data not yet generated.');
      },
    });
  }

  ngOnDestroy(): void {
    this.graphService()?.destroy();
  }

  onSearch(event: Event): void {
    this.searchQuery.set((event.target as HTMLInputElement).value);
  }

  onWeightFilter(event: Event): void {
    this.filterWeight.set((event.target as HTMLSelectElement).value);
  }

  onTypeFilter(event: Event): void {
    this.filterType.set((event.target as HTMLSelectElement).value);
  }

  toggleEgoMode(): void {
    this.egoMode.set(!this.egoMode());
  }

  clearSelection(): void {
    this.selectedNode.set(null);
    this.egoMode.set(false);
  }

  resetView(): void {
    this.selectedNode.set(null);
    this.egoMode.set(false);
    this.searchQuery.set('');
    this.filterWeight.set('');
    this.filterType.set('');
    this.showTier2.set(true);
    this.graphService()?.resetZoom();
  }
}
