import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ActivatedRoute } from '@angular/router';
import { signal } from '@angular/core';
import { Subject } from 'rxjs';
import { vi } from 'vitest';
import { RetroPage } from './retro.page';
import { SignalrRetroClient } from '../../../core/retro.client';
import { IdentityService } from '../../../core/identity.service';
import { SessionMembershipService } from '../../../core/session-membership.service';
import {
  ReactionEvent,
  RetroActionResult,
  RetroBoardSnapshot,
  RetroCardInfo,
} from '../../../core/models';

const CODE = 'blue-fox-42';
const ME = 'me';

function card(over: Partial<RetroCardInfo> = {}): RetroCardInfo {
  return {
    id: 'card-1',
    text: 'Deploys got faster',
    authorUserId: ME,
    authorDisplayName: 'Me',
    isMine: true,
    order: 0,
    createdAt: '2026-01-01T00:00:00Z',
    groupId: null,
    ...over,
  };
}

function board(over: Partial<RetroBoardSnapshot> = {}): RetroBoardSnapshot {
  const flat = {
    id: 'room-1',
    shortCode: CODE,
    name: 'Sprint 24 retro',
    organiserUserId: ME,
    reactionsEnabled: true,
    allowRoleChange: true,
    isClosed: false,
    participants: [
      {
        userId: ME,
        displayName: 'Me',
        isOrganiser: true,
        role: 'Observer' as const,
        hasVoted: false,
        changedAfterReveal: false,
        vote: null,
        isConnected: true,
        isOutlier: false,
      },
    ],
    ...over,
  };

  return {
    ...flat,
    room: {
      id: flat.id,
      shortCode: flat.shortCode,
      name: flat.name,
      tool: 'Retro',
      organiserUserId: flat.organiserUserId,
      reactionsEnabled: flat.reactionsEnabled,
      allowRoleChange: flat.allowRoleChange,
      isClosed: flat.isClosed,
      hasPassword: false,
      participants: flat.participants,
    },
    template: 'WentWellToImprove',
    phase: 'Collect',
    allowParticipantGrouping: false,
    groups: [],
    nextPhase: 'Group',
    previousPhase: null,
    phaseDurationSeconds: null,
    phaseDeadline: null,
    anonymous: false,
    canChangeAnonymity: false,
    columns: [
      { id: 'col-1', title: 'Went well', order: 0, cards: [card()], hiddenCardCount: 0 },
      { id: 'col-2', title: 'To improve', order: 1, cards: [], hiddenCardCount: 0 },
      { id: 'col-3', title: 'Action items', order: 2, cards: [], hiddenCardCount: 0 },
    ],
    ...over,
  };
}

const ok = (b: RetroBoardSnapshot): RetroActionResult => ({ status: 'Ok', board: b });

class FakeRetroClient {
  readonly board = signal<RetroBoardSnapshot | null>(board());
  readonly closed = signal(false);
  readonly status = signal<'connected'>('connected');
  readonly reactions$ = new Subject<ReactionEvent>();
  connect = vi.fn().mockResolvedValue(undefined);
  disconnect = vi.fn().mockResolvedValue(undefined);
  joinBoard = vi.fn().mockResolvedValue({ status: 'Ok', board: board(), participant: null, error: null });
  addCard = vi.fn<(...a: unknown[]) => Promise<RetroActionResult>>().mockResolvedValue(ok(board()));
  editCard = vi.fn<(...a: unknown[]) => Promise<RetroActionResult>>().mockResolvedValue(ok(board()));
  deleteCard = vi.fn<(...a: unknown[]) => Promise<RetroActionResult>>().mockResolvedValue(ok(board()));
  moveCard = vi.fn<(...a: unknown[]) => Promise<RetroActionResult>>().mockResolvedValue(ok(board()));
  setAnonymous = vi.fn<(...a: unknown[]) => Promise<RetroActionResult>>().mockResolvedValue(ok(board()));
  advancePhase = vi.fn<(...a: unknown[]) => Promise<RetroActionResult>>().mockResolvedValue(
    ok(board({ phase: 'Group', nextPhase: 'Vote', previousPhase: 'Collect' })),
  );
  previousPhase = vi.fn<(...a: unknown[]) => Promise<RetroActionResult>>().mockResolvedValue(ok(board()));
  groupCards = vi.fn<(...a: unknown[]) => Promise<RetroActionResult>>().mockResolvedValue(ok(board()));
  ungroupCard = vi.fn<(...a: unknown[]) => Promise<RetroActionResult>>().mockResolvedValue(ok(board()));
  renameGroup = vi.fn<(...a: unknown[]) => Promise<RetroActionResult>>().mockResolvedValue(ok(board()));
  setAllowParticipantGrouping = vi
    .fn<(...a: unknown[]) => Promise<RetroActionResult>>()
    .mockResolvedValue(ok(board()));
}

type Cmp = {
  draft: string;
  editDraft: string;
  openComposer(columnId: string): void;
  addCard(column: { id: string; title: string }): Promise<void>;
  startEdit(c: RetroCardInfo): void;
  saveEdit(c: RetroCardInfo): Promise<void>;
  deleteCard(c: RetroCardInfo): Promise<void>;
  moveCard(c: RetroCardInfo, targetColumnId: string): Promise<void>;
  otherColumns(c: RetroCardInfo): { id: string }[];
  canModify(c: RetroCardInfo): boolean;
  toggleAnonymous(): Promise<void>;
  advancePhase(): Promise<void>;
  previousPhase(): Promise<void>;
  canEditText(c: RetroCardInfo): boolean;
  canGroup(): boolean;
  groupWith(c: RetroCardInfo, value: string): Promise<void>;
  ungroup(c: RetroCardInfo): Promise<void>;
  startRename(g: { id: string; label: string }): void;
  saveRename(g: { id: string; label: string }): Promise<void>;
  renameDraft: string;
  toggleParticipantGrouping(): Promise<void>;
};

async function setup(fake: FakeRetroClient) {
  TestBed.configureTestingModule({
    imports: [RetroPage],
    providers: [
      provideRouter([]),
      { provide: SignalrRetroClient, useValue: fake },
      { provide: IdentityService, useValue: { userId: ME, displayName: 'Me' } },
      { provide: SessionMembershipService, useValue: { get: () => 'Voter', remember: vi.fn() } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => CODE } } } },
    ],
  });
  const fixture = TestBed.createComponent(RetroPage);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture;
}

describe('RetroPage', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('renders every column with its cards', async () => {
    const fixture = await setup(new FakeRetroClient());
    const el = fixture.nativeElement as HTMLElement;

    const columns = el.querySelectorAll('section.card');
    expect(columns.length).toBe(3);
    expect(el.textContent).toContain('Went well');
    expect(el.textContent).toContain('Deploys got faster');
  });

  it('shows an empty-state message for a column with no cards', async () => {
    const fixture = await setup(new FakeRetroClient());

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Nothing here yet');
  });

  it('adds a card to the column whose composer is open', async () => {
    const fake = new FakeRetroClient();
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    cmp.openComposer('col-2');
    cmp.draft = 'Standups run long';
    await cmp.addCard({ id: 'col-2', title: 'To improve' });

    expect(fake.addCard).toHaveBeenCalledWith(CODE, ME, 'col-2', 'Standups run long');
  });

  it('does not send an empty card', async () => {
    const fake = new FakeRetroClient();
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    cmp.openComposer('col-1');
    cmp.draft = '   ';
    await cmp.addCard({ id: 'col-1', title: 'Went well' });

    expect(fake.addCard).not.toHaveBeenCalled();
  });

  it('offers edit and delete on my own card', async () => {
    const fixture = await setup(new FakeRetroClient());
    const cmp = fixture.componentInstance as unknown as Cmp;

    expect(cmp.canModify(card({ isMine: true }))).toBe(true);
  });

  it('saves an edit with the trimmed draft', async () => {
    const fake = new FakeRetroClient();
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    const target = card();
    cmp.startEdit(target);
    cmp.editDraft = '  reworded  ';
    await cmp.saveEdit(target);

    expect(fake.editCard).toHaveBeenCalledWith(CODE, ME, 'card-1', 'reworded');
  });

  it('deletes a card', async () => {
    const fake = new FakeRetroClient();
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    await cmp.deleteCard(card());

    expect(fake.deleteCard).toHaveBeenCalledWith(CODE, ME, 'card-1');
  });

  it('offers a keyboard move control listing the other columns', async () => {
    // The board must be usable without dragging (#4) — drag support arrives in #24 on top of this.
    const fixture = await setup(new FakeRetroClient());
    const el = fixture.nativeElement as HTMLElement;

    const select = el.querySelector<HTMLSelectElement>('select#move-card-1');
    expect(select).toBeTruthy();
    const options = Array.from(select!.options).map((o) => o.textContent?.trim());
    expect(options).toContain('To improve');
    expect(options).toContain('Action items');
    expect(options).not.toContain('Went well');
  });

  it('moves a card to the end of the chosen column', async () => {
    const fake = new FakeRetroClient();
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    await cmp.moveCard(card(), 'col-2');

    expect(fake.moveCard).toHaveBeenCalledWith(CODE, ME, 'card-1', 'col-2', 0);
  });

  it('announces a change through the live region', async () => {
    const fake = new FakeRetroClient();
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    await cmp.deleteCard(card());
    fixture.detectChanges();

    const live = (fixture.nativeElement as HTMLElement).querySelector('[aria-live="polite"]');
    expect(live?.textContent).toContain('Card deleted');
  });

  it('hides the composer and card controls on a closed board', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(board({ isClosed: true }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;
    const cmp = fixture.componentInstance as unknown as Cmp;

    expect(el.textContent).toContain('read-only');
    expect(el.querySelector('button.btn-outline-primary')).toBeNull();
    expect(cmp.canModify(card({ isMine: true }))).toBe(false);
  });

  it('reports the board as ended once the server says it closed', async () => {
    const fake = new FakeRetroClient();
    fake.closed.set(true);
    fake.board.set(null);
    const fixture = await setup(fake);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('This retro has ended');
  });

  it('surfaces a rejected card as an error the viewer can dismiss', async () => {
    const fake = new FakeRetroClient();
    fake.addCard.mockResolvedValue({ status: 'InvalidCardText', board: null });
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    cmp.openComposer('col-1');
    cmp.draft = 'x';
    await cmp.addCard({ id: 'col-1', title: 'Went well' });
    fixture.detectChanges();

    const alert = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('under 500 characters');
  });

  // --- Anonymity (#22) ----------------------------------------------------

  it('shows an anonymity badge, and no author name, on an anonymous board', async () => {
    // The server sends no authorship on an anonymous board, so the card renders without a name
    // while still being editable by its author via isMine.
    const fake = new FakeRetroClient();
    fake.board.set(
      board({
        anonymous: true,
        columns: [
          {
            id: 'col-1',
            title: 'Went well',
            order: 0,
            cards: [card({ authorUserId: null, authorDisplayName: null, isMine: true })],
            hiddenCardCount: 0,
          },
        ],
      }),
    );
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('anonymous');
    expect(el.textContent).not.toContain('Me');
    // The author still sees which card is theirs, via isMine.
    expect(el.textContent).toContain('yours');
  });

  it('offers the anonymity toggle to a facilitator while the board is still empty', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(board({ canChangeAnonymity: true }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('Make cards anonymous');
  });

  it('hides the anonymity toggle once the board has cards', async () => {
    // The server refuses the change, so offering the control would be a lie.
    const fake = new FakeRetroClient();
    fake.board.set(board({ canChangeAnonymity: false }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).not.toContain('Make cards anonymous');
  });

  it('toggles anonymity to the opposite of the current setting', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(board({ anonymous: false, canChangeAnonymity: true }));
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    await cmp.toggleAnonymous();

    expect(fake.setAnonymous).toHaveBeenCalledWith(CODE, ME, true);
  });

  it('explains why anonymity is locked when the server refuses the change', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(board({ canChangeAnonymity: true }));
    fake.setAnonymous.mockResolvedValue({ status: 'AnonymityLocked', board: null });
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    await cmp.toggleAnonymous();
    fixture.detectChanges();

    const alert = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('locked once the board has cards');
  });

  // --- Phases (#23) -------------------------------------------------------

  it('renders the phase rail with the current phase marked as the current step', async () => {
    const fixture = await setup(new FakeRetroClient());
    const el = fixture.nativeElement as HTMLElement;

    const steps = el.querySelectorAll('nav ol li');
    expect(steps.length).toBe(6);
    const current = el.querySelector('[aria-current="step"]');
    expect(current?.textContent?.trim()).toBe('Collect');
  });

  it('offers the facilitator a way forward but not back from the first phase', async () => {
    const fixture = await setup(new FakeRetroClient());
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('Group →');
    expect(el.textContent).not.toContain('← ');
  });

  it('advances the phase and announces the change', async () => {
    const fake = new FakeRetroClient();
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    await cmp.advancePhase();
    fixture.detectChanges();

    expect(fake.advancePhase).toHaveBeenCalledWith(CODE, ME, null);
    const live = (fixture.nativeElement as HTMLElement).querySelector('[aria-live="polite"]');
    expect(live?.textContent).toContain('Phase changed to Group');
  });

  it('offers a step back once past the first phase', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(board({ phase: 'Vote', nextPhase: 'Discuss', previousPhase: 'Group' }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('← Group');
  });

  it('hides the phase controls from a non-facilitator', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(
      board({
        organiserUserId: 'someone-else',
        participants: [
          {
            userId: ME,
            displayName: 'Me',
            isOrganiser: false,
            role: 'Voter',
            hasVoted: false,
            changedAfterReveal: false,
            vote: null,
            isConnected: true,
            isOutlier: false,
          },
        ],
      }),
    );
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).not.toContain('Group →');
  });

  it('reports other people writing during collect without showing what', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(
      board({
        columns: [
          { id: 'col-1', title: 'Went well', order: 0, cards: [], hiddenCardCount: 3 },
        ],
      }),
    );
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('Hidden until collecting ends');
    expect(el.textContent).toContain('3');
    expect(el.textContent).not.toContain('Nothing here yet');
  });

  it('closes the composer once collecting ends', async () => {
    // Cards are Collect-only; leaving the composer up would offer a control the server refuses.
    const fake = new FakeRetroClient();
    fake.board.set(board({ phase: 'Group', nextPhase: 'Vote', previousPhase: 'Collect' }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('button.btn-outline-primary')).toBeNull();
  });

  it('hides the edit control outside collect but keeps delete and move', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(board({ phase: 'Group', nextPhase: 'Vote', previousPhase: 'Collect' }));
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    expect(cmp.canEditText(card({ isMine: true }))).toBe(false);
    expect(cmp.canModify(card({ isMine: true }))).toBe(true);
  });

  it('shows a countdown when the phase has a running deadline', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(
      board({ phaseDeadline: new Date(Date.now() + 90_000).toISOString(), phaseDurationSeconds: 120 }),
    );
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    const badge = el.querySelector('.font-monospace');
    expect(badge?.textContent).toMatch(/9\ds/);
  });

  it('shows no countdown when none is running', async () => {
    const fixture = await setup(new FakeRetroClient());

    expect((fixture.nativeElement as HTMLElement).querySelector('.font-monospace')).toBeNull();
  });
});

// --- Grouping (#24) ---------------------------------------------------------

describe('RetroPage grouping', () => {
  afterEach(() => TestBed.resetTestingModule());

  /** A board in the Group phase, which is the only phase grouping is allowed in. */
  function grouping(over: Partial<RetroBoardSnapshot> = {}): RetroBoardSnapshot {
    return board({ phase: 'Group', nextPhase: 'Vote', previousPhase: 'Collect', ...over });
  }

  it('renders each theme with its cards and a count', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(
      grouping({
        groups: [
          {
            id: 'g-1',
            label: 'Slow feedback loop',
            order: 0,
            cards: [card({ id: 'c-1', text: 'CI is slow', groupId: 'g-1' })],
          },
        ],
      }),
    );
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('Themes');
    expect(el.textContent).toContain('Slow feedback loop');
    expect(el.textContent).toContain('CI is slow');
  });

  it('hides the themes panel until something is grouped', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(grouping());
    const fixture = await setup(fake);

    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Themes');
  });

  it('offers a keyboard control to group a card into a new or existing theme', async () => {
    // Dragging is unavailable to keyboard and screen-reader users, so this is the primary path (#4).
    const fake = new FakeRetroClient();
    fake.board.set(
      grouping({
        groups: [{ id: 'g-1', label: 'Slow feedback loop', order: 0, cards: [] }],
      }),
    );
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    const select = el.querySelector<HTMLSelectElement>('select#group-card-1');
    expect(select).toBeTruthy();
    const options = Array.from(select!.options).map((o) => o.textContent?.trim());
    expect(options).toContain('New theme');
    expect(options).toContain('Slow feedback loop');
  });

  it('groups a card into a brand-new theme', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(grouping());
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    await cmp.groupWith(card({ id: 'c-1' }), 'new');

    expect(fake.groupCards).toHaveBeenCalledWith(CODE, ME, ['c-1'], null);
  });

  it('groups a card into an existing theme', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(grouping());
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    await cmp.groupWith(card({ id: 'c-1' }), 'g-1');

    expect(fake.groupCards).toHaveBeenCalledWith(CODE, ME, ['c-1'], 'g-1');
  });

  it('ignores the placeholder option', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(grouping());
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    await cmp.groupWith(card({ id: 'c-1' }), '');

    expect(fake.groupCards).not.toHaveBeenCalled();
  });

  it('takes a card back out of its theme', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(grouping());
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    await cmp.ungroup(card({ id: 'c-1', groupId: 'g-1' }));

    expect(fake.ungroupCard).toHaveBeenCalledWith(CODE, ME, 'c-1');
  });

  it('renames a theme with the trimmed label', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(grouping());
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    cmp.startRename({ id: 'g-1', label: 'old' });
    cmp.renameDraft = '  Slow feedback loop  ';
    await cmp.saveRename({ id: 'g-1', label: 'old' });

    expect(fake.renameGroup).toHaveBeenCalledWith(CODE, ME, 'g-1', 'Slow feedback loop');
  });

  it('does not offer grouping outside the group phase', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(board({ phase: 'Collect' }));
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    expect(cmp.canGroup()).toBe(false);
    expect((fixture.nativeElement as HTMLElement).querySelector('select#group-card-1')).toBeNull();
  });

  it('does not offer grouping to a participant unless the board allows it', async () => {
    const participantOnly = {
      organiserUserId: 'someone-else',
      participants: [
        {
          userId: ME,
          displayName: 'Me',
          isOrganiser: false,
          role: 'Voter' as const,
          hasVoted: false,
          changedAfterReveal: false,
          vote: null,
          isConnected: true,
          isOutlier: false,
        },
      ],
    };

    const closedFake = new FakeRetroClient();
    closedFake.board.set(grouping({ ...participantOnly, allowParticipantGrouping: false }));
    const closedFixture = await setup(closedFake);
    expect((closedFixture.componentInstance as unknown as Cmp).canGroup()).toBe(false);
    TestBed.resetTestingModule();

    const openFake = new FakeRetroClient();
    openFake.board.set(grouping({ ...participantOnly, allowParticipantGrouping: true }));
    const openFixture = await setup(openFake);
    expect((openFixture.componentInstance as unknown as Cmp).canGroup()).toBe(true);
  });

  it('lets a facilitator open grouping to everyone', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(
      grouping({
        allowParticipantGrouping: false,
        groups: [{ id: 'g-1', label: 'A theme', order: 0, cards: [] }],
      }),
    );
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    await cmp.toggleParticipantGrouping();

    expect(fake.setAllowParticipantGrouping).toHaveBeenCalledWith(CODE, ME, true);
  });

  it('marks cards as draggable only while grouping is available', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(grouping());
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('li[draggable="true"]')).toBeTruthy();
  });
});
