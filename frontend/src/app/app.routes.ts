import { Routes } from '@angular/router';
import { HomePage } from './pages/home/home.page';
import { JoinPage } from './pages/join/join.page';
import { SessionPage } from './pages/session/session.page';

export const routes: Routes = [
  { path: '', component: HomePage, title: 'plnpkr' },
  { path: 'join/:shortCode', component: JoinPage, title: 'Join session' },
  { path: 'session/:shortCode', component: SessionPage, title: 'plnpkr' },
  { path: '**', redirectTo: '' },
];
