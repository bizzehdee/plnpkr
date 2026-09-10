import { Routes } from '@angular/router';
import { HomePage } from './pages/home/home.page';
import { JoinPage } from './pages/join/join.page';

/**
 * Platform routes (#20). Each tool lives under its own prefix so a second tool adds routes rather
 * than reshaping existing ones. `/session/:shortCode` is kept as a permanent redirect because
 * invite links to it are already sitting in people's calendars and chat history.
 *
 * **Each tool loads on demand (#31).** The two tools deliberately share nothing but the room engine,
 * so they are the natural split points: a visitor to a poker table has no use for the retro board's
 * grouping and dot-voting code, and vice versa. The picker and the `/join` landing stay eager —
 * they are the two cold entry points, and making an invite link wait on a chunk would put the
 * latency in the worst possible place.
 */
export const routes: Routes = [
  { path: '', component: HomePage, title: 'TeamTools' },
  { path: 'join/:shortCode', component: JoinPage, title: 'Join' },

  // Planning Poker.
  {
    path: 'poker/new',
    title: 'New planning poker session',
    loadComponent: () => import('./pages/poker/create/poker-create.page').then((m) => m.PokerCreatePage),
  },
  {
    path: 'poker/:shortCode',
    title: 'Planning poker',
    loadComponent: () => import('./pages/session/session.page').then((m) => m.SessionPage),
  },

  // Team Retro.
  {
    path: 'retro/new',
    title: 'New team retro',
    loadComponent: () => import('./pages/retro/create/retro-create.page').then((m) => m.RetroCreatePage),
  },
  {
    path: 'retro/:shortCode/summary',
    title: 'Retro summary',
    loadComponent: () =>
      import('./pages/retro/summary/retro-summary.page').then((m) => m.RetroSummaryPage),
  },
  {
    path: 'retro/:shortCode',
    title: 'Team retro',
    loadComponent: () => import('./pages/retro/board/retro.page').then((m) => m.RetroPage),
  },

  // Lean Coffee.
  {
    path: 'coffee/new',
    title: 'New Lean Coffee',
    loadComponent: () => import('./pages/coffee/create/coffee-create.page').then((m) => m.CoffeeCreatePage),
  },
  {
    path: 'coffee/:shortCode',
    title: 'Lean Coffee',
    loadComponent: () => import('./pages/coffee/board/coffee.page').then((m) => m.CoffeePage),
  },

  // Legacy single-tool paths.
  { path: 'session/:shortCode', redirectTo: 'poker/:shortCode', pathMatch: 'full' },

  { path: '**', redirectTo: '' },
];
