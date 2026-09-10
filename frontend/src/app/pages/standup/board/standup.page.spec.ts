import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { vi } from 'vitest';
import { StandupPage } from './standup.page';
import { IdentityService } from '../../../core/identity.service';
import { SessionMembershipService } from '../../../core/session-membership.service';
import { SignalrStandupClient } from '../../../core/standup.client';
import { StandupExportService } from '../../../core/standup-export.service';
import {
  ReactionEvent,
  StandupActionResult,
  StandupBlockerInfo,
  StandupBoardSnapshot,
  StandupPersonInfo,
  StandupQuestionInfo,
} from '../../../core/models';

const CODE = 'blue-fox-42';
const ME = 'me';
const Q1 = 'q-1';
const Q2 = 'q-2';

const QUESTIONS: StandupQuestionInfo[] = [
  { id: Q1, text: 'What did you do?', order: 0 },
  { id: Q2, text: 'Anything in your way?', order: 1 },
];

function person(over: Partial<StandupPersonInfo> = {}): StandupPersonInfo {
  return {
    userId: ME,
    displayName: 'Me',
    isMe: true,
    answers: [],
    postedAt: null,
    ...over,
  };
}

function blocker(over: Partial<StandupBlockerInfo> = {}): StandupBlockerInfo {
  return {
    id: 'blocker-1',
    text: 'Waiting on the platform team',
    authorUserId: ME,
    authorDisplayName: 'Me',
    ownerUserId: null,
    ownerName: null,
    isResolved: false,
    carriedOver: false,
    createdAt: '2026-01-01T09:00:00Z',
    ...over,
  };
}

function board(over: Partial<StandupBoardSnapshot> = {}): StandupBoardSnapshot {
  const flat = {
    id: 'room-1',
    shortCode: CODE,
    name: 'Monday standup',
    organiserUserId: ME,
    reactionsEnabled: true,
    allowRoleChange: true,
    isClosed: false,
    participants: [
      {
        userId: ME,
        displayName: 'Me',
        isOrganiser: true,
        role: 'Voter' as const,
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
      tool: 'Standup',
      organiserUserId: flat.organiserUserId,
      reactionsEnabled: flat.reactionsEnabled,
      allowRoleChange: flat.allowRoleChange,
      isClosed: flat.isClosed,
      hasPassword: false,
      participants: flat.participants,
    },
    questions: QUESTIONS,
    iHavePosted: false,
    postedCount: 0,
    participantCount: 3,
    people: [],
    blockers: [],
    previousBoardShortCode: null,
    ...over,
  } as StandupBoardSnapshot;
}

const ok = (b: StandupBoardSnapshot): StandupActionResult => ({ status: 'Ok', board: b });

class FakeStandupClient {
  readonly board = signal<StandupBoardSnapshot | null>(board());
  readonly closed = signal(false);
  readonly status = signal<'connected'>('connected');
  readonly reactions$ = new Subject<ReactionEvent>();
  connect = vi.fn().mockResolvedValue(undefined);
  joinBoard = vi.fn().mockResolvedValue({
    status: 'Ok', board: board(), participant: null, error: null,
  });
  answer = vi.fn<(...a: unknown[]) => Promise<StandupActionResult>>().mockResolvedValue(ok(board()));
  addBlocker = vi.fn<(...a: unknown[]) => Promise<StandupActionResult>>()
    .mockResolvedValue(ok(board()));
  assignBlocker = vi.fn<(...a: unknown[]) => Promise<StandupActionResult>>()
    .mockResolvedValue(ok(board()));
  toggleBlockerResolved = vi.fn<(...a: unknown[]) => Promise<StandupActionResult>>()
    .mockResolvedValue(ok(board()));
  deleteBlocker = vi.fn<(...a: unknown[]) => Promise<StandupActionResult>>()
    .mockResolvedValue(ok(board()));
}

type Cmp = {
  blockerText: string;
  assignOwnerUserId: string;
  assignOwnerName: string;
  draftFor(questionId: string): string;
  setDraft(questionId: string, text: string): void;
  saveAnswer(q: StandupQuestionInfo): Promise<void>;
  postAll(): Promise<void>;
  anyDraftFilled(): boolean;
  addBlocker(): Promise<void>;
  takeBlocker(b: StandupBlockerInfo): Promise<void>;
  saveAssign(b: StandupBlockerInfo): Promise<void>;
  startAssign(b: StandupBlockerInfo): void;
  download(format: 'md' | 'csv'): void;
};

const exportSpy = { download: vi.fn() };

async function setup(fake: FakeStandupClient) {
  exportSpy.download.mockClear();
  TestBed.configureTestingModule({
    imports: [StandupPage],
    providers: [
      provideRouter([]),
      { provide: SignalrStandupClient, useValue: fake },
      { provide: StandupExportService, useValue: exportSpy },
      { provide: IdentityService, useValue: { userId: ME, displayName: 'Me' } },
      { provide: SessionMembershipService, useValue: { get: () => 'Voter', remember: vi.fn() } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => CODE } } } },
    ],
  });
  const fixture = TestBed.createComponent(StandupPage);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture;
}

describe('StandupPage', () => {
  afterEach(() => TestBed.resetTestingModule());

  // --- Post-to-read -------------------------------------------------------

  it('explains the gate instead of showing an empty room', async () => {
    // The projection has already withheld everyone else's answers; what the page owes the reader is
    // the reason, so an empty panel does not look like a team that has not turned up.
    const fake = new FakeStandupClient();
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('#standup-gate')).not.toBeNull();
    expect(el.querySelector('#standup-gate')!.textContent).toContain('Post your own answers first');
  });

  it('shows everyone else once the viewer has posted', async () => {
    const fake = new FakeStandupClient();
    fake.board.set(board({
      iHavePosted: true,
      postedCount: 2,
      people: [
        person({ answers: [{ questionId: Q1, text: 'Shipped the export', updatedAt: null }] }),
        person({
          userId: 'bob',
          displayName: 'Bob',
          isMe: false,
          answers: [{ questionId: Q1, text: 'Fixed the flaky test', updatedAt: null }],
          postedAt: '2026-01-01T09:00:00Z',
        }),
      ],
    }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('#standup-gate')).toBeNull();
    expect(el.textContent).toContain('Bob');
    expect(el.textContent).toContain('Fixed the flaky test');
  });

  it('leaves the viewer out of the others list, since their own is the form', async () => {
    const fake = new FakeStandupClient();
    fake.board.set(board({
      iHavePosted: true,
      people: [person({ answers: [{ questionId: Q1, text: 'Mine', updatedAt: null }] })],
    }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('#standup-nobody-else')).not.toBeNull();
  });

  it('counts who has posted without naming who has not', async () => {
    // The point of #36: presence only knows who opened the room, so a name here would as often be
    // someone on holiday as someone late.
    const fake = new FakeStandupClient();
    fake.board.set(board({
      postedCount: 1,
      participantCount: 3,
      iHavePosted: true,
      people: [person({ answers: [{ questionId: Q1, text: 'Mine', updatedAt: null }] })],
    }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('#standup-posted-count')!.textContent!.trim())
      .toBe('1 of the 3 people in this room have posted');
  });

  it('has no phase rail and no countdown', async () => {
    // Deliberate (#36). A rail here would be the wrong kind of reuse of #35's extraction.
    const fake = new FakeStandupClient();
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('nav ol')).toBeNull();
    expect(el.textContent).not.toContain('0:');
  });

  // --- Answering ----------------------------------------------------------

  it('renders one editable box per question', async () => {
    const fake = new FakeStandupClient();
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelectorAll('textarea')).toHaveLength(2);
    expect(el.querySelector(`#standup-answer-${Q1}`)).not.toBeNull();
  });

  it('saves one answer', async () => {
    const fake = new FakeStandupClient();
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;
    cmp.setDraft(Q1, '  Shipped the export  ');

    await cmp.saveAnswer(QUESTIONS[0]);

    expect(fake.answer).toHaveBeenCalledWith(CODE, ME, Q1, 'Shipped the export');
  });

  it('posts every filled answer in one go, skipping the blank ones', async () => {
    const fake = new FakeStandupClient();
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;
    cmp.setDraft(Q1, 'Shipped the export');
    cmp.setDraft(Q2, '   ');

    await cmp.postAll();

    expect(fake.answer).toHaveBeenCalledTimes(1);
    expect(fake.answer).toHaveBeenCalledWith(CODE, ME, Q1, 'Shipped the export');
  });

  it('will not post nothing at all', async () => {
    const fake = new FakeStandupClient();
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector<HTMLButtonElement>('#standup-post-all')!.disabled).toBe(true);
  });

  it('seeds the boxes with what the server already has for me', async () => {
    // Otherwise a reload shows blank boxes over saved answers, and pressing Post would quietly
    // clear them.
    const fake = new FakeStandupClient();
    fake.board.set(board({
      iHavePosted: true,
      people: [person({
        answers: [{ questionId: Q1, text: 'Shipped the export', updatedAt: null }],
      })],
    }));
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    expect(cmp.draftFor(Q1)).toBe('Shipped the export');
    expect(cmp.draftFor(Q2)).toBe('');
  });

  it('marks an edited answer as edited', async () => {
    const fake = new FakeStandupClient();
    fake.board.set(board({
      iHavePosted: true,
      people: [
        person({ answers: [{ questionId: Q1, text: 'Mine', updatedAt: null }] }),
        person({
          userId: 'bob',
          displayName: 'Bob',
          isMe: false,
          answers: [{
            questionId: Q1, text: 'Reworded', updatedAt: '2026-01-01T10:00:00Z',
          }],
        }),
      ],
    }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('edited');
  });

  it('locks the boxes on a closed standup', async () => {
    const fake = new FakeStandupClient();
    fake.board.set(board({ isClosed: true }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector<HTMLTextAreaElement>(`#standup-answer-${Q1}`)!.disabled).toBe(true);
  });

  // --- Blockers -----------------------------------------------------------

  it('shows blockers before the viewer has posted', async () => {
    // Unlike answers: a blocker is a request for help, and gating it would keep it from whoever
    // could act on it.
    const fake = new FakeStandupClient();
    fake.board.set(board({ blockers: [blocker()] }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('#standup-gate')).not.toBeNull();
    expect(el.querySelector('#standup-blocker-list')!.textContent)
      .toContain('Waiting on the platform team');
  });

  it('raises a blocker and closes the composer', async () => {
    const fake = new FakeStandupClient();
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;
    cmp.blockerText = '  Waiting on the platform team  ';

    await cmp.addBlocker();

    expect(fake.addBlocker)
      .toHaveBeenCalledWith(CODE, ME, 'Waiting on the platform team');
    expect(cmp.blockerText).toBe('');
  });

  it('takes a blocker on in one click', async () => {
    const fake = new FakeStandupClient();
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    await cmp.takeBlocker(blocker());

    expect(fake.assignBlocker).toHaveBeenCalledWith(CODE, ME, 'blocker-1', ME, null);
  });

  it('assigns a blocker to someone who was never in the room', async () => {
    const fake = new FakeStandupClient();
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;
    cmp.startAssign(blocker());
    cmp.assignOwnerUserId = '';
    cmp.assignOwnerName = ' Dana ';

    await cmp.saveAssign(blocker());

    expect(fake.assignBlocker).toHaveBeenCalledWith(CODE, ME, 'blocker-1', null, 'Dana');
  });

  it('puts unresolved blockers above cleared ones', async () => {
    const fake = new FakeStandupClient();
    fake.board.set(board({
      blockers: [
        blocker({ id: 'done', text: 'Already sorted', isResolved: true }),
        blocker({ id: 'open', text: 'Still stuck' }),
      ],
    }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    const items = [...el.querySelectorAll('#standup-blocker-list li')];
    expect(items[0].textContent).toContain('Still stuck');
    expect(items[1].textContent).toContain('Already sorted');
  });

  it('shows a carried-forward blocker as carried over', async () => {
    const fake = new FakeStandupClient();
    fake.board.set(board({ blockers: [blocker({ carriedOver: true })] }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('carried over');
  });

  it('says when nothing is in anyone’s way', async () => {
    const fake = new FakeStandupClient();
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('#standup-no-blockers')).not.toBeNull();
  });

  // --- Retention and export -----------------------------------------------

  it('says out loud that the room will not be kept', async () => {
    // "Export it or lose it" (#36) — the platform's retention rule stated rather than carved out.
    const fake = new FakeStandupClient();
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('#standup-retention')!.textContent).toContain('export this standup');
  });

  it('exports the board the viewer can actually see', async () => {
    const fake = new FakeStandupClient();
    const snapshot = board({ iHavePosted: true });
    fake.board.set(snapshot);
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    cmp.download('md');

    expect(exportSpy.download).toHaveBeenCalledWith(snapshot, 'md');
  });

  it('shows where it was carried forward from', async () => {
    const fake = new FakeStandupClient();
    fake.board.set(board({ previousBoardShortCode: 'friday-standup' }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('friday-standup');
  });

  it('reports a deleted standup rather than an empty board', async () => {
    const fake = new FakeStandupClient();
    fake.closed.set(true);
    fake.board.set(null);
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('deleted');
  });
});
