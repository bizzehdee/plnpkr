import { Routes } from '@angular/router';
import { HomePage } from './pages/home/home.page';
import { JoinPage } from './pages/join/join.page';
import { PokerCreatePage } from './pages/poker/create/poker-create.page';
import { RetroCreatePage } from './pages/retro/create/retro-create.page';
import { RetroPage } from './pages/retro/board/retro.page';
import { RetroSummaryPage } from './pages/retro/summary/retro-summary.page';
import { SessionPage } from './pages/session/session.page';

/**
 * Platform routes (#20). Each tool lives under its own prefix so a second tool adds routes rather
 * than reshaping existing ones. `/session/:shortCode` is kept as a permanent redirect because
 * invite links to it are already sitting in people's calendars and chat history.
 */
export const routes: Routes = [
  { path: '', component: HomePage, title: 'TeamTools' },
  { path: 'join/:shortCode', component: JoinPage, title: 'Join' },

  // Planning Poker.
  { path: 'poker/new', component: PokerCreatePage, title: 'New planning poker session' },
  { path: 'poker/:shortCode', component: SessionPage, title: 'Planning poker' },

  // Team Retro.
  { path: 'retro/new', component: RetroCreatePage, title: 'New team retro' },
  { path: 'retro/:shortCode/summary', component: RetroSummaryPage, title: 'Retro summary' },
  { path: 'retro/:shortCode', component: RetroPage, title: 'Team retro' },

  // Legacy single-tool paths.
  { path: 'session/:shortCode', redirectTo: 'poker/:shortCode', pathMatch: 'full' },

  { path: '**', redirectTo: '' },
];
