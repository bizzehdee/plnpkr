import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { vi } from 'vitest';
import { CoffeePage } from './coffee.page';
import { SignalrCoffeeClient } from '../../../core/coffee.client';
import { IdentityService } from '../../../core/identity.service';
import { SessionMembershipService } from '../../../core/session-membership.service';
import {
  CoffeeActionResult,
  CoffeeBoardSnapshot,
  CoffeeDecisionInfo,
  CoffeeTopicInfo,
  ReactionEvent,
} from '../../../core/models';

const CODE = 'blue-fox-42';
const ME = 'me';

function topic(over: Partial<CoffeeTopicInfo> = {}): CoffeeTopicInfo {
  return {
    id: 'topic-1',
    text: 'Why is CI so slow?',
    authorUserId: ME,
    authorDisplayName: 'Me',
    isMine: true,
    order: 0,
    totalDots: null,
    myDots: 0,
    isDiscussed: false,
    discussedSeconds: 0,
    extensions: 0,
    ...over,
  };
}

function board(over: Partial<CoffeeBoardSnapshot> = {}): CoffeeBoardSnapshot {
  const flat = {
    id: 'room-1',
    shortCode: CODE,
    name: 'Monday coffee',
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
      tool: 'Coffee',
      organiserUserId: flat.organiserUserId,
      reactionsEnabled: flat.reactionsEnabled,
      allowRoleChange: flat.allowRoleChange,
      isClosed: flat.isClosed,
      hasPassword: false,
      participants: flat.participants,
    },
    phase: 'Propose',
    nextPhase: 'Vote',
    previousPhase: null,
    phaseDurationSeconds: 300,
    phaseDeadline: null,
    voteBudget: 3,
    allowMultiplePerItem: false,
    myDotsRemaining: 3,
    voteTotalsVisible: false,
    topics: [],
    hiddenTopicCount: 0,
    agenda: [],
    currentTopicId: null,
    extendVote: null,
    decisions: [],
    ...over,
  } as CoffeeBoardSnapshot;
}

const ok = (b: CoffeeBoardSnapshot): CoffeeActionResult => ({ status: 'Ok', board: b });

class FakeCoffeeClient {
  readonly board = signal<CoffeeBoardSnapshot | null>(board());
  readonly closed = signal(false);
  readonly status = signal<'connected'>('connected');
  readonly reactions$ = new Subject<ReactionEvent>();
  connect = vi.fn().mockResolvedValue(undefined);
  joinBoard = vi.fn().mockResolvedValue({ status: 'Ok', board: board(), participant: null, error: null });
  addTopic = vi.fn<(...a: unknown[]) => Promise<CoffeeActionResult>>().mockResolvedValue(ok(board()));
  editTopic = vi.fn<(...a: unknown[]) => Promise<CoffeeActionResult>>().mockResolvedValue(ok(board()));
  deleteTopic = vi.fn<(...a: unknown[]) => Promise<CoffeeActionResult>>().mockResolvedValue(ok(board()));
  castVote = vi.fn<(...a: unknown[]) => Promise<CoffeeActionResult>>().mockResolvedValue(ok(board()));
  withdrawVote = vi.fn<(...a: unknown[]) => Promise<CoffeeActionResult>>().mockResolvedValue(ok(board()));
  advancePhase = vi.fn<(...a: unknown[]) => Promise<CoffeeActionResult>>().mockResolvedValue(ok(board()));
  previousPhase = vi.fn<(...a: unknown[]) => Promise<CoffeeActionResult>>().mockResolvedValue(ok(board()));
  nextTopic = vi.fn<(...a: unknown[]) => Promise<CoffeeActionResult>>().mockResolvedValue(ok(board()));
  voteOnExtension = vi.fn<(...a: unknown[]) => Promise<CoffeeActionResult>>().mockResolvedValue(ok(board()));
  resolveExtension = vi.fn<(...a: unknown[]) => Promise<CoffeeActionResult>>().mockResolvedValue(ok(board()));
  addDecision = vi.fn<(...a: unknown[]) => Promise<CoffeeActionResult>>().mockResolvedValue(ok(board()));
  toggleDecisionDone = vi.fn<(...a: unknown[]) => Promise<CoffeeActionResult>>().mockResolvedValue(ok(board()));
  deleteDecision = vi.fn<(...a: unknown[]) => Promise<CoffeeActionResult>>().mockResolvedValue(ok(board()));
}

type Cmp = {
  draft: string;
  addTopic(): Promise<void>;
  canAddDot(t: CoffeeTopicInfo): boolean;
  canRemoveDot(t: CoffeeTopicInfo): boolean;
  addDot(t: CoffeeTopicInfo): Promise<void>;
  canWriteDecisions(): boolean;
  voteExtend(choice: 'KeepGoing' | 'MoveOn'): Promise<void>;
  resolveExtension(): Promise<void>;
  nextTopic(): Promise<void>;
  formatSpent(seconds: number): string;
};

async function setup(fake: FakeCoffeeClient) {
  TestBed.configureTestingModule({
    imports: [CoffeePage],
    providers: [
      provideRouter([]),
      { provide: SignalrCoffeeClient, useValue: fake },
      { provide: IdentityService, useValue: { userId: ME, displayName: 'Me' } },
      { provide: SessionMembershipService, useValue: { get: () => 'Voter', remember: vi.fn() } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => CODE } } } },
    ],
  });
  const fixture = TestBed.createComponent(CoffeePage);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture;
}

describe('CoffeePage', () => {
  afterEach(() => TestBed.resetTestingModule());

  // --- The rail -----------------------------------------------------------

  it('renders the four phases with the current one marked as the step', async () => {
    const fake = new FakeCoffeeClient();
    fake.board.set(board({ phase: 'Vote', previousPhase: 'Propose', nextPhase: 'Discuss' }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    const rail = el.querySelectorAll('nav ol li');
    expect(rail).toHaveLength(4);
    expect(el.querySelector('[aria-current="step"]')!.textContent).toContain('Vote');
  });

  it('offers the facilitator both directions along the rail', async () => {
    const fake = new FakeCoffeeClient();
    fake.board.set(board({ phase: 'Vote', previousPhase: 'Propose', nextPhase: 'Discuss' }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('← Propose');
    expect(el.textContent).toContain('Discuss →');
  });

  // --- Hidden collection --------------------------------------------------

  it('tells a proposer how many topics others are writing, without the text', async () => {
    // The projection already hid them; the page's job is to say so rather than show an empty list.
    const fake = new FakeCoffeeClient();
    fake.board.set(board({ topics: [topic()], hiddenTopicCount: 2 }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('Others are writing');
    expect(el.textContent).toContain('2');
    expect(el.textContent).toContain('Why is CI so slow?');
  });

  it('adds a topic and clears the composer', async () => {
    const fake = new FakeCoffeeClient();
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;
    cmp.draft = 'Flaky tests';

    await cmp.addTopic();

    expect(fake.addTopic).toHaveBeenCalledWith(CODE, ME, 'Flaky tests');
    expect(cmp.draft).toBe('');
  });

  it('does not send a blank topic', async () => {
    const fake = new FakeCoffeeClient();
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;
    cmp.draft = '   ';

    await cmp.addTopic();

    expect(fake.addTopic).not.toHaveBeenCalled();
  });

  // --- Dot voting ---------------------------------------------------------

  it('offers the dot controls only while voting is open', async () => {
    const proposing = new FakeCoffeeClient();
    proposing.board.set(board({ phase: 'Propose', topics: [topic()] }));
    const before = await setup(proposing);
    expect((before.nativeElement as HTMLElement).querySelector('[aria-label^="Add a dot"]')).toBeNull();

    TestBed.resetTestingModule();

    const voting = new FakeCoffeeClient();
    voting.board.set(board({ phase: 'Vote', topics: [topic()] }));
    const during = await setup(voting);
    expect((during.nativeElement as HTMLElement).querySelector('[aria-label^="Add a dot"]')).toBeTruthy();
  });

  it('refuses a second dot on one topic unless the room allows stacking', async () => {
    const fake = new FakeCoffeeClient();
    fake.board.set(board({ phase: 'Vote', topics: [topic({ myDots: 1 })] }));
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    expect(cmp.canAddDot(topic({ myDots: 1 }))).toBe(false);

    fake.board.set(board({ phase: 'Vote', allowMultiplePerItem: true, topics: [topic({ myDots: 1 })] }));
    expect(cmp.canAddDot(topic({ myDots: 1 }))).toBe(true);
  });

  it('refuses a dot when the allowance is spent', async () => {
    const fake = new FakeCoffeeClient();
    fake.board.set(board({ phase: 'Vote', myDotsRemaining: 0, topics: [topic()] }));
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    expect(cmp.canAddDot(topic())).toBe(false);
    expect(cmp.canRemoveDot(topic({ myDots: 1 }))).toBe(true);
  });

  it('shows dot totals only once they are visible', async () => {
    const voting = new FakeCoffeeClient();
    voting.board.set(board({ phase: 'Vote', topics: [topic({ totalDots: null })] }));
    const before = await setup(voting);
    expect(
      (before.nativeElement as HTMLElement).querySelector('ol.list-group span.text-bg-primary'),
    ).toBeNull();

    TestBed.resetTestingModule();

    const discussing = new FakeCoffeeClient();
    discussing.board.set(board({
      phase: 'Discuss',
      voteTotalsVisible: true,
      agenda: [topic({ totalDots: 4 })],
    }));
    const after = await setup(discussing);
    const badge = (after.nativeElement as HTMLElement).querySelector(
      'ol.list-group span.text-bg-primary',
    );
    expect(badge!.textContent).toContain('4 dots');
  });

  // --- The discussion -----------------------------------------------------

  it('names the topic under discussion and who proposed it', async () => {
    const fake = new FakeCoffeeClient();
    fake.board.set(board({
      phase: 'Discuss',
      voteTotalsVisible: true,
      currentTopicId: 'topic-1',
      agenda: [topic({ totalDots: 2, authorDisplayName: 'Bob', authorUserId: 'bob', isMine: false })],
      phaseDeadline: new Date(Date.now() + 120_000).toISOString(),
    }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('Now discussing');
    expect(el.textContent).toContain('Why is CI so slow?');
    expect(el.textContent).toContain('Bob');
  });

  it('shows the remaining time as mm:ss', async () => {
    // A bare second count is hard to read past a minute.
    const fake = new FakeCoffeeClient();
    fake.board.set(board({
      phase: 'Discuss',
      currentTopicId: 'topic-1',
      agenda: [topic()],
      phaseDeadline: new Date(Date.now() + 125_000).toISOString(),
    }));
    const fixture = await setup(fake);

    expect((fixture.nativeElement as HTMLElement).textContent).toMatch(/2:0[0-5]/);
  });

  it('says so when the whole list has been worked', async () => {
    const fake = new FakeCoffeeClient();
    fake.board.set(board({
      phase: 'Discuss',
      voteTotalsVisible: true,
      currentTopicId: null,
      agenda: [topic({ isDiscussed: true, totalDots: 2, discussedSeconds: 315 })],
    }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('worked the whole list');
    expect(el.textContent).toContain('5:15');
  });

  // --- The extension vote -------------------------------------------------

  it('shows how many have answered but not the split, while the vote runs', async () => {
    // Otherwise the room follows whoever clicked first — the whole point of hiding it.
    const fake = new FakeCoffeeClient();
    fake.board.set(board({
      phase: 'Discuss',
      currentTopicId: 'topic-1',
      agenda: [topic()],
      extendVote: { topicId: 'topic-1', answered: 2, myChoice: null, keepGoing: null, moveOn: null },
    }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('Time is up');
    expect(el.textContent).toContain('Answered');
    expect(el.querySelector('[aria-label="Keep going or move on"]')).toBeTruthy();
  });

  it('shows the split once it is resolved', async () => {
    const fake = new FakeCoffeeClient();
    fake.board.set(board({
      phase: 'Discuss',
      currentTopicId: 'topic-1',
      agenda: [topic()],
      extendVote: { topicId: 'topic-1', answered: 3, myChoice: 'KeepGoing', keepGoing: 2, moveOn: 1 },
    }));
    const fixture = await setup(fake);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Keep going 2');
  });

  it('sends the viewer their extension answer', async () => {
    const fake = new FakeCoffeeClient();
    fake.board.set(board({
      phase: 'Discuss',
      currentTopicId: 'topic-1',
      agenda: [topic()],
      extendVote: { topicId: 'topic-1', answered: 0, myChoice: null, keepGoing: null, moveOn: null },
    }));
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    await cmp.voteExtend('KeepGoing');

    expect(fake.voteOnExtension).toHaveBeenCalledWith(CODE, ME, 'KeepGoing');
  });

  it('offers only the facilitator the button that closes the vote', async () => {
    const fake = new FakeCoffeeClient();
    const notOrganiser = board({
      phase: 'Discuss',
      currentTopicId: 'topic-1',
      agenda: [topic()],
      extendVote: { topicId: 'topic-1', answered: 1, myChoice: null, keepGoing: null, moveOn: null },
      organiserUserId: 'someone-else',
      participants: [
        {
          userId: ME, displayName: 'Me', isOrganiser: false, role: 'Voter',
          hasVoted: false, changedAfterReveal: false, vote: null, isConnected: true, isOutlier: false,
        },
        {
          userId: 'someone-else', displayName: 'Ada', isOrganiser: true, role: 'Voter',
          hasVoted: false, changedAfterReveal: false, vote: null, isConnected: true, isOutlier: false,
        },
      ],
    });
    fake.board.set(notOrganiser);
    const fixture = await setup(fake);

    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Close the vote');
  });

  // --- Decisions ----------------------------------------------------------

  it('does not offer decisions before the discussion', async () => {
    const fake = new FakeCoffeeClient();
    fake.board.set(board({ phase: 'Propose' }));
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    expect(cmp.canWriteDecisions()).toBe(false);
  });

  it('still offers decisions on a closed room', async () => {
    // The same carve-out as the retro's action items: "we did that" gets ticked days later.
    const fake = new FakeCoffeeClient();
    fake.board.set(board({ phase: 'Done', isClosed: true }));
    const fixture = await setup(fake);
    const cmp = fixture.componentInstance as unknown as Cmp;

    expect(cmp.canWriteDecisions()).toBe(true);
  });

  it('renders a decision with its owner and due date', async () => {
    const decision: CoffeeDecisionInfo = {
      id: 'd1',
      title: 'Quarantine the flaky test',
      topicId: 'topic-1',
      ownerUserId: null,
      ownerName: 'Dana',
      dueDate: '2026-03-01T00:00:00Z',
      isDone: false,
      createdAt: '2026-02-01T00:00:00Z',
    };
    const fake = new FakeCoffeeClient();
    fake.board.set(board({ phase: 'Discuss', decisions: [decision] }));
    const fixture = await setup(fake);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('Quarantine the flaky test');
    expect(el.textContent).toContain('Dana');
    expect(el.querySelector('input[type="checkbox"]')).toBeTruthy();
  });

  // --- Closed / gone ------------------------------------------------------

  it('says the session has ended when the room is gone', async () => {
    const fake = new FakeCoffeeClient();
    fake.closed.set(true);
    const fixture = await setup(fake);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('session has ended');
  });
});
