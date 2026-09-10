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
import { RetroExportService, RetroExportStatus } from '../../../core/retro-export.service';
import {
  ReactionEvent,
  RetroActionResult,
  RetroActionInfo,
  RetroBoardSnapshot,
  RetroCardInfo,
  RetroGroupInfo,
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
    myDots: 0,
    totalDots: null,
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
    voteBudget: 3,
    allowMultiplePerItem: false,
    myDotsRemaining: 3,
    voteTotalsVisible: false,
    ranking: [],
    actions: [],
    previousBoardShortCode: null,
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
  castVote = vi
    .fn<(...a: unknown[]) => Promise<RetroActionResult>>()
    .mockResolvedValue(ok(board({ myDotsRemaining: 2 })));
  withdrawVote = vi
    .fn<(...a: unknown[]) => Promise<RetroActionResult>>()
    .mockResolvedValue(ok(board({ myDotsRemaining: 3 })));
  addAction = vi.fn<(...a: unknown[]) => Promise<RetroActionResult>>().mockResolvedValue(ok(board()));
  editAction = vi.fn<(...a: unknown[]) => Promise<RetroActionResult>>().mockResolvedValue(ok(board()));
  toggleActionDone = vi
    .fn<(...a: unknown[]) => Promise<RetroActionResult>>()
    .mockResolvedValue(ok(board()));
  deleteAction = vi
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
  canAddDot(item: { myDots: number }): boolean;
  canRemoveDot(item: { myDots: number }): boolean;
  addDot(kind: 'Card' | 'Group', item: { id: string; myDots: number }): Promise<void>;
  removeDot(kind: 'Card' | 'Group', item: { id: string }): Promise<void>;
  canWriteActions(): boolean;
  canUpdateActions(): boolean;
  openActionComposer(fromGroup?: RetroGroupInfo): void;
  saveAction(): Promise<void>;
  toggleActionDone(a: RetroActionInfo): Promise<void>;
  actionTitle: string;
  actionOwnerUserId: string;
  actionOwnerName: string;
  actionDue: string;
};

type ExportCmp = {
  exportPassword: string;
  downloadExport(format: 'md' | 'csv' | 'json'): Promise<void>;
};

/** The export's client side, faked: these tests care about what the page offers and asks for. */
class FakeExportService {
  download = vi.fn<(...a: unknown[]) => Promise<RetroExportStatus>>().mockResolvedValue('Ok');
  load = vi.fn().mockResolvedValue({ status: 'Ok', export: null });
}

async function setup(fake: FakeRetroClient, exports = new FakeExportService()) {
  TestBed.configureTestingModule({
    imports: [RetroPage],
    providers: [
      provideRouter([]),
      { provide: SignalrRetroClient, useValue: fake },
      { provide: RetroExportService, useValue: exports },
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
    // Scoped to the columns: the ACTION composer is deliberately still offered on a closed board
    // (#26), so a page-wide selector would now match it.
    expect(el.querySelector('section.card button.btn-outline-primary')).toBeNull();
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
            myDots: 0,
            totalDots: null,
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
        groups: [{ id: 'g-1', label: 'Slow feedback loop', order: 0, cards: [], myDots: 0, totalDots: null }],
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
        groups: [{ id: 'g-1', label: 'A theme', order: 0, cards: [], myDots: 0, totalDots: null }],
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

// --- Dot voting (#25) -------------------------------------------------------

describe('RetroPage dot voting', () => {
  afterEach(() => TestBed.resetTestingModule());

  /** A board in the Vote phase, where dots may be spent. */
  function voting(over: Partial<RetroBoardSnapshot> = {}): RetroBoardSnapshot {
    return board({ phase: 'Vote', nextPhase: 'Discuss', previousPhase: 'Group', ...over });
  }

  it('shows how many dots this viewer has left', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(voting({ myDotsRemaining: 2, voteBudget: 3 }));
    const fixture = await setup(fake);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Dots left');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('2 / 3');
  });

  it('spends a dot on a loose card', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(voting());
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    await cmp.addDot('Card', { id: 'card-1', myDots: 0 });

    expect(fake.castVote).toHaveBeenCalledWith(CODE, ME, 'Card', 'card-1');
  });

  it('takes a dot back', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(voting());
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    await cmp.removeDot('Card', { id: 'card-1' });

    expect(fake.withdrawVote).toHaveBeenCalledWith(CODE, ME, 'Card', 'card-1');
  });

  it('announces the remaining allowance after each dot', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(voting());
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    await cmp.addDot('Card', { id: 'card-1', myDots: 0 });
    fixture.detectChanges();

    const live = (fixture.nativeElement as HTMLElement).querySelector('[aria-live="polite"]');
    expect(live?.textContent).toContain('Dot spent. 2 left');
  });

  it('mirrors the server budget rule so the control disables instead of failing', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(voting({ myDotsRemaining: 0 }));
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    expect(cmp.canAddDot({ myDots: 0 })).toBe(false);
  });

  it('mirrors the no-stacking rule', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(voting({ allowMultiplePerItem: false }));
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    expect(cmp.canAddDot({ myDots: 0 })).toBe(true);
    expect(cmp.canAddDot({ myDots: 1 })).toBe(false);
  });

  it('allows stacking when the board does', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(voting({ allowMultiplePerItem: true }));
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    expect(cmp.canAddDot({ myDots: 1 })).toBe(true);
  });

  it('offers no dot controls outside the vote phase', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(board({ phase: 'Collect' }));
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    expect(cmp.canAddDot({ myDots: 0 })).toBe(false);
    expect(cmp.canRemoveDot({ myDots: 1 })).toBe(false);
  });

  it('gives every dot control an accessible label naming its item', async () => {
    // "+" alone tells a screen-reader user nothing about what they are voting for (#4).
    const fake = new FakeRetroClient();
    fake.board.set(voting());
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    const add = el.querySelector('button[aria-label^="Spend a dot on"]');
    expect(add?.getAttribute('aria-label')).toContain('Deploys got faster');
  });

  it('shows no ranked agenda while voting is open', async () => {
    // A ranking is a running total by another name.
    const fake = new FakeRetroClient();
    fake.board.set(voting({ ranking: [] }));
    const fixture = await setup(fake);

    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Discussion order');
  });

  it('shows the ranked agenda once totals are visible', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(
      board({
        phase: 'Discuss',
        nextPhase: 'Actions',
        previousPhase: 'Vote',
        voteTotalsVisible: true,
        ranking: [
          { kind: 'Group', id: 'g-1', label: 'Slow feedback loop', dots: 4 },
          { kind: 'Card', id: 'c-9', label: 'good docs', dots: 1 },
        ],
      }),
    );
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('Discussion order');
    const rows = el.querySelectorAll('ol.list-group-numbered > li');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('Slow feedback loop');
    expect(rows[0].textContent).toContain('4 dots');
    expect(rows[1].textContent).toContain('1 dot');
  });
});

// --- Action items (#26) -----------------------------------------------------

describe('RetroPage action items', () => {
  afterEach(() => TestBed.resetTestingModule());

  /** A board in Discuss, where actions become writable. */
  function discussing(over: Partial<RetroBoardSnapshot> = {}): RetroBoardSnapshot {
    return board({
      phase: 'Discuss',
      nextPhase: 'Actions',
      previousPhase: 'Vote',
      voteTotalsVisible: true,
      ...over,
    });
  }

  function action(over: Partial<RetroActionInfo> = {}): RetroActionInfo {
    return {
      id: 'a-1',
      title: 'Quarantine the flaky test',
      ownerUserId: null,
      ownerName: null,
      dueDate: null,
      isDone: false,
      doneAt: null,
      sourceGroupId: null,
      carriedOver: false,
      ...over,
    };
  }

  it('lists actions with their owner and due date', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(
      discussing({
        actions: [action({ ownerName: 'Dana from Platform', dueDate: '2026-03-01T00:00:00Z' })],
      }),
    );
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('Action items');
    expect(el.textContent).toContain('Quarantine the flaky test');
    expect(el.textContent).toContain('Dana from Platform');
    // Formatted through Intl, so the date order follows the locale rather than being hand-rolled.
    expect(el.textContent).toContain('2026');
  });

  it('says so when an action has no owner yet', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(discussing({ actions: [action()] }));
    const fixture = await setup(fake);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No owner yet');
  });

  it('records a new action with a participant owner and a due date', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(discussing());
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    cmp.openActionComposer();
    cmp.actionTitle = '  Speed up CI  ';
    cmp.actionOwnerUserId = ME;
    cmp.actionDue = '2026-03-01';
    await cmp.saveAction();

    expect(fake.addAction).toHaveBeenCalledWith(
      CODE, ME, 'Speed up CI', ME, null, '2026-03-01T00:00:00.000Z', null);
  });

  it('records a free-text owner when nobody in the room is picked', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(discussing());
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    cmp.openActionComposer();
    cmp.actionTitle = 'Ask Platform to bump the runner';
    cmp.actionOwnerName = 'Dana';
    await cmp.saveAction();

    expect(fake.addAction).toHaveBeenCalledWith(
      CODE, ME, 'Ask Platform to bump the runner', null, 'Dana', null, null);
  });

  it('prefills the title from a theme it was created from', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(discussing());
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    cmp.openActionComposer({
      id: 'g-1',
      label: 'Slow feedback loop',
      order: 0,
      cards: [],
      myDots: 0,
      totalDots: 2,
    });

    expect(cmp.actionTitle).toBe('Slow feedback loop');
  });

  it('does not send an action with no title', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(discussing());
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    cmp.openActionComposer();
    cmp.actionTitle = '   ';
    await cmp.saveAction();

    expect(fake.addAction).not.toHaveBeenCalled();
  });

  it('marks an action done and announces it', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(discussing({ actions: [action()] }));
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    await cmp.toggleActionDone(action());
    fixture.detectChanges();

    expect(fake.toggleActionDone).toHaveBeenCalledWith(CODE, ME, 'a-1');
    const live = (fixture.nativeElement as HTMLElement).querySelector('[aria-live="polite"]');
    expect(live?.textContent).toContain('Action marked done');
  });

  it('labels the done checkbox with the action it belongs to', async () => {
    // A bare checkbox tells a screen-reader user nothing about what it marks done (#4).
    const fake = new FakeRetroClient();
    fake.board.set(discussing({ actions: [action()] }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    const box = el.querySelector('input[type="checkbox"]');
    expect(box?.getAttribute('aria-label')).toContain('Quarantine the flaky test');
  });

  it('marks a carried-over action as such', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(discussing({ actions: [action({ carriedOver: true })] }));
    const fixture = await setup(fake);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('carried over');
  });

  it('offers no action controls before the discussion', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(board({ phase: 'Collect' }));
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    expect(cmp.canWriteActions()).toBe(false);
    // Assert on the panel, not the words: the default template has a COLUMN called "Action items",
    // so a text match would pass or fail for the wrong reason.
    expect((fixture.nativeElement as HTMLElement).querySelector('#actions-heading')).toBeNull();
  });

  it('keeps actions editable on a closed board, and says why', async () => {
    // The one deliberate write a closed room still accepts (#26).
    const fake = new FakeRetroClient();
    fake.board.set(discussing({ isClosed: true, actions: [action()] }));
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;
    const el = fixture.nativeElement as HTMLElement;

    expect(cmp.canWriteActions()).toBe(true);
    expect(el.querySelector('input[type="checkbox"]')).toBeTruthy();
    expect(el.textContent).toContain('actions can still be updated');
  });
});

// --- Carry-over (#27) -------------------------------------------------------

describe('RetroPage carry-over', () => {
  afterEach(() => TestBed.resetTestingModule());

  function carried(over: Partial<RetroActionInfo> = {}): RetroActionInfo {
    return {
      id: 'a-1',
      title: 'Speed up CI',
      ownerUserId: null,
      ownerName: null,
      dueDate: null,
      isDone: false,
      doneAt: null,
      sourceGroupId: null,
      carriedOver: true,
      ...over,
    };
  }

  it('shows carried actions during collect, so the team reviews them first', async () => {
    // The review list at the top of Collect is the highest-value two minutes of a retro.
    const fake = new FakeRetroClient();
    fake.board.set(
      board({ phase: 'Collect', actions: [carried()], previousBoardShortCode: 'retro-1' }),
    );
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('#actions-heading')).toBeTruthy();
    expect(el.textContent).toContain('Speed up CI');
    expect(el.textContent).toContain('carried over');
  });

  it('lets a carried action be ticked off during collect', async () => {
    // Adding a NEW action is phase-gated; ticking an existing one is not, matching the server —
    // otherwise the review list would be read-only exactly when it is being reviewed.
    const fake = new FakeRetroClient();
    fake.board.set(board({ phase: 'Collect', actions: [carried()] }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;
    const cmp = fixture.componentInstance as unknown as Cmp;

    expect(cmp.canUpdateActions()).toBe(true);
    expect(cmp.canWriteActions()).toBe(false);
    expect(el.querySelector('input[type="checkbox"]')).toBeTruthy();
  });

  it('names the retro the actions were carried from', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(board({ actions: [carried()], previousBoardShortCode: 'retro-1' }));
    const fixture = await setup(fake);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Carried from');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('retro-1');
  });

  it('offers a deep-linked next retro once the board is closed', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(board({ isClosed: true, actions: [carried()] }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    const link = el.querySelector<HTMLAnchorElement>('a[href*="/retro/new"]');
    expect(link).toBeTruthy();
    expect(link!.getAttribute('href')).toContain(`from=${CODE}`);
  });

  it('does not offer a next retro while this one is still running', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(board({ actions: [carried()] }));
    const fixture = await setup(fake);

    expect((fixture.nativeElement as HTMLElement).querySelector('a[href*="/retro/new"]')).toBeNull();
  });

  // --- Export (#28) ---

  it('does not offer an export before the discussion starts', async () => {
    // The server would refuse it: the file would carry cards nobody has seen (#23) and totals
    // nobody has reached (#25). A button that can only fail is worse than no button.
    const fake = new FakeRetroClient();
    fake.board.set(board({ phase: 'Collect', voteTotalsVisible: false }));
    const fixture = await setup(fake);

    expect((fixture.nativeElement as HTMLElement).querySelector('#export-heading')).toBeNull();
  });

  it('offers all three formats once the totals are visible', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(board({ phase: 'Discuss', voteTotalsVisible: true }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('#export-heading')).toBeTruthy();
    const text = el.querySelector('section[aria-labelledby="export-heading"]')!.textContent!;
    expect(text).toContain('Markdown');
    expect(text).toContain('CSV');
    expect(text).toContain('JSON');
  });

  it('still offers the export on a closed board', async () => {
    // Exporting after the retro has ended is the normal case.
    const fake = new FakeRetroClient();
    fake.board.set(board({ isClosed: true, voteTotalsVisible: true }));
    const fixture = await setup(fake);

    expect((fixture.nativeElement as HTMLElement).querySelector('#export-heading')).toBeTruthy();
  });

  it('links to the read-only summary page', async () => {
    // The link someone pastes into a chat thread, so a reader does not have to join the room.
    const fake = new FakeRetroClient();
    fake.board.set(board({ phase: 'Discuss', voteTotalsVisible: true }));
    const fixture = await setup(fake);

    const link = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      `a[href="/retro/${CODE}/summary"]`,
    );
    expect(link).toBeTruthy();
  });

  it('downloads the format that was asked for', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(board({ phase: 'Discuss', voteTotalsVisible: true }));
    const exports = new FakeExportService();
    const fixture = await setup(fake, exports);
    const cmp = fixture.componentInstance as unknown as ExportCmp;

    await cmp.downloadExport('csv');

    expect(exports.download).toHaveBeenCalledWith(CODE, 'csv', '');
  });

  it('asks for the password only after the server has refused', async () => {
    // The board page never holds the join password — a rejoin reclaims the seat by user id — so
    // prompting up front would demand something the viewer may never have needed.
    const fake = new FakeRetroClient();
    fake.board.set(board({ phase: 'Discuss', voteTotalsVisible: true }));
    const exports = new FakeExportService();
    exports.download.mockResolvedValue('PasswordRequired');
    const fixture = await setup(fake, exports);
    const el = fixture.nativeElement as HTMLElement;
    const cmp = fixture.componentInstance as unknown as ExportCmp;

    expect(el.querySelector('#export-password')).toBeNull();

    await cmp.downloadExport('md');
    fixture.detectChanges();

    expect(el.querySelector('#export-password')).toBeTruthy();
    expect(el.textContent).toContain('has a password');
  });

  it('retries with the password the viewer supplied', async () => {
    const fake = new FakeRetroClient();
    fake.board.set(board({ phase: 'Discuss', voteTotalsVisible: true }));
    const exports = new FakeExportService();
    const fixture = await setup(fake, exports);
    const cmp = fixture.componentInstance as unknown as ExportCmp;

    cmp.exportPassword = 'hunter2';
    await cmp.downloadExport('md');

    expect(exports.download).toHaveBeenCalledWith(CODE, 'md', 'hunter2');
  });

  it('says that an anonymous board exports no authors', async () => {
    // Reassurance at the point of the decision, where someone is about to share the file.
    const fake = new FakeRetroClient();
    fake.board.set(board({ phase: 'Discuss', voteTotalsVisible: true, anonymous: true }));
    const fixture = await setup(fake);

    const text = (fixture.nativeElement as HTMLElement)
      .querySelector('section[aria-labelledby="export-heading"]')!.textContent!;
    expect(text).toContain('no card authors');
  });
});
