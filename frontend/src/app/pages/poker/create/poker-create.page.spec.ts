import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { vi } from 'vitest';
import { PokerCreatePage } from './poker-create.page';
import { SignalrRealtimeClient } from '../../../core/poker.client';
import { IdentityService } from '../../../core/identity.service';
import { CreateSessionResult, SessionSnapshot } from '../../../core/models';

function snapshot(shortCode: string): SessionSnapshot {
  return {
    room: {
      id: 'id-1',
      shortCode,
      name: 'Sprint 24',
      tool: 'Poker',
      organiserUserId: 'user-1',
      reactionsEnabled: true,
      allowRoleChange: true,
      isClosed: false,
      hasPassword: false,
      participants: [],
    },
    id: 'id-1',
    shortCode,
    name: 'Sprint 24',
    deckType: 'Fibonacci',
    cards: ['0', '1', '2', '?', '☕'],
    state: 'Voting',
    organiserUserId: 'user-1',
    autoReveal: false,
    reactionsEnabled: true,
    allowRoleChange: true,
    isClosed: false,
    currentStory: null,
    currentStoryNote: null,
    participants: [],
    stats: null,
    integration: null,
    timerDurationSeconds: null,
    timerDeadline: null,
    timerPausedRemainingSeconds: null,
  };
}

class FakeRealtimeClient {
  connect = vi.fn().mockResolvedValue(undefined);
  createSession = vi.fn<(...a: unknown[]) => Promise<CreateSessionResult>>().mockResolvedValue({
    status: 'Ok',
    session: snapshot('blue-fox-42'),
    error: null,
  });
}

describe('PokerCreatePage', () => {
  let fake: FakeRealtimeClient;
  let router: Router;

  beforeEach(async () => {
    fake = new FakeRealtimeClient();
    await TestBed.configureTestingModule({
      imports: [PokerCreatePage],
      providers: [
        provideRouter([]),
        { provide: SignalrRealtimeClient, useValue: fake },
        { provide: IdentityService, useValue: { userId: 'user-1', displayName: '' } },
      ],
    }).compileComponents();
    router = TestBed.inject(Router);
  });

  it('shows a validation error when the session name is blank', async () => {
    const fixture = TestBed.createComponent(PokerCreatePage);
    const cmp = fixture.componentInstance as unknown as { displayName: string; create(): Promise<void> };
    cmp.displayName = 'Alice';

    await cmp.create();
    fixture.detectChanges();

    expect(fake.createSession).not.toHaveBeenCalled();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('give the session a name');
  });

  it('creates a session and navigates to the poker session route', async () => {
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(PokerCreatePage);
    const cmp = fixture.componentInstance as unknown as {
      sessionName: string;
      displayName: string;
      create(): Promise<void>;
    };
    cmp.sessionName = 'Sprint 24';
    cmp.displayName = 'Alice';

    await cmp.create();

    expect(fake.createSession).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith(['/poker', 'blue-fox-42']);
  });

  it('shows a translated message for a rate-limited create, not the raw server text', async () => {
    fake.createSession.mockResolvedValue({
      status: 'RateLimited',
      session: null,
      error: "You're doing that too often — please wait a moment and try again.",
    });
    const fixture = TestBed.createComponent(PokerCreatePage);
    const cmp = fixture.componentInstance as unknown as {
      sessionName: string;
      displayName: string;
      create(): Promise<void>;
    };
    cmp.sessionName = 'Sprint 24';
    cmp.displayName = 'Alice';

    await cmp.create();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('doing that too often');
  });

  it('renders translated deck option labels', () => {
    const fixture = TestBed.createComponent(PokerCreatePage);
    fixture.detectChanges();
    const options = [...(fixture.nativeElement as HTMLElement).querySelectorAll('#deckType option')].map(
      (o) => o.textContent?.trim(),
    );
    expect(options).toContain('Fibonacci');
    expect(options).toContain('T-shirt sizes');
    expect(options).toContain('Custom…');
  });
});
