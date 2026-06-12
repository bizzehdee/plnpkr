import { describe, it, expect, beforeEach } from 'vitest';
import { DeckStorageService } from './deck-storage.service';

describe('DeckStorageService', () => {
  let svc: DeckStorageService;

  beforeEach(() => {
    localStorage.clear();
    svc = new DeckStorageService();
  });

  it('starts empty', () => {
    expect(svc.list()).toEqual([]);
  });

  it('saves and lists a named deck', () => {
    svc.save({ name: 'Powers of 3', cards: '1, 3, 9, 27' });
    expect(svc.list()).toEqual([{ name: 'Powers of 3', cards: '1, 3, 9, 27' }]);
  });

  it('replaces a deck with the same name (case-insensitive), no duplicates', () => {
    svc.save({ name: 'Mine', cards: '1, 2' });
    svc.save({ name: 'mine', cards: '1, 2, 3' });
    // Replaced in place; the latest save's name/cards win, and there's only one entry.
    expect(svc.list()).toEqual([{ name: 'mine', cards: '1, 2, 3' }]);
  });

  it('ignores blank name or cards', () => {
    svc.save({ name: '   ', cards: '1, 2' });
    svc.save({ name: 'X', cards: '   ' });
    expect(svc.list()).toEqual([]);
  });

  it('removes a deck by name', () => {
    svc.save({ name: 'A', cards: '1' });
    svc.save({ name: 'B', cards: '2' });
    svc.remove('a');
    expect(svc.list()).toEqual([{ name: 'B', cards: '2' }]);
  });
});
