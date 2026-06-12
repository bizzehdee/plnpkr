import { Injectable } from '@angular/core';
import { SavedDeck } from './models';

const KEY = 'pp.decks';

/**
 * Remembers the organiser's named custom decks in their own browser's localStorage (opt-in; same
 * mechanism as the display name and tracker connection, #34), so they're offered on the create screen
 * and the mid-session deck switcher without retyping. Per-browser, never server-side. See #11.
 */
@Injectable({ providedIn: 'root' })
export class DeckStorageService {
  /** All saved decks, or an empty list. */
  list(): SavedDeck[] {
    const raw = localStorage.getItem(KEY);
    if (!raw) return [];
    try {
      const parsed = JSON.parse(raw);
      return Array.isArray(parsed) ? (parsed as SavedDeck[]) : [];
    } catch {
      return [];
    }
  }

  /** Add or replace a deck by name (case-insensitive); blank names/cards are ignored. */
  save(deck: SavedDeck): SavedDeck[] {
    const name = deck.name.trim();
    const cards = deck.cards.trim();
    if (!name || !cards) return this.list();
    const others = this.list().filter((d) => d.name.toLowerCase() !== name.toLowerCase());
    const next = [...others, { name, cards }];
    localStorage.setItem(KEY, JSON.stringify(next));
    return next;
  }

  /** Remove a deck by name (case-insensitive). */
  remove(name: string): SavedDeck[] {
    const next = this.list().filter((d) => d.name.toLowerCase() !== name.trim().toLowerCase());
    localStorage.setItem(KEY, JSON.stringify(next));
    return next;
  }
}
