import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { ActivatedRoute } from '@angular/router';
import { vi } from 'vitest';
import { Subject } from 'rxjs';
import { SessionPage } from './session.page';
import { SignalrRealtimeClient } from '../../core/realtime.client';
import { IdentityService } from '../../core/identity.service';
import { ParticipantInfo, ReactionEvent, SessionSnapshot, SessionState } from '../../core/models';

const CODE = 'blue-fox-42';
const ME = 'me-user';

function participant(over: Partial<ParticipantInfo> = {}): ParticipantInfo {
  return {
    userId: 'p',
    displayName: 'P',
    isOrganiser: false,
    role: 'Voter',
    hasVoted: false,
    changedAfterReveal: false,
    vote: null,
    isConnected: true,
    isOutlier: false,
    ...over,
  };
}

function snap(over: Partial<SessionSnapshot> = {}): SessionSnapshot {
  return {
    id: 'id',
    shortCode: CODE,
    name: 'Sprint 24',
    deckType: 'Fibonacci',
    cards: ['1', '2', '3', '?', '☕'],
    state: 'Voting' as SessionState,
    organiserUserId: null,
    autoReveal: false,
    reactionsEnabled: true,
    allowRoleChange: true,
    isClosed: false,
    currentStory: null,
    participants: [participant({ userId: ME, displayName: 'Me' })],
    stats: null,
    integration: null,
    timerDurationSeconds: null,
    timerDeadline: null,
    timerPausedRemainingSeconds: null,
    ...over,
  };
}

class FakeRealtimeClient {
  readonly session = signal<SessionSnapshot | null>(snap());
  readonly closed = signal(false);
  readonly reactions$ = new Subject<ReactionEvent>();
  react = vi.fn().mockResolvedValue(undefined);
  castVote = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  revealVotes = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  resetRound = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  resetVote = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  setAutoReveal = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  setStory = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  setDeck = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  closeSession = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  deleteSession = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  setPassword = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  setReactionsEnabled = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  setAllowRoleChange = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  changeRole = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  setTimerDuration = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  startTimer = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  pauseTimer = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  resumeTimer = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  stopTimer = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  connectTracker = vi.fn().mockResolvedValue({ status: 'Ok', session: null, accountName: 'Ada' });
  disconnectTracker = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  loadQueueFromUrl = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  loadQueueFromKeys = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  selectQueueItem = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  clearQueue = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
  leaveSession = vi.fn().mockResolvedValue(undefined);
}

function setup(fake: FakeRealtimeClient) {
  TestBed.configureTestingModule({
    imports: [SessionPage],
    providers: [
      provideRouter([]),
      { provide: SignalrRealtimeClient, useValue: fake },
      { provide: IdentityService, useValue: { userId: ME, displayName: 'Me' } },
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { paramMap: { get: () => CODE } } },
      },
    ],
  });
  const fixture = TestBed.createComponent(SessionPage);
  fixture.detectChanges();
  return fixture;
}

describe('SessionPage', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('shows the card deck for a voter and casts a vote on click', async () => {
    const fake = new FakeRealtimeClient();
    const fixture = setup(fake);

    const buttons = (fixture.nativeElement as HTMLElement).querySelectorAll('button.btn-outline-primary, button.btn-primary');
    // Card buttons include the deck values
    const cardButton = [...buttons].find((b) => b.textContent?.trim() === '3') as HTMLButtonElement;
    expect(cardButton).toBeTruthy();

    cardButton.click();
    expect(fake.castVote).toHaveBeenCalledWith(CODE, ME, '3');
  });

  it('hides the deck and shows an observing note for observers', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(snap({ participants: [participant({ userId: ME, role: 'Observer' })] }));
    const fixture = setup(fake);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain("You're observing");
    const cardButtons = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].filter(
      (b) => b.textContent?.trim() === '3',
    );
    expect(cardButtons.length).toBe(0);
  });

  it('shows organiser controls only to the organiser', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({
        organiserUserId: ME,
        participants: [participant({ userId: ME, isOrganiser: true, role: 'Observer' })],
      }),
    );
    const fixture = setup(fake);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Organiser controls');
  });

  it('hides organiser controls from a non-organiser voter', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({
        organiserUserId: 'someone-else',
        participants: [participant({ userId: ME }), participant({ userId: 'someone-else', isOrganiser: true })],
      }),
    );
    const fixture = setup(fake);

    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Organiser controls');
  });

  it('marks a disconnected participant as away', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({
        participants: [
          participant({ userId: ME, displayName: 'Me' }),
          participant({ userId: 'bob', displayName: 'Bob', isConnected: false }),
        ],
      }),
    );
    const fixture = setup(fake);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('away');
  });

  it('displays the current story', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(snap({ currentStory: 'PROJ-123 Login' }));
    const fixture = setup(fake);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('PROJ-123 Login');
  });

  it('shows the linked issue (title + key) to everyone', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({
        integration: {
          provider: 'Jira',
          linkedIssue: {
            key: 'PROJ-7',
            title: 'Login page',
            description: '<p>Build the <b>login</b></p>',
            url: 'https://acme.atlassian.net/browse/PROJ-7',
            storyPoints: 3,
            storyPointsFieldAvailable: true,
          },
          queue: [],
        },
      }),
    );
    const fixture = setup(fake);

    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('PROJ-7');
    expect(el.textContent).toContain('Login page');
    // The title shows once (in the story line), not duplicated in the description card.
    expect(el.textContent!.split('Login page').length - 1).toBe(1);
    // Link to the ticket + current points moved into the story line.
    const link = el.querySelector('a[href="https://acme.atlassian.net/browse/PROJ-7"]');
    expect(link?.textContent).toContain('PROJ-7');
    expect(el.textContent).toContain('3 pts');
    // Description renders as formatted HTML inside the fixed-height container — no title repeated.
    const desc = el.querySelector('.ticket-description');
    expect(desc).toBeTruthy();
    expect(desc!.querySelector('b')?.textContent).toBe('login');
    expect(desc!.textContent).not.toContain('Login page');
  });

  // The integration UI only renders when the server reports ≥1 enabled provider.
  function enableIntegrations(
    fixture: ReturnType<typeof setup>,
    providers: { id: string; oauth: boolean }[] = [{ id: 'Jira', oauth: false }],
  ): void {
    (fixture.componentInstance as unknown as { enabledProviders: { set(v: unknown): void } })
      .enabledProviders.set(providers);
    fixture.detectChanges();
  }

  // Modals/menu are signal-controlled; open them directly for content assertions.
  function openModal(fixture: ReturnType<typeof setup>, modal: 'invite' | 'tracker' | 'settings' | 'danger'): void {
    (fixture.componentInstance as unknown as { activeModal: { set(v: string): void } }).activeModal.set(modal);
    fixture.detectChanges();
  }

  function openMenu(fixture: ReturnType<typeof setup>): void {
    const btn = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')]
      .find((b) => b.textContent?.trim() === '☰ Menu') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();
  }

  it('offers the organiser the issue-tracker modal when integrations are enabled', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({
        organiserUserId: ME,
        participants: [participant({ userId: ME, isOrganiser: true, role: 'Observer' })],
      }),
    );
    const fixture = setup(fake);
    enableIntegrations(fixture);

    // The menu offers an "Issue tracker" item…
    openMenu(fixture);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Issue tracker');

    // …and its modal shows the connect affordance.
    openModal(fixture, 'tracker');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Connect a tracker');
  });

  it('hides the issue-tracker menu item when integrations are disabled server-side', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({
        organiserUserId: ME,
        participants: [participant({ userId: ME, isOrganiser: true, role: 'Observer' })],
      }),
    );
    const fixture = setup(fake); // integrationsEnabled defaults false (options call not stubbed)

    openMenu(fixture);
    const menuText = (fixture.nativeElement as HTMLElement).querySelector('.dropdown-menu')?.textContent ?? '';
    expect(menuText).not.toContain('Issue tracker');
    expect(menuText).toContain('Settings'); // the menu still works for other items
  });

  function openConnectPanel(fixture: ReturnType<typeof setup>): void {
    openModal(fixture, 'tracker');
    const connectBtn = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')]
      .find((b) => b.textContent?.trim() === 'Connect a tracker') as HTMLButtonElement;
    connectBtn.click();
    fixture.detectChanges();
  }

  function organiserSession() {
    return snap({ organiserUserId: ME, participants: [participant({ userId: ME, isOrganiser: true, role: 'Observer' })] });
  }

  it('hides the provider picker when only one provider is enabled', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(organiserSession());
    const fixture = setup(fake);
    enableIntegrations(fixture, [{ id: 'Jira', oauth: false }]);
    openConnectPanel(fixture);

    // No Azure DevOps option since only Jira is enabled; the picker collapses away.
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Azure DevOps');
  });

  it('lists both providers in the picker when both are enabled', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(organiserSession());
    const fixture = setup(fake);
    enableIntegrations(fixture, [
      { id: 'Jira', oauth: false },
      { id: 'AzureDevOps', oauth: false },
    ]);
    openConnectPanel(fixture);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Azure DevOps');
  });

  it('shows the OAuth log-in button only when the selected provider has OAuth', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(organiserSession());
    const fixture = setup(fake);
    enableIntegrations(fixture, [{ id: 'Jira', oauth: true }]);
    openConnectPanel(fixture);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Log in to Jira');
  });

  // The submit control is derived purely from the session snapshot (organiser + revealed + a linked
  // ticket with a usable points field) so it survives a page refresh — no local connect signal needed.
  function revealedWithLinkedTicket(organiserUserId: string | null) {
    return snap({
      organiserUserId,
      state: 'Revealed',
      participants: [
        participant({ userId: ME, isOrganiser: organiserUserId === ME, role: 'Observer' }),
        participant({ userId: 'bob', displayName: 'Bob', hasVoted: true, vote: '5' }),
      ],
      stats: { average: 5, consensus: true, voteCount: 1, distribution: [{ value: '5', count: 1 }], min: 5, max: 5, stdDev: 0, outlierValues: [] },
      integration: {
        provider: 'Jira' as const,
        linkedIssue: { key: 'PROJ-7', title: 'Login', description: null, url: 'u', storyPoints: null, storyPointsFieldAvailable: true },
        queue: [],
      },
    });
  }

  it('shows the submit-points control from the snapshot alone (survives refresh, no connect signal)', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(revealedWithLinkedTicket(ME));
    const fixture = setup(fake);
    enableIntegrations(fixture);
    // Deliberately do NOT set connectedAccount — proves the button no longer depends on it.

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Submit points');
  });

  it('hides the submit-points control from a non-organiser', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(revealedWithLinkedTicket('someone-else'));
    const fixture = setup(fake);
    enableIntegrations(fixture);

    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Submit points');
  });

  it('pre-fills the submit value with the deck card nearest the average (no consensus); ties round up', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({
        organiserUserId: ME,
        state: 'Revealed',
        cards: ['1', '2', '3', '5', '8', '13', '?', '☕'],
        participants: [
          participant({ userId: ME, isOrganiser: true, role: 'Observer' }),
          participant({ userId: 'bob', hasVoted: true, vote: '5' }),
          participant({ userId: 'cara', hasVoted: true, vote: '8' }),
        ],
        // average 6.5 → nearest cards are 5 and 8 (tie) → higher wins → 8.
        stats: { average: 6.5, consensus: false, voteCount: 2, distribution: [], min: 5, max: 8, stdDev: 1.5, outlierValues: [] },
        integration: {
          provider: 'Jira',
          linkedIssue: { key: 'PROJ-7', title: 'X', description: null, url: 'u', storyPoints: null, storyPointsFieldAvailable: true },
          queue: [],
        },
      }),
    );
    const fixture = setup(fake);
    const c = fixture.componentInstance as unknown as { suggestedPoints(): number | null };

    expect(c.suggestedPoints()).toBe(8);
  });

  describe('auto-reconnect on session start', () => {
    const SAVED = { provider: 'Jira', baseUrl: 'https://x.atlassian.net', email: 'ada@x.io', token: 'tok', storyPointsField: null };
    afterEach(() => localStorage.removeItem('pp.tracker'));

    async function organiserWithSaved(saved: unknown = SAVED): Promise<{ fake: FakeRealtimeClient; c: any }> {
      localStorage.setItem('pp.tracker', JSON.stringify(saved));
      const fake = new FakeRealtimeClient();
      fake.session.set(snap({ organiserUserId: ME, participants: [participant({ userId: ME, isOrganiser: true })] }));
      const fixture = setup(fake);
      enableIntegrations(fixture); // Jira enabled
      const c = fixture.componentInstance as any;
      await c.maybeAutoReconnect();
      return { fake, c };
    }

    it('silently restores a remembered connection for the organiser', async () => {
      const { fake, c } = await organiserWithSaved();
      expect(fake.connectTracker).toHaveBeenCalledWith(CODE, ME, 'Jira', SAVED.baseUrl, SAVED.email, SAVED.token, null);
      expect(c.connectedAccount()).toBe('Ada');
    });

    it('only attempts once per page visit', async () => {
      const { fake, c } = await organiserWithSaved();
      await c.maybeAutoReconnect();
      expect(fake.connectTracker).toHaveBeenCalledTimes(1);
    });

    it('does not auto-reconnect for a non-organiser', async () => {
      localStorage.setItem('pp.tracker', JSON.stringify(SAVED));
      const fake = new FakeRealtimeClient();
      fake.session.set(snap({ organiserUserId: 'someone-else' }));
      const fixture = setup(fake);
      enableIntegrations(fixture);
      await (fixture.componentInstance as any).maybeAutoReconnect();
      expect(fake.connectTracker).not.toHaveBeenCalled();
    });

    it('does not auto-reconnect when the saved provider is no longer enabled', async () => {
      const { fake } = await organiserWithSaved({ ...SAVED, provider: 'AzureDevOps' }); // only Jira enabled
      expect(fake.connectTracker).not.toHaveBeenCalled();
    });

    it('stays quiet when there is no remembered connection', async () => {
      const fake = new FakeRealtimeClient();
      fake.session.set(snap({ organiserUserId: ME }));
      const fixture = setup(fake);
      enableIntegrations(fixture);
      await (fixture.componentInstance as any).maybeAutoReconnect();
      expect(fake.connectTracker).not.toHaveBeenCalled();
    });
  });

  it('uses the consensus value as the submit suggestion when unanimous', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(revealedWithLinkedTicket(ME)); // consensus true, average 5
    const fixture = setup(fake);
    const c = fixture.componentInstance as unknown as { suggestedPoints(): number | null };

    expect(c.suggestedPoints()).toBe(5);
  });

  it('never overwrites the value the organiser typed when stats shift after reveal', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(revealedWithLinkedTicket(ME)); // pre-fills the box with the suggestion (5)
    const fixture = setup(fake);
    const c = fixture.componentInstance as unknown as {
      pointsToSubmit: { (): number | null; set(v: number | null): void };
    };
    expect(c.pointsToSubmit()).toBe(5);

    // Organiser overrides the suggestion.
    c.pointsToSubmit.set(13);

    // A post-reveal vote change shifts the average/suggestion…
    fake.session.set({
      ...revealedWithLinkedTicket(ME),
      stats: { average: 2, consensus: false, voteCount: 2, distribution: [], min: 1, max: 3, stdDev: 1, outlierValues: [] },
    });
    fixture.detectChanges();

    // …but the organiser's typed value stands.
    expect(c.pointsToSubmit()).toBe(13);
  });

  it('never overwrites the value the organiser typed when stats shift after reveal', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(revealedWithLinkedTicket(ME)); // pre-fills the box with the suggestion (5)
    const fixture = setup(fake);
    const c = fixture.componentInstance as unknown as {
      pointsToSubmit: { (): number | null; set(v: number | null): void };
    };
    expect(c.pointsToSubmit()).toBe(5);

    // Organiser overrides the suggestion.
    c.pointsToSubmit.set(13);

    // A post-reveal vote change shifts the average/suggestion…
    fake.session.set({
      ...revealedWithLinkedTicket(ME),
      stats: { average: 2, consensus: false, voteCount: 2, distribution: [], min: 1, max: 3, stdDev: 1, outlierValues: [] },
    });
    fixture.detectChanges();

    // …but the organiser's typed value stands.
    expect(c.pointsToSubmit()).toBe(13);
  });

  it('disables submit entirely for a non-numeric deck (e.g. T-shirt sizes)', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({
        organiserUserId: ME,
        state: 'Revealed',
        deckType: 'TShirt',
        cards: ['XS', 'S', 'M', 'L', 'XL', '?', '☕'],
        participants: [
          participant({ userId: ME, isOrganiser: true, role: 'Observer' }),
          participant({ userId: 'bob', hasVoted: true, vote: 'M' }),
        ],
        stats: { average: null, consensus: false, voteCount: 1, distribution: [], min: null, max: null, stdDev: null, outlierValues: [] },
        integration: {
          provider: 'Jira',
          linkedIssue: { key: 'PROJ-7', title: 'X', description: null, url: 'u', storyPoints: null, storyPointsFieldAvailable: true },
          queue: [],
        },
      }),
    );
    const fixture = setup(fake);
    enableIntegrations(fixture);
    const c = fixture.componentInstance as unknown as { isNumericDeck(): boolean };

    expect(c.isNumericDeck()).toBe(false);
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Submit points');
  });

  it('renders the ticket queue and selects an item on click', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({
        organiserUserId: ME,
        participants: [participant({ userId: ME, isOrganiser: true, role: 'Observer' })],
        integration: {
          provider: 'Jira',
          linkedIssue: null,
          queue: [
            { key: 'PROJ-1', title: 'First', status: 'To Do', storyPoints: null, url: 'u1', isSelected: false },
            { key: 'PROJ-2', title: 'Second', status: 'Doing', storyPoints: 5, url: 'u2', isSelected: true },
          ],
        },
      }),
    );
    fake.selectQueueItem = vi.fn().mockResolvedValue({ status: 'Ok', session: null });
    const fixture = setup(fake);
    enableIntegrations(fixture);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('PROJ-1');
    expect(text).toContain('PROJ-2');

    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('.list-group-item-action');
    const firstRow = [...rows].find((r) => r.textContent?.includes('PROJ-1')) as HTMLElement;
    firstRow.click();
    expect(fake.selectQueueItem).toHaveBeenCalledWith(CODE, ME, 'PROJ-1');
  });

  it('shows the session-ended message when closed', () => {
    const fake = new FakeRealtimeClient();
    fake.closed.set(true);
    const fixture = setup(fake);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('This session has ended');
  });

  it('renders revealed vote values, the edited marker, and stats', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({
        state: 'Revealed',
        participants: [
          participant({ userId: ME, displayName: 'Me', hasVoted: true, vote: '5' }),
          participant({ userId: 'bob', displayName: 'Bob', hasVoted: true, vote: '13', changedAfterReveal: true }),
        ],
        stats: { average: 9, consensus: false, voteCount: 2, distribution: [{ value: '5', count: 1 }, { value: '13', count: 1 }], min: 5, max: 13, stdDev: 4, outlierValues: [] },
      }),
    );
    const fixture = setup(fake);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('edited');
    expect(text).toContain('Average');
    expect(text).toContain('9');
  });

  it('highlights outliers and lists them in the results callout', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({
        state: 'Revealed',
        participants: [
          participant({ userId: ME, displayName: 'Me', hasVoted: true, vote: '3' }),
          participant({ userId: 'carol', displayName: 'Carol', hasVoted: true, vote: '5' }),
          participant({ userId: 'dave', displayName: 'Dave', hasVoted: true, vote: '13', isOutlier: true }),
        ],
        stats: {
          average: 7, consensus: false, voteCount: 3,
          distribution: [{ value: '3', count: 1 }, { value: '5', count: 1 }, { value: '13', count: 1 }],
          min: 3, max: 13, stdDev: 4.3, outlierValues: ['13'],
        },
      }),
    );
    const fixture = setup(fake);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('outlier');
    expect(text).toContain('Discuss outliers');
    expect(text).toContain('Dave (13)');
  });

  it('sends an emoji reaction when a reaction button is clicked', () => {
    const fake = new FakeRealtimeClient();
    const fixture = setup(fake);

    const btn = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button.reaction-btn')]
      .find((b) => b.textContent?.trim() === '🎉') as HTMLButtonElement;
    expect(btn).toBeTruthy();
    btn.click();

    expect(fake.react).toHaveBeenCalledWith('🎉');
  });

  it('floats a reaction received from the session group', () => {
    const fake = new FakeRealtimeClient();
    const fixture = setup(fake);

    fake.reactions$.next({ userId: 'bob', emoji: '🚀' });
    fixture.detectChanges();

    const floats = (fixture.nativeElement as HTMLElement).querySelectorAll('.reaction-float');
    expect(floats.length).toBe(1);
    expect(floats[0].textContent?.trim()).toBe('🚀');
  });

  it('hides the reaction bar when reactions are disabled for the session', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(snap({ reactionsEnabled: false }));
    const fixture = setup(fake);

    const buttons = (fixture.nativeElement as HTMLElement).querySelectorAll('button.reaction-btn');
    expect(buttons.length).toBe(0);
  });

  it('lets the organiser toggle reactions off', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({
        organiserUserId: ME,
        reactionsEnabled: true,
        participants: [participant({ userId: ME, isOrganiser: true, role: 'Observer' })],
      }),
    );
    const fixture = setup(fake);
    openModal(fixture, 'settings');

    const toggle = (fixture.nativeElement as HTMLElement).querySelector('#reactionsEnabled') as HTMLInputElement;
    expect(toggle).toBeTruthy();
    toggle.click();

    expect(fake.setReactionsEnabled).toHaveBeenCalledWith(CODE, ME, false);
  });

  it('shows a running countdown to everyone', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({ timerDurationSeconds: 120, timerDeadline: new Date(Date.now() + 65_000).toISOString() }),
    );
    const fixture = setup(fake);

    const mono = (fixture.nativeElement as HTMLElement).querySelector('.font-monospace');
    expect(mono).toBeTruthy();
    expect(mono!.textContent?.trim()).toMatch(/^1:0\d$/); // ~1:05 remaining
  });

  it('shows "Paused" when the timer is paused', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(snap({ timerDurationSeconds: 120, timerPausedRemainingSeconds: 40 }));
    const fixture = setup(fake);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Paused');
    expect(text).toContain('0:40');
  });

  it('lets the organiser start the timer with the selected duration', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({
        organiserUserId: ME,
        timerDurationSeconds: 60,
        participants: [participant({ userId: ME, isOrganiser: true, role: 'Observer' })],
      }),
    );
    const fixture = setup(fake);

    const startBtn = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')]
      .find((b) => b.textContent?.trim() === 'Start') as HTMLButtonElement;
    expect(startBtn).toBeTruthy();
    startBtn.click();

    expect(fake.startTimer).toHaveBeenCalledWith(CODE, ME, 60);
  });

  it('lets the organiser pause a running timer', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({
        organiserUserId: ME,
        timerDurationSeconds: 60,
        timerDeadline: new Date(Date.now() + 30_000).toISOString(),
        participants: [participant({ userId: ME, isOrganiser: true, role: 'Observer' })],
      }),
    );
    const fixture = setup(fake);

    const pauseBtn = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')]
      .find((b) => b.textContent?.trim() === 'Pause') as HTMLButtonElement;
    expect(pauseBtn).toBeTruthy();
    pauseBtn.click();

    expect(fake.pauseTimer).toHaveBeenCalledWith(CODE, ME);
  });

  it('shows the deck switcher in the organiser settings modal', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({ organiserUserId: ME, participants: [participant({ userId: ME, isOrganiser: true, role: 'Observer' })] }),
    );
    const fixture = setup(fake);
    openModal(fixture, 'settings');

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Card deck');
    expect(text).toContain('Change deck');
  });

  it('changes the deck via the organiser deck switcher (resets the round server-side)', async () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({ organiserUserId: ME, participants: [participant({ userId: ME, isOrganiser: true, role: 'Observer' })] }),
    );
    const fixture = setup(fake);
    const ci = fixture.componentInstance as unknown as {
      startEditDeck(): void;
      deckDraftType: string;
      deckDraftCustom: string;
      changeDeck(): Promise<void>;
    };

    ci.startEditDeck();
    ci.deckDraftType = 'Custom';
    ci.deckDraftCustom = '1, 2, 4';
    await ci.changeDeck();

    expect(fake.setDeck).toHaveBeenCalledWith(CODE, ME, 'Custom', '1, 2, 4');
  });

  // --- Top-right menu + modals ---
  it('opens the invite modal from the menu with the share link', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(snap());
    const fixture = setup(fake);

    openMenu(fixture);
    const inviteItem = [...(fixture.nativeElement as HTMLElement).querySelectorAll('.dropdown-item')]
      .find((b) => b.textContent?.trim() === 'Invite others') as HTMLButtonElement;
    expect(inviteItem).toBeTruthy();
    inviteItem.click();
    fixture.detectChanges();

    const modal = (fixture.nativeElement as HTMLElement).querySelector('.modal');
    expect(modal).toBeTruthy();
    const input = modal!.querySelector('input') as HTMLInputElement;
    expect(input.value).toContain('/join/');
    expect(modal!.textContent).toContain('Copy invite link');
  });

  it('shows only the Invite item to a non-organiser', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({
        organiserUserId: 'someone-else',
        participants: [participant({ userId: ME }), participant({ userId: 'someone-else', isOrganiser: true })],
      }),
    );
    const fixture = setup(fake);
    enableIntegrations(fixture); // even with integrations on, a non-organiser can't manage them

    openMenu(fixture);
    const items = [...(fixture.nativeElement as HTMLElement).querySelectorAll('.dropdown-item')].map((b) =>
      b.textContent?.trim(),
    );
    expect(items).toEqual(['Invite others']);
  });

  it('renders the observing notice above the participants list in the right column', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(snap({ participants: [participant({ userId: ME, role: 'Observer' })] }));
    const fixture = setup(fake);

    const html = fixture.nativeElement as HTMLElement;
    const notice = [...html.querySelectorAll('.alert')].find((a) => a.textContent?.includes("You're observing"));
    const participantsCard = [...html.querySelectorAll('.card')].find((c) =>
      c.querySelector('.card-header')?.textContent?.includes('Participants'),
    );
    expect(notice).toBeTruthy();
    expect(participantsCard).toBeTruthy();
    // The notice precedes the participants card in document order.
    expect(notice!.compareDocumentPosition(participantsCard!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  // --- Mid-session role changes ---
  it('lets an observer become a voter when role changes are allowed', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(snap({ allowRoleChange: true, participants: [participant({ userId: ME, role: 'Observer' })] }));
    const fixture = setup(fake);

    const btn = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')]
      .find((b) => b.textContent?.trim() === 'Join as voter') as HTMLButtonElement;
    expect(btn).toBeTruthy();
    btn.click();

    expect(fake.changeRole).toHaveBeenCalledWith(CODE, ME, ME, 'Voter');
  });

  it('hides the self role switch for a non-organiser when role changes are disabled', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({
        allowRoleChange: false,
        organiserUserId: 'someone-else',
        participants: [participant({ userId: ME, role: 'Observer' }), participant({ userId: 'someone-else', isOrganiser: true })],
      }),
    );
    const fixture = setup(fake);

    const btn = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')]
      .find((b) => b.textContent?.trim() === 'Join as voter');
    expect(btn).toBeUndefined();
  });

  it('lets a voter switch to observer', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(snap({ allowRoleChange: true, participants: [participant({ userId: ME, role: 'Voter' })] }));
    const fixture = setup(fake);

    const btn = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')]
      .find((b) => b.textContent?.trim() === 'Switch to observer') as HTMLButtonElement;
    expect(btn).toBeTruthy();
    btn.click();

    expect(fake.changeRole).toHaveBeenCalledWith(CODE, ME, ME, 'Observer');
  });

  it('lets the organiser flip another participant’s role', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({
        organiserUserId: ME,
        participants: [
          participant({ userId: ME, isOrganiser: true, role: 'Observer' }),
          participant({ userId: 'bob', displayName: 'Bob', role: 'Voter' }),
        ],
      }),
    );
    const fixture = setup(fake);

    const btn = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button[title]')]
      .find((b) => b.getAttribute('title') === 'Make observer') as HTMLButtonElement;
    expect(btn).toBeTruthy();
    btn.click();

    expect(fake.changeRole).toHaveBeenCalledWith(CODE, ME, 'bob', 'Observer');
  });

  it('lets the organiser toggle allow-role-change in settings', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({ organiserUserId: ME, allowRoleChange: true, participants: [participant({ userId: ME, isOrganiser: true, role: 'Observer' })] }),
    );
    const fixture = setup(fake);
    openModal(fixture, 'settings');

    const toggle = (fixture.nativeElement as HTMLElement).querySelector('#allowRoleChange') as HTMLInputElement;
    expect(toggle).toBeTruthy();
    toggle.click();

    expect(fake.setAllowRoleChange).toHaveBeenCalledWith(CODE, ME, false);
  });

  // --- Close & delete ---
  it('shows a read-only banner and hides controls when the session is closed', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({ isClosed: true, organiserUserId: ME, participants: [participant({ userId: ME, isOrganiser: true, role: 'Voter' })] }),
    );
    const fixture = setup(fake);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('closed');
    expect(text).not.toContain('Organiser controls');
    // No deck buttons and no reaction bar on a closed session.
    const cardButtons = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].filter(
      (b) => b.textContent?.trim() === '3',
    );
    expect(cardButtons.length).toBe(0);
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('button.reaction-btn').length).toBe(0);
  });

  it('lets the organiser close the session (read-only) via the danger modal', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({ organiserUserId: ME, participants: [participant({ userId: ME, isOrganiser: true, role: 'Observer' })] }),
    );
    const fixture = setup(fake);
    openModal(fixture, 'danger');

    const el = fixture.nativeElement as HTMLElement;
    ([...el.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Close session (read-only)') as HTMLButtonElement).click();
    fixture.detectChanges();
    ([...el.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Yes, close') as HTMLButtonElement).click();

    expect(fake.closeSession).toHaveBeenCalledWith(CODE, ME);
  });

  it('lets the organiser delete the session via the danger modal', () => {
    const fake = new FakeRealtimeClient();
    fake.session.set(
      snap({ organiserUserId: ME, participants: [participant({ userId: ME, isOrganiser: true, role: 'Observer' })] }),
    );
    const fixture = setup(fake);
    openModal(fixture, 'danger');

    const el = fixture.nativeElement as HTMLElement;
    ([...el.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Delete session') as HTMLButtonElement).click();
    fixture.detectChanges();
    ([...el.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Yes, delete') as HTMLButtonElement).click();

    expect(fake.deleteSession).toHaveBeenCalledWith(CODE, ME);
  });

  it('shows the ended screen when the session is closed/deleted on the server', () => {
    const fake = new FakeRealtimeClient();
    const fixture = setup(fake);
    fake.closed.set(true); // mirrors the SessionClosed broadcast
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('This session has ended');
  });
});
