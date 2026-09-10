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
    anonymous: false,
    canChangeAnonymity: false,
    columns: [
      { id: 'col-1', title: 'Went well', order: 0, cards: [card()] },
      { id: 'col-2', title: 'To improve', order: 1, cards: [] },
      { id: 'col-3', title: 'Action items', order: 2, cards: [] },
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
});
