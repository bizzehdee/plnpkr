import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '../../core/translate.pipe';

/** A tool on the platform landing page. `route` is null until the tool ships. */
interface ToolCard {
  id: string;
  titleKey: string;
  blurbKey: string;
  bulletKeys: string[];
  icon: string;
  route: string | null;
}

/**
 * The platform front door (#19/#20). TeamTools hosts four ceremony tools over one room engine, so
 * the landing page picks a tool rather than jumping straight into creating a poker session — the
 * poker create form moved to `/poker/new`.
 */
@Component({
  selector: 'app-home',
  imports: [RouterLink, TranslatePipe],
  templateUrl: './home.page.html',
})
export class HomePage {
  protected readonly tools: ToolCard[] = [
    {
      id: 'poker',
      titleKey: 'tool.poker.name',
      blurbKey: 'tool.poker.blurb',
      bulletKeys: ['tool.poker.point1', 'tool.poker.point2', 'tool.poker.point3'],
      icon: '🃏',
      route: '/poker/new',
    },
    {
      id: 'retro',
      titleKey: 'tool.retro.name',
      blurbKey: 'tool.retro.blurb',
      bulletKeys: ['tool.retro.point1', 'tool.retro.point2', 'tool.retro.point3'],
      icon: '🔄',
      route: '/retro/new',
    },
    {
      id: 'coffee',
      titleKey: 'tool.coffee.name',
      blurbKey: 'tool.coffee.blurb',
      bulletKeys: ['tool.coffee.point1', 'tool.coffee.point2', 'tool.coffee.point3'],
      icon: '☕',
      route: '/coffee/new',
    },
    {
      id: 'standup',
      titleKey: 'tool.standup.name',
      blurbKey: 'tool.standup.blurb',
      bulletKeys: ['tool.standup.point1', 'tool.standup.point2', 'tool.standup.point3'],
      icon: '📝',
      route: '/standup/new',
    },
  ];
}
