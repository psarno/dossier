import { Routes } from '@angular/router';
import { HomeComponent } from './components/home/home';
import { SectionListComponent } from './components/section-list/section-list';
import { SectionDetailComponent } from './components/section-detail/section-detail';
import { NamesListComponent } from './components/names-list/names-list';
import { NamesDetailComponent } from './components/names-detail/names-detail';
import { SearchComponent } from './components/search/search';
import { AdminComponent } from './components/admin/admin';
import { GeneratedSourcesComponent } from './components/generated-sources/generated-sources';
import { FrameworkComponent } from './components/framework/framework';

export const routes: Routes = [
  { path: '', component: HomeComponent },
  { path: 'summary', component: SectionListComponent },
  { path: 'summary/:slug', component: SectionDetailComponent },
  { path: 'framework', component: FrameworkComponent },
  { path: 'names', component: NamesListComponent },
  { path: 'names/:slug', component: NamesDetailComponent },
  { path: 'sources', component: GeneratedSourcesComponent },
  { path: 'search', component: SearchComponent },
  { path: 'admin', component: AdminComponent },
  {
    path: 'connections',
    loadComponent: () => import('./components/connections/connections').then(m => m.ConnectionsComponent),
  },
  { path: '**', redirectTo: '' },
];
