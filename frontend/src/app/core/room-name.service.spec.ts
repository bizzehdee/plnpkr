import { RoomNameService } from './room-name.service';

const KEY = 'pp.roomNames';

describe('RoomNameService', () => {
  let sut: RoomNameService;

  beforeEach(() => {
    localStorage.clear();
    sut = new RoomNameService();
  });

  afterEach(() => localStorage.clear());

  // --- Recall -------------------------------------------------------------

  it('offers nothing on a browser that has never created a room', () => {
    expect(sut.recall('Poker')).toEqual({ name: '', appendDate: false });
  });

  it('offers back what was used last time', () => {
    sut.remember('Standup', 'Team Dragon', true);

    expect(sut.recall('Standup')).toEqual({ name: 'Team Dragon', appendDate: true });
  });

  it('remembers each tool separately', () => {
    // A poker session and a retro are rarely called the same thing.
    sut.remember('Poker', 'Sprint 24 backlog', false);
    sut.remember('Retro', 'Sprint 24 retro', true);

    expect(sut.recall('Poker')).toEqual({ name: 'Sprint 24 backlog', appendDate: false });
    expect(sut.recall('Retro')).toEqual({ name: 'Sprint 24 retro', appendDate: true });
    expect(sut.recall('Coffee').name).toBe('');
  });

  it('replaces the previous name for a tool rather than accumulating', () => {
    sut.remember('Coffee', 'Monday coffee', false);
    sut.remember('Coffee', 'Friday coffee', true);

    expect(sut.recall('Coffee')).toEqual({ name: 'Friday coffee', appendDate: true });
  });

  it('trims what it stores', () => {
    sut.remember('Poker', '  Team Dragon  ', false);

    expect(sut.recall('Poker').name).toBe('Team Dragon');
  });

  it('does not remember a blank name', () => {
    sut.remember('Poker', '   ', true);

    expect(sut.recall('Poker')).toEqual({ name: '', appendDate: false });
  });

  it('turning the stamp off is remembered as off', () => {
    // A false that failed to persist would silently re-stamp next time.
    sut.remember('Retro', 'Team Dragon', true);
    sut.remember('Retro', 'Team Dragon', false);

    expect(sut.recall('Retro').appendDate).toBe(false);
  });

  // --- Composing ----------------------------------------------------------

  it('appends the date as YYYY-MM-DD when the box is ticked', () => {
    expect(sut.compose('Team Dragon', true, new Date(2026, 8, 10)))
      .toBe('Team Dragon 2026-09-10');
  });

  it('leaves the name alone when the box is not ticked', () => {
    expect(sut.compose('Team Dragon', false, new Date(2026, 8, 10))).toBe('Team Dragon');
  });

  it('pads single-digit months and days', () => {
    expect(sut.compose('Team Dragon', true, new Date(2026, 0, 5)))
      .toBe('Team Dragon 2026-01-05');
  });

  it('dates the room by the browser’s day, not UTC’s', () => {
    // `toISOString()` would call a 9pm session in Sydney tomorrow and an 8am one in Los Angeles
    // yesterday — and "which day is this standup" is the question the stamp exists to answer.
    const lateEvening = new Date(2026, 8, 10, 23, 30);

    expect(sut.compose('Team Dragon', true, lateEvening)).toBe('Team Dragon 2026-09-10');
  });

  it('composing twice is the same as composing once', () => {
    // Protects the recalled-name path: a stored name that somehow carries a date does not compound.
    const day = new Date(2026, 8, 10);
    const once = sut.compose('Team Dragon', true, day);

    expect(sut.compose(once, true, day)).toBe('Team Dragon 2026-09-10');
  });

  it('replaces a date the organiser typed themselves', () => {
    expect(sut.compose('Sprint review 2026-09-01', true, new Date(2026, 8, 10)))
      .toBe('Sprint review 2026-09-10');
  });

  it('leaves a name that merely contains a date in the middle', () => {
    expect(sut.compose('2026-09-01 planning', true, new Date(2026, 8, 10)))
      .toBe('2026-09-01 planning 2026-09-10');
  });

  it('will not turn a blank name into a bare date', () => {
    // The date is an append, not a name. A room called "2026-09-10" is nobody's intent, and the
    // create forms still require a name of their own.
    expect(sut.compose('   ', true, new Date(2026, 8, 10))).toBe('');
  });

  // --- Storage that cannot be trusted -------------------------------------

  it('offers nothing when the stored value is not readable', () => {
    localStorage.setItem(KEY, '{not json');

    expect(sut.recall('Poker')).toEqual({ name: '', appendDate: false });
  });

  it('offers nothing when the stored entry is the wrong shape', () => {
    localStorage.setItem(KEY, JSON.stringify({ Poker: { name: 42 } }));

    expect(sut.recall('Poker')).toEqual({ name: '', appendDate: false });
  });

  it('a corrupt store still accepts a new name', () => {
    localStorage.setItem(KEY, '[]');

    sut.remember('Poker', 'Team Dragon', true);

    expect(sut.recall('Poker')).toEqual({ name: 'Team Dragon', appendDate: true });
  });
});
