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
 * The platform front door (#19/#20). TeamTools hosts two ceremony tools over one room engine, so
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
      // Not yet built (#21–#28). Listed but not linked: a card that navigates nowhere useful is
      // worse than one that says plainly it isn't ready.
      route: null,
    },
  ];
}
