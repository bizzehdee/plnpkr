import { Component, computed, effect, inject, signal, HostListener, OnInit, OnDestroy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SignalrRealtimeClient } from '../../core/realtime.client';
import { IdentityService } from '../../core/identity.service';
import { TrackerStorageService } from '../../core/tracker-storage.service';
import { resolveApiBase } from '../../core/app-config';
import { DECK_LABELS, DeckType, IntegrationProvider, ParticipantInfo, ParticipantRole, REACTION_EMOJI, SavedDeck } from '../../core/models';
import { DeckStorageService } from '../../core/deck-storage.service';
import { RevealCueService } from '../../core/reveal-cue.service';

@Component({
  selector: 'app-session',
  imports: [DecimalPipe, RouterLink, FormsModule],
  templateUrl: './session.page.html',
  styleUrl: './session.page.scss',
})
export class SessionPage implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly realtime = inject(SignalrRealtimeClient);
  private readonly identity = inject(IdentityService);
  private readonly revealCue = inject(RevealCueService);

  // --- Reveal cue (#2): sound + a brief visual flash when votes are revealed ---
  /** Whether the reveal sound is muted (per-browser, persisted). */
  protected readonly soundMuted = this.revealCue.muted;
  /** Pulses true briefly when the round transitions to Revealed, driving the flash animation. */
  protected readonly revealFlash = signal(false);
  /** Tracks the previous session state so we can fire the cue only on the Voting → Revealed edge. */
  private wasRevealed = false;
  /** Set once the first snapshot is observed, so we never fire the cue on initial load. */
  private cuePrimed = false;
  private revealFlashTimer?: ReturnType<typeof setTimeout>;

  protected toggleSoundMuted(): void {
    this.revealCue.toggleMute();
  }

  /** Briefly raises the flash flag; the CSS animation self-disables under prefers-reduced-motion. */
  private flashReveal(): void {
    this.revealFlash.set(true);
    if (this.revealFlashTimer !== undefined) clearTimeout(this.revealFlashTimer);
    this.revealFlashTimer = setTimeout(() => this.revealFlash.set(false), 900);
  }

  protected shortCode = '';
  protected readonly myUserId = this.identity.userId;
  protected readonly session = this.realtime.session;
  protected readonly closed = this.realtime.closed;
  protected readonly copied = signal(false);

  // --- Top-right menu + modals (#30, #26) ---
  protected readonly menuOpen = signal(false);
  /** Which modal is open, if any. */
  protected readonly activeModal = signal<'invite' | 'tracker' | 'settings' | 'danger' | null>(null);

  /** True once the session has been closed (read-only, #26). */
  protected readonly isClosed = computed(() => this.session()?.isClosed ?? false);

  protected toggleMenu(): void {
    this.menuOpen.update((v) => !v);
  }

  protected openModal(modal: 'invite' | 'tracker' | 'settings' | 'danger'): void {
    this.activeModal.set(modal);
    this.menuOpen.set(false);
  }

  protected closeModal(): void {
    this.activeModal.set(null);
  }

  /** Esc closes an open modal first, otherwise the menu. */
  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    if (this.activeModal() !== null) {
      this.activeModal.set(null);
    } else if (this.menuOpen()) {
      this.menuOpen.set(false);
    }
  }

  // --- Close & delete (organiser, #26) — each behind an inline confirm ---
  protected readonly confirmingClose = signal(false);
  protected readonly confirmingDelete = signal(false);

  protected async closeSession(): Promise<void> {
    await this.realtime.closeSession(this.shortCode, this.myUserId);
    this.confirmingClose.set(false);
    this.activeModal.set(null);
  }

  /** Soft-delete: on success the server broadcasts SessionClosed → the "ended" screen renders. */
  protected deleteSession(): Promise<unknown> {
    return this.realtime.deleteSession(this.shortCode, this.myUserId);
  }

  /** Locally-remembered selection so the voter sees their pick highlighted while votes are hidden. */
  protected readonly selectedCard = signal<string | null>(null);

  // --- Emoji reactions (ephemeral, #17) ---
  protected readonly reactionEmoji = REACTION_EMOJI;
  /** Reactions currently floating on screen; each auto-removed after its animation. */
  protected readonly floatingReactions = signal<{ id: number; emoji: string }[]>([]);
  private reactionSeq = 0;

  /** Whether reactions are currently allowed in this session (organiser-toggleable, #17). */
  protected readonly reactionsEnabled = computed(() => this.session()?.reactionsEnabled ?? false);

  protected sendReaction(emoji: string): void {
    void this.realtime.react(emoji);
  }

  /** Organiser toggles reactions on/off mid-session (#17). */
  protected toggleReactions(): Promise<unknown> {
    return this.realtime.setReactionsEnabled(this.shortCode, this.myUserId, !this.session()?.reactionsEnabled);
  }

  // --- Mid-session role changes (#21) ---
  protected readonly allowRoleChange = computed(() => this.session()?.allowRoleChange ?? false);

  /** I may switch my own role when the organiser allows it, or if I'm the organiser. */
  protected readonly canChangeOwnRole = computed(() => !!this.me() && (this.allowRoleChange() || this.canControl()));

  /** Organiser toggles whether participants may switch their own role (#21). */
  protected toggleAllowRoleChange(): Promise<unknown> {
    return this.realtime.setAllowRoleChange(this.shortCode, this.myUserId, !this.session()?.allowRoleChange);
  }

  /** Switch my own role. */
  protected setMyRole(role: ParticipantRole): Promise<unknown> {
    return this.realtime.changeRole(this.shortCode, this.myUserId, this.myUserId, role);
  }

  /** Organiser flips another participant's role (Voter ↔ Observer). */
  protected toggleParticipantRole(p: ParticipantInfo): Promise<unknown> {
    const next: ParticipantRole = p.role === 'Voter' ? 'Observer' : 'Voter';
    return this.realtime.changeRole(this.shortCode, this.myUserId, p.userId, next);
  }

  // --- Round timer (#14) ---
  /** Ticks ~2×/sec so the countdown re-renders against the server deadline. */
  private readonly nowMs = signal(Date.now());
  private timerTick?: ReturnType<typeof setInterval>;

  protected readonly timerOptions: { value: number; label: string }[] = [
    { value: 30, label: '0:30' },
    { value: 60, label: '1:00' },
    { value: 120, label: '2:00' },
    { value: 300, label: '5:00' },
  ];
  /** Organiser's selected duration (seconds) for start/restart; seeded from the session on init. */
  protected timerSelection = 60;

  protected readonly timerDeadline = computed(() => this.session()?.timerDeadline ?? null);
  protected readonly timerPausedRemaining = computed(() => this.session()?.timerPausedRemainingSeconds ?? null);
  protected readonly timerRunning = computed(() => this.timerDeadline() !== null);
  protected readonly timerPaused = computed(() => this.timerPausedRemaining() !== null);
  protected readonly timerActive = computed(() => this.timerRunning() || this.timerPaused());

  /** Seconds left: counts down while running, frozen while paused, null when idle. */
  protected readonly timerRemaining = computed<number | null>(() => {
    const dl = this.timerDeadline();
    if (dl !== null) {
      return Math.max(0, Math.ceil((new Date(dl).getTime() - this.nowMs()) / 1000));
    }
    return this.timerPausedRemaining();
  });

  /** "m:ss" for the countdown, or '' when idle. */
  protected readonly timerDisplay = computed(() => {
    const s = this.timerRemaining();
    if (s === null) return '';
    return `${Math.floor(s / 60)}:${(s % 60).toString().padStart(2, '0')}`;
  });

  /** A running timer that has hit zero — the server reveal is imminent. */
  protected readonly timerExpired = computed(() => this.timerRunning() && (this.timerRemaining() ?? 0) <= 0);

  /** Start or restart the timer with the selected duration (also persists it as the configured value). */
  protected startTimer(): Promise<unknown> {
    return this.realtime.startTimer(this.shortCode, this.myUserId, this.timerSelection);
  }

  protected pauseTimer(): Promise<unknown> {
    return this.realtime.pauseTimer(this.shortCode, this.myUserId);
  }

  protected resumeTimer(): Promise<unknown> {
    return this.realtime.resumeTimer(this.shortCode, this.myUserId);
  }

  protected stopTimer(): Promise<unknown> {
    return this.realtime.stopTimer(this.shortCode, this.myUserId);
  }

  /** Persist a duration change made via the dropdown (changeable mid-session even without starting). */
  protected onTimerDurationChange(): Promise<unknown> {
    return this.realtime.setTimerDuration(this.shortCode, this.myUserId, this.timerSelection);
  }

  // --- Large-group mode (#6): aggregate counts + a collapsible participant list ---
  /** Above this many participants the list collapses to a head slice behind a "show all" toggle. */
  protected readonly largeGroupThreshold = 25;

  /** Voting/observer tally for the summary header (voted-of-voters, observer count). Builds on #1. */
  protected readonly tally = computed(() => {
    const ps = this.session()?.participants ?? [];
    const voters = ps.filter((p) => p.role === 'Voter');
    return {
      voted: voters.filter((v) => v.hasVoted).length,
      voters: voters.length,
      observers: ps.filter((p) => p.role === 'Observer').length,
      total: ps.length,
    };
  });

  protected readonly isLargeGroup = computed(
    () => (this.session()?.participants.length ?? 0) > this.largeGroupThreshold,
  );
  /** Whether the full list is expanded in large-group mode. */
  protected readonly showAllParticipants = signal(false);

  /** Participants to render: the full list, or a head slice when collapsed in large-group mode. */
  protected readonly visibleParticipants = computed(() => {
    const ps = this.session()?.participants ?? [];
    if (!this.isLargeGroup() || this.showAllParticipants()) return ps;
    return ps.slice(0, this.largeGroupThreshold);
  });

  protected toggleShowAllParticipants(): void {
    this.showAllParticipants.update((v) => !v);
  }

  protected readonly me = computed(() =>
    this.session()?.participants.find((p) => p.userId === this.myUserId) ?? null,
  );
  protected readonly isObserver = computed(() => this.me()?.role === 'Observer');
  /** True whenever the cards are on screen — the Revealed state and the post-reveal Discussion phase (#9). */
  protected readonly revealed = computed(() => {
    const s = this.session()?.state;
    return s === 'Revealed' || s === 'Discussion';
  });
  /** The timed talk-it-through phase (#9): cards stay visible, the deck is hidden until the re-vote. */
  protected readonly inDiscussion = computed(() => this.session()?.state === 'Discussion');

  protected startDiscussion(): Promise<unknown> {
    return this.realtime.startDiscussion(this.shortCode, this.myUserId, this.timerSelection);
  }

  protected endDiscussion(): Promise<unknown> {
    return this.realtime.endDiscussion(this.shortCode, this.myUserId);
  }

  /**
   * Reveal/reset controls: any organiser (founding or promoted co-organiser, #7), or anyone when the
   * session has no organiser at all (the "open session" rule, #10).
   */
  protected readonly canControl = computed(() => {
    const s = this.session();
    const me = this.me();
    if (!s || !me) return false;
    const hasOrganiser = s.organiserUserId != null || s.participants.some((p) => p.isOrganiser);
    return !hasOrganiser || me.isOrganiser || s.organiserUserId === this.myUserId;
  });

  // --- Multiple organisers / hand-off (#7) ---
  protected promote(targetUserId: string): Promise<unknown> {
    return this.realtime.promoteToOrganiser(this.shortCode, this.myUserId, targetUserId);
  }

  protected demote(targetUserId: string): Promise<unknown> {
    return this.realtime.demoteOrganiser(this.shortCode, this.myUserId, targetUserId);
  }

  protected transferOrganiser(targetUserId: string): Promise<unknown> {
    return this.realtime.transferOrganiser(this.shortCode, this.myUserId, targetUserId);
  }

  protected readonly deckLabel = computed(() => {
    const s = this.session();
    return s ? DECK_LABELS[s.deckType] : '';
  });

  protected readonly inviteLink = computed(
    () => `${window.location.origin}/join/${this.shortCode}`,
  );

  /** Participants flagged as outliers on reveal — the people to discuss with. */
  protected readonly outliers = computed(() =>
    (this.session()?.participants ?? []).filter((p) => p.isOutlier),
  );

  /**
   * True when the cards are revealed but the team did NOT reach consensus — the cue to discuss and
   * re-vote (#8). Covers both statistical outliers and a plain split with no single outlier.
   */
  protected readonly hasDisagreement = computed(() => {
    const stats = this.session()?.stats;
    return this.revealed() && !!stats && !stats.consensus && stats.voteCount > 1;
  });

  /** "Dave (13), Bob (2)" — for the results callout. */
  protected readonly outlierSummary = computed(() =>
    this.outliers()
      .map((p) => `${p.displayName} (${p.vote})`)
      .join(', '),
  );

  // --- Issue-tracker integration (#4) ---
  private readonly trackerStorage = inject(TrackerStorageService);
  private readonly http = inject(HttpClient);

  /** Providers enabled server-side, each with whether it has OAuth configured (#43). Empty ⇒ the
   *  whole integration UI is hidden (no provider enabled). The connect form lists only these. */
  protected readonly enabledProviders = signal<{ id: IntegrationProvider; oauth: boolean }[]>([]);

  /** True when at least one provider is enabled — gates the entire integration UI. */
  protected readonly integrationsEnabled = computed(() => this.enabledProviders().length > 0);

  /** Display label for a provider id. */
  protected providerLabel(p: IntegrationProvider): string {
    return p === 'Jira' ? 'Jira' : 'Azure DevOps';
  }

  /** Whether the currently-selected provider offers OAuth ("Log in"). */
  protected selectedProviderHasOAuth(): boolean {
    return this.enabledProviders().find((p) => p.id === this.trackerProvider)?.oauth ?? false;
  }

  protected readonly integration = computed(() => this.session()?.integration ?? null);
  protected readonly linkedIssue = computed(() => this.integration()?.linkedIssue ?? null);
  protected readonly queue = computed(() => this.integration()?.queue ?? []);

  /** The single title shown below the emoji bar: the manual story, or the linked ticket's title when
   *  none is set. The ticket link + current points render alongside it (not in the description). */
  protected readonly storyTitle = computed(() => this.session()?.currentStory ?? this.linkedIssue()?.title ?? null);

  /** Single input: one or more ticket IDs, or a board/query URL. */
  protected ticketInput = '';
  protected readonly connectedAccount = signal<string | null>(null);
  protected readonly savedConnection = signal(this.trackerStorage.get());

  protected showConnectPanel = false;
  protected trackerProvider: IntegrationProvider = 'Jira';
  protected trackerBaseUrl = '';
  protected trackerEmail = '';
  protected trackerToken = '';
  protected trackerStoryPointsField = ''; // optional manual story-points field id/name (#41)
  protected rememberTracker = true;

  protected readonly integrationBusy = signal(false);
  protected readonly integrationError = signal<string | null>(null);
  protected readonly submitSuccess = signal<string | null>(null);

  /** True while a silent auto-reconnect (on session start) is in flight. */
  protected readonly autoReconnecting = signal(false);
  /** One-shot guard so the auto-reconnect only fires once per page visit. */
  private autoReconnectAttempted = false;

  /** The deck's numeric cards (½ = 0.5; non-numeric like ?, ☕, T-shirt sizes excluded). */
  protected readonly deckNumericCards = computed(() =>
    (this.session()?.cards ?? [])
      .map((c) => this.parseCard(c))
      .filter((n): n is number => n !== null),
  );

  /** Submit is only meaningful for decks with numeric cards. */
  protected readonly isNumericDeck = computed(() => this.deckNumericCards().length > 0);

  /**
   * Suggested story points to submit: the consensus value when the round is unanimous, otherwise
   * the deck card **nearest the average** (snapped to a real card, tie → higher).
   */
  protected readonly suggestedPoints = computed(() => {
    const stats = this.session()?.stats;
    if (!stats || stats.average === null) return null;
    return stats.consensus ? stats.average : this.closestCard(stats.average);
  });

  /** Editable value bound to the submit input; seeded from the suggestion on reveal. */
  protected readonly pointsToSubmit = signal<number | null>(null);

  /**
   * Submit is offered **only to the organiser** (same controller gate as reveal/reset), once the
   * cards are revealed, the deck is numeric, and a ticket with a usable story-points field is
   * linked. Everything is derived from the session snapshot (not the local connect signal), so the
   * button survives a page refresh / reconnect. See #24.
   */
  protected readonly canSubmitPoints = computed(() =>
    this.revealed()
    && this.canControl()
    && this.isNumericDeck()
    && !!this.linkedIssue()?.storyPointsFieldAvailable
    && this.suggestedPoints() !== null,
  );

  /** Numeric value of a card, mirroring the server (`StatsCalculator`): ½ = 0.5; null if non-numeric. */
  private parseCard(card: string): number | null {
    if (card === '½') return 0.5;
    const n = Number(card);
    return card.trim() !== '' && Number.isFinite(n) ? n : null;
  }

  /** The numeric deck card closest to `average`; ties resolve to the higher card. */
  private closestCard(average: number): number | null {
    const cards = this.deckNumericCards();
    if (cards.length === 0) return null;
    return cards.reduce((best, c) => {
      const d = Math.abs(c - average);
      const bd = Math.abs(best - average);
      return d < bd || (d === bd && c > best) ? c : best;
    });
  }

  // --- Tickets (#38) ---
  /** One input for everything: a board/query URL, or one or more ticket IDs. */
  protected async addTickets(): Promise<void> {
    this.integrationError.set(null);
    const value = this.ticketInput.trim();
    if (!value) return;
    this.integrationBusy.set(true);
    try {
      const isUrl = /^https?:\/\//i.test(value);
      const r = isUrl
        ? await this.realtime.loadQueueFromUrl(this.shortCode, this.myUserId, value)
        : await this.realtime.loadQueueFromKeys(
            this.shortCode,
            this.myUserId,
            value.split(/[\s,]+/).filter((k) => k.length > 0),
          );
      if (r.status === 'Ok') this.ticketInput = '';
      else this.integrationError.set(r.error ?? 'Could not load tickets.');
    } finally {
      this.integrationBusy.set(false);
    }
  }

  protected selectTicket(key: string): Promise<unknown> {
    return this.realtime.selectQueueItem(this.shortCode, this.myUserId, key);
  }

  protected clearQueue(): Promise<unknown> {
    return this.realtime.clearQueue(this.shortCode, this.myUserId);
  }

  /** Move to the next/previous ticket relative to the currently-selected one. */
  protected async stepQueue(delta: number): Promise<void> {
    const q = this.queue();
    if (q.length === 0) return;
    const current = q.findIndex((t) => t.isSelected);
    const next = current < 0 ? 0 : Math.min(q.length - 1, Math.max(0, current + delta));
    await this.selectTicket(q[next].key);
  }

  protected async submitStoryPoints(): Promise<void> {
    this.integrationError.set(null);
    this.submitSuccess.set(null);
    const value = this.pointsToSubmit();
    if (value === null || Number.isNaN(value)) {
      this.integrationError.set('Enter a numeric value to submit.');
      return;
    }
    this.integrationBusy.set(true);
    try {
      const result = await this.realtime.submitStoryPoints(this.shortCode, this.myUserId, value);
      if (result.status === 'Ok') {
        this.submitSuccess.set(`✓ ${value} saved to ${this.linkedIssue()?.key}`);
      } else {
        this.integrationError.set(result.error ?? 'Could not submit story points.');
      }
    } finally {
      this.integrationBusy.set(false);
    }
  }

  protected async connectTracker(): Promise<void> {
    this.integrationError.set(null);
    if (!this.trackerBaseUrl.trim() || !this.trackerToken.trim()) {
      this.integrationError.set('Base URL and token are required.');
      return;
    }
    this.integrationBusy.set(true);
    try {
      const storyPointsField = this.trackerStoryPointsField.trim() || null;
      const result = await this.realtime.connectTracker(
        this.shortCode,
        this.myUserId,
        this.trackerProvider,
        this.trackerBaseUrl.trim(),
        this.trackerEmail.trim() || null,
        this.trackerToken.trim(),
        storyPointsField,
      );
      if (result.status === 'Ok') {
        this.connectedAccount.set(result.accountName ?? 'connected');
        if (this.rememberTracker) {
          const saved = {
            provider: this.trackerProvider,
            baseUrl: this.trackerBaseUrl.trim(),
            email: this.trackerEmail.trim() || null,
            token: this.trackerToken.trim(),
            storyPointsField,
          };
          this.trackerStorage.save(saved);
          this.savedConnection.set(saved);
        }
        this.trackerToken = '';
        this.showConnectPanel = false;
      } else {
        this.integrationError.set(result.error ?? 'Could not connect.');
      }
    } finally {
      this.integrationBusy.set(false);
    }
  }

  /** Opens the provider's login in a popup; the callback posts the result back to this window. */
  protected loginWithProvider(provider: IntegrationProvider): void {
    this.integrationError.set(null);
    const apiBase = resolveApiBase();
    const slug = provider === 'Jira' ? 'jira' : 'azuredevops';
    const spField = this.trackerStoryPointsField.trim();
    const spParam = spField ? `&spField=${encodeURIComponent(spField)}` : '';
    const url = `${apiBase}/api/integrations/${slug}/connect?session=${encodeURIComponent(this.shortCode)}&userId=${encodeURIComponent(this.myUserId)}${spParam}`;
    const popup = window.open(url, 'pp-oauth', 'width=520,height=720');

    const onMessage = (event: MessageEvent) => {
      if (event.origin !== apiBase) return; // only trust the API origin
      const data = event.data;
      if (!data || typeof data !== 'object') return;
      if (data.type === 'pp-tracker-connected') {
        this.connectedAccount.set(data.accountName ?? 'connected');
        this.showConnectPanel = false;
        window.removeEventListener('message', onMessage);
        popup?.close();
      } else if (data.type === 'pp-tracker-error') {
        this.integrationError.set(data.error ?? 'Login failed.');
        window.removeEventListener('message', onMessage);
      }
    };
    window.addEventListener('message', onMessage);
  }

  protected async reconnectSaved(): Promise<void> {
    const saved = this.savedConnection();
    if (!saved) return;
    this.trackerProvider = saved.provider;
    this.trackerBaseUrl = saved.baseUrl;
    this.trackerEmail = saved.email ?? '';
    this.trackerToken = saved.token;
    this.trackerStoryPointsField = saved.storyPointsField ?? '';
    this.rememberTracker = true;
    await this.connectTracker();
  }

  protected forgetSaved(): void {
    this.trackerStorage.clear();
    this.savedConnection.set(null);
  }

  /**
   * On session start, silently re-establish a remembered tracker connection (#45). The server keeps
   * tokens in memory only, so a page reload or a server restart drops the live link even though the
   * session still shows the linked provider — this restores it without the organiser clicking "Reconnect".
   * Runs once per page visit, organiser-only (only they can connect), and only when the saved
   * provider is still enabled. Stays quiet on failure: the manual "Reconnect (saved)" button
   * remains for a retry if the saved token has expired.
   */
  private async maybeAutoReconnect(): Promise<void> {
    if (this.autoReconnectAttempted) return;
    const saved = this.savedConnection();
    if (!saved) return;
    if (!this.canControl()) return; // only the organiser can connect a tracker
    if (this.connectedAccount()) return; // already live in this browser
    if (!this.enabledProviders().some((p) => p.id === saved.provider)) return; // provider disabled
    this.autoReconnectAttempted = true;

    this.autoReconnecting.set(true);
    try {
      const result = await this.realtime.connectTracker(
        this.shortCode,
        this.myUserId,
        saved.provider,
        saved.baseUrl,
        saved.email,
        saved.token,
        saved.storyPointsField ?? null,
      );
      if (result.status === 'Ok') {
        this.connectedAccount.set(result.accountName ?? 'connected');
      }
      // Failure is intentionally silent — the saved-connection panel keeps a manual Reconnect button.
    } catch {
      /* network/server hiccup — leave the manual Reconnect affordance in place */
    } finally {
      this.autoReconnecting.set(false);
    }
  }

  protected async disconnectTracker(): Promise<void> {
    await this.realtime.disconnectTracker(this.shortCode, this.myUserId);
    this.connectedAccount.set(null);
  }

  constructor() {
    // Keep the local highlight in sync: clear it when my vote is cleared/reset, and adopt the
    // server value once revealed.
    effect(() => {
      const me = this.me();
      if (!me) return;
      if (this.revealed() && me.vote) {
        this.selectedCard.set(me.vote);
      } else if (!me.hasVoted) {
        this.selectedCard.set(null);
      }
    });

    // Fire the reveal cue (sound + flash) on the Voting → Revealed edge only — not on every
    // snapshot while already revealed, and not on first load into a revealed session. See #2.
    effect(() => {
      const s = this.session();
      if (s === null) return;
      const revealed = this.revealed();
      if (!this.cuePrimed) {
        // First snapshot: adopt its state silently so joining a revealed session is quiet.
        this.cuePrimed = true;
      } else if (revealed && !this.wasRevealed) {
        this.revealCue.playReveal();
        this.flashReveal();
      }
      this.wasRevealed = revealed;
    });

    // Modal focus management (#4): on open, remember the trigger and move focus into the dialog;
    // on close, restore focus to wherever it was. Esc-to-close already lives in onEscape().
    effect(() => {
      const modal = this.activeModal();
      if (modal) {
        this.lastFocused = (document.activeElement as HTMLElement) ?? null;
        setTimeout(() => {
          const dialog = document.querySelector<HTMLElement>('.modal .modal-content');
          const focusable = dialog?.querySelector<HTMLElement>(
            'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled])',
          );
          (focusable ?? dialog)?.focus();
        });
      } else if (this.lastFocused) {
        this.lastFocused.focus();
        this.lastFocused = null;
      }
    });

    // Drive the countdown: re-evaluate the timer signals twice a second against the server deadline.
    this.timerTick = setInterval(() => this.nowMs.set(Date.now()), 500);

    // Float each received reaction briefly, then drop it. Subscription auto-cleans on destroy.
    this.realtime.reactions$.pipe(takeUntilDestroyed()).subscribe((r) => {
      const id = ++this.reactionSeq;
      this.floatingReactions.update((list) => [...list, { id, emoji: r.emoji }]);
      setTimeout(() => this.floatingReactions.update((list) => list.filter((x) => x.id !== id)), 2200);
    });

    // Pre-fill the submit-points input from the suggestion on reveal — but ONLY when the box is empty,
    // so a value the organiser typed is never clobbered if stats shift afterwards (nothing is
    // auto-submitted; the suggestion is just the default, always overwritable). Cleared on a new round.
    effect(() => {
      if (this.revealed()) {
        if (this.pointsToSubmit() === null) {
          this.pointsToSubmit.set(this.suggestedPoints());
        }
      } else {
        this.pointsToSubmit.set(null);
        this.submitSuccess.set(null);
      }
    });
  }

  ngOnInit(): void {
    this.shortCode = this.route.snapshot.paramMap.get('shortCode') ?? '';
    const current = this.session();
    if (!current || current.shortCode !== this.shortCode) {
      this.router.navigate(['/join', this.shortCode]);
      return;
    }
    // Seed the organiser's duration picker from the session's configured value (default 1:00).
    this.timerSelection = this.session()?.timerDurationSeconds ?? 60;
    void this.loadIntegrationOptions();
  }

  ngOnDestroy(): void {
    if (this.timerTick !== undefined) {
      clearInterval(this.timerTick);
    }
    if (this.revealFlashTimer !== undefined) {
      clearTimeout(this.revealFlashTimer);
    }
  }

  private async loadIntegrationOptions(): Promise<void> {
    try {
      const options = await firstValueFrom(
        this.http.get<{ providers: { id: IntegrationProvider; oauth: boolean }[] }>(
          `${resolveApiBase()}/api/integrations/options`,
        ),
      );
      const providers = options.providers ?? [];
      this.enabledProviders.set(providers);
      // Default the connect form to the first enabled provider.
      if (providers.length > 0) {
        this.trackerProvider = providers[0].id;
      }
      // Now that we know which providers are enabled, restore a remembered connection.
      await this.maybeAutoReconnect();
    } catch {
      this.enabledProviders.set([]);
    }
  }

  protected async vote(card: string): Promise<void> {
    this.selectedCard.set(card);
    await this.realtime.castVote(this.shortCode, this.myUserId, card);
  }

  protected reveal(): Promise<unknown> {
    return this.realtime.revealVotes(this.shortCode, this.myUserId);
  }

  protected resetRound(): Promise<unknown> {
    return this.realtime.resetRound(this.shortCode, this.myUserId);
  }

  protected resetVote(targetUserId: string): Promise<unknown> {
    return this.realtime.resetVote(this.shortCode, this.myUserId, targetUserId);
  }

  protected toggleAutoReveal(): Promise<unknown> {
    return this.realtime.setAutoReveal(this.shortCode, this.myUserId, !this.session()?.autoReveal);
  }

  protected storyDraft = '';
  protected editingStory = signal(false);

  protected startEditStory(): void {
    this.storyDraft = this.session()?.currentStory ?? '';
    this.editingStory.set(true);
  }

  protected async saveStory(): Promise<void> {
    await this.realtime.setStory(this.shortCode, this.myUserId, this.storyDraft.trim() || null);
    this.editingStory.set(false);
  }

  // --- Story note (#10) — collaborative free-text justification ---
  protected noteDraft = '';
  protected readonly editingNote = signal(false);
  /** The current note from the snapshot. */
  protected readonly storyNote = computed(() => this.session()?.currentStoryNote ?? null);

  protected startEditNote(): void {
    this.noteDraft = this.session()?.currentStoryNote ?? '';
    this.editingNote.set(true);
  }

  protected async saveNote(): Promise<void> {
    await this.realtime.setStoryNote(this.shortCode, this.myUserId, this.noteDraft.trim() || null);
    this.editingNote.set(false);
  }

  // --- Mid-session deck switch (organiser only, #11) ---
  private readonly deckStorage = inject(DeckStorageService);
  protected readonly deckOptions = Object.entries(DECK_LABELS) as [DeckType, string][];
  protected readonly savedDecks = signal<SavedDeck[]>([]);
  protected readonly editingDeck = signal(false);
  protected readonly deckError = signal<string | null>(null);
  protected deckDraftType: DeckType = 'Fibonacci';
  protected deckDraftCustom = '';

  protected startEditDeck(): void {
    const s = this.session();
    this.deckDraftType = s?.deckType ?? 'Fibonacci';
    // The snapshot exposes the resolved cards (incl. ?/☕); for a custom deck, pre-fill the editable
    // list from the cards minus the always-appended non-numeric ones.
    this.deckDraftCustom = s?.deckType === 'Custom'
      ? (s?.cards ?? []).filter((c) => c !== '?' && c !== '☕').join(', ')
      : '';
    this.savedDecks.set(this.deckStorage.list());
    this.deckError.set(null);
    this.editingDeck.set(true);
  }

  protected applySavedDeckDraft(deck: SavedDeck): void {
    this.deckDraftType = 'Custom';
    this.deckDraftCustom = deck.cards;
  }

  protected async changeDeck(): Promise<void> {
    this.deckError.set(null);
    const custom = this.deckDraftType === 'Custom' ? this.deckDraftCustom.trim() : null;
    if (this.deckDraftType === 'Custom' && !custom) {
      this.deckError.set('Enter at least one custom card.');
      return;
    }
    const result = await this.realtime.setDeck(this.shortCode, this.myUserId, this.deckDraftType, custom);
    if (result.status === 'Ok') {
      this.editingDeck.set(false);
    } else if (result.status === 'InvalidDeck') {
      this.deckError.set('That deck is invalid — check the custom cards.');
    } else {
      this.deckError.set('Could not change the deck.');
    }
  }

  // --- Session password (organiser only, #2) ---
  protected passwordDraft = '';
  protected readonly passwordBusy = signal(false);
  protected readonly passwordNote = signal<string | null>(null);

  protected async savePassword(): Promise<void> {
    const value = this.passwordDraft.trim();
    if (!value) return;
    await this.setPassword(value, 'Password updated.');
  }

  protected async clearPassword(): Promise<void> {
    await this.setPassword(null, 'Password removed.');
  }

  private async setPassword(password: string | null, note: string): Promise<void> {
    this.passwordNote.set(null);
    this.passwordBusy.set(true);
    try {
      const result = await this.realtime.setPassword(this.shortCode, this.myUserId, password);
      if (result.status === 'Ok') {
        this.passwordDraft = '';
        this.passwordNote.set(note);
      }
    } finally {
      this.passwordBusy.set(false);
    }
  }

  // --- Accessibility (#4) ---

  /** Roving tabindex: only the selected card (or the first, if none) is in the tab order. */
  protected cardTabIndex(card: string, index: number): number {
    const cards = this.session()?.cards ?? [];
    const sel = this.selectedCard();
    const activeIndex = sel && cards.includes(sel) ? cards.indexOf(sel) : 0;
    return index === activeIndex ? 0 : -1;
  }

  /** Arrow/Home/End keyboard navigation across the card radiogroup; moving selects + focuses. */
  protected onCardKeydown(event: KeyboardEvent, index: number): void {
    const cards = this.session()?.cards ?? [];
    if (cards.length === 0) return;

    let target = index;
    switch (event.key) {
      case 'ArrowRight':
      case 'ArrowDown':
        target = (index + 1) % cards.length;
        break;
      case 'ArrowLeft':
      case 'ArrowUp':
        target = (index - 1 + cards.length) % cards.length;
        break;
      case 'Home':
        target = 0;
        break;
      case 'End':
        target = cards.length - 1;
        break;
      default:
        return; // let other keys (Space/Enter activate the button natively) through
    }
    event.preventDefault();
    void this.vote(cards[target]);
    document
      .querySelector<HTMLButtonElement>(`button.playing-card[data-card-index="${target}"]`)
      ?.focus();
  }

  /** A concise spoken summary announced (aria-live) the moment votes are revealed. */
  protected readonly resultsAnnouncement = computed(() => {
    if (!this.revealed()) return '';
    const stats = this.session()?.stats;
    if (!stats) return 'Votes revealed.';
    const parts = ['Votes revealed.'];
    if (stats.average !== null) parts.push(`Average ${stats.average}.`);
    if (stats.consensus) parts.push('Consensus reached.');
    else if (this.outliers().length) parts.push(`Outliers to discuss: ${this.outlierSummary()}.`);
    return parts.join(' ');
  });

  /** The element focused before a modal opened, so focus can be restored when it closes. */
  private lastFocused: HTMLElement | null = null;

  /** Keeps Tab focus inside an open modal (a lightweight focus trap). */
  protected onModalKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Tab') return;
    const container = event.currentTarget as HTMLElement;
    const focusables = [
      ...container.querySelectorAll<HTMLElement>(
        'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
      ),
    ].filter((el) => el.offsetParent !== null);
    if (focusables.length === 0) return;

    const first = focusables[0];
    const last = focusables[focusables.length - 1];
    const active = document.activeElement as HTMLElement;
    if (event.shiftKey && active === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    }
  }

  protected voteDisplay(p: ParticipantInfo): string {
    if (p.role === 'Observer') return '—';
    if (this.revealed()) return p.vote ?? '—';
    return p.hasVoted ? '✓' : '…';
  }

  /** Screen-reader label for a participant's vote pill — announces voted/waiting without leaking the value (#1). */
  protected voteAriaLabel(p: ParticipantInfo): string {
    if (p.role === 'Observer') return `${p.displayName} is observing`;
    if (this.revealed()) return `${p.displayName} voted ${p.vote ?? 'nothing'}`;
    return p.hasVoted ? `${p.displayName} has voted` : `${p.displayName} has not voted yet`;
  }

  protected async copyInvite(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.inviteLink());
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    } catch {
      /* clipboard may be blocked; input is selectable as a fallback */
    }
  }

  protected async leave(): Promise<void> {
    await this.realtime.leaveSession(this.shortCode, this.myUserId);
    await this.router.navigate(['/']);
  }
}
