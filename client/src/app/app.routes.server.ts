import { RenderMode, ServerRoute } from '@angular/ssr';

export const serverRoutes: ServerRoute[] = [
  { path: '', renderMode: RenderMode.Prerender },
  { path: 'summary', renderMode: RenderMode.Prerender },
  { path: 'framework', renderMode: RenderMode.Prerender },
  { path: 'names', renderMode: RenderMode.Prerender },
  { path: 'sources', renderMode: RenderMode.Server },
  // Parameterized routes: rendered on-demand by the server
  { path: 'summary/:slug', renderMode: RenderMode.Server },
  { path: 'names/:slug', renderMode: RenderMode.Server },
  { path: 'search', renderMode: RenderMode.Server },
  { path: 'admin', renderMode: RenderMode.Server },
  { path: 'connections', renderMode: RenderMode.Server },
  { path: '**', renderMode: RenderMode.Server },
];
