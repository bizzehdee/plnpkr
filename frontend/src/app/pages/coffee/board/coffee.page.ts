import { Component, OnDestroy, OnInit, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { SignalrCoffeeClient } from '../../../core/coffee.client';
import { IdentityService } from '../../../core/identity.service';
import { SessionMembershipService } from '../../../core/session-membership.service';
import { I18nService } from '../../../core/i18n.service';
import { TranslatePipe } from '../../../core/translate.pipe';
import {
  COFFEE_MAX_TOPIC_LENGTH,
  COFFEE_PHASE_LABEL_KEYS,
  COFFEE_PHASE_ORDER,
  CoffeeActionResult,
  CoffeeDecisionInfo,
  CoffeePhase,
  CoffeeTopicInfo,
  ExtendChoice,
} from '../../../core/models';

/**
 * The Lean Coffee board (#35): propose topics privately, rank them with dots, then work the ranked
 * list one topic at a time under a timebox.
 *
 * Keyboard-reachable throughout, per the a11y baseline (#4): the dot controls carry per-item
 * labels, the phase rail is an ordered list with `aria-current="step"`, and there is no
 * pointer-only interaction to have an equivalent for — the ranking is server-side, so unlike the
 * retro's grouping there is nothing to drag.
 */
@Component({
  selector: 'app-coffee',
  imports: [FormsModule, RouterLink, TranslatePipe],
  templateUrl: './coffee.page.html',
})
export class CoffeePage implements OnInit, OnDestroy {
  private readonly coffee = inject(SignalrCoffeeClient);
  private readonly identity = inject(IdentityService);
  private readonly membership = inject(SessionMembershipService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly i18n = inject(I18nService);

  protected readonly maxTopicLength = COFFEE_MAX_TOPIC_LENGTH;
  protected readonly phases = COFFEE_PHASE_ORDER;

  protected shortCode = '';
  protected readonly board = this.coffee.board;
  protected readonly closed = this.coffee.closed;
  protected readonly error = signal<string | null>(null);
  protected readonly announcement = signal('');
  protected readonly myUserId = this.identity.userId;

  /** The topic composer. */
  protected readonly composing = signal(false);
  protected draft = '';

  /** Which topic is being reworded. */
  protected readonly editing = signal<string | null>(null);
  protected editDraft = '';

  /** The decision composer. */
  protected readonly decisionComposerOpen = signal(false);
  protected decisionTitle = '';
  protected decisionOwnerUserId = '';
  protected decisionOwnerName = '';
  protected decisionDue = '';

  // --- Lifecycle ----------------------------------------------------------

  constructor() {
    // Remember the seat so a reload reconnects instead of bouncing to the join gate.
    effect(() => {
      const board = this.board();
      if (board && board.participants.some((p) => p.userId === this.myUserId)) {
        this.membership.remember(board.shortCode, 'Voter');
      }
    });
  }

  async ngOnInit(): Promise<void> {
    this.shortCode = this.route.snapshot.paramMap.get('shortCode') ?? '';
    this.tick = setInterval(() => this.now.set(Date.now()), 1000);

    const current = this.board();
    if (current && current.shortCode === this.shortCode) {
      return; // arrived straight from the create form — already seated
    }

    if (!(await this.tryRejoin())) {
      await this.router.navigate(['/join', this.shortCode]);
    }
  }

  ngOnDestroy(): void {
    if (this.tick) {
      clearInterval(this.tick);
    }
  }

  /** Reclaims a remembered seat on a reload; false if this browser has never joined. */
  private async tryRejoin(): Promise<boolean> {
    const role = this.membership.get(this.shortCode);
    if (!role) {
      return false;
    }

    try {
      await this.coffee.connect();
      const result = await this.coffee.joinBoard(
        this.shortCode, this.myUserId, this.identity.displayName, role);
      return result.status === 'Ok';
    } catch {
      return false;
    }
  }

  // --- The countdown ------------------------------------------------------

  private readonly now = signal(Date.now());
  private tick?: ReturnType<typeof setInterval>;

  /** Seconds left on the current topic's timebox, or null when nothing is running. */
  protected readonly secondsLeft = computed(() => {
    const deadline = this.board()?.phaseDeadline;
    if (!deadline) {
      return null;
    }
    const left = Math.ceil((new Date(deadline).getTime() - this.now()) / 1000);
    return Math.max(0, left);
  });

  /** mm:ss, because a bare second count is hard to read past a minute. */
  protected readonly timeLeft = computed(() => {
    const left = this.secondsLeft();
    if (left === null) {
      return null;
    }
    return `${Math.floor(left / 60)}:${String(left % 60).padStart(2, '0')}`;
  });

  // --- Derived state ------------------------------------------------------

  protected readonly isProposing = computed(() => this.board()?.phase === 'Propose');
  protected readonly isVoting = computed(() => this.board()?.phase === 'Vote');
  protected readonly isDiscussing = computed(() => this.board()?.phase === 'Discuss');

  protected readonly canFacilitate = computed(() => {
    const board = this.board();
    if (!board) {
      return false;
    }
    const me = board.participants.find((p) => p.userId === this.myUserId);
    // No connected organiser means anyone may drive, matching the succession rule (#7).
    return me?.isOrganiser === true || !board.participants.some((p) => p.isOrganiser && p.isConnected);
  });

  protected readonly currentTopic = computed<CoffeeTopicInfo | null>(() => {
    const board = this.board();
    if (!board?.currentTopicId) {
      return null;
    }
    return board.agenda.find((t) => t.id === board.currentTopicId)
      ?? board.topics.find((t) => t.id === board.currentTopicId)
      ?? null;
  });

  protected readonly hasDotsLeft = computed(() => (this.board()?.myDotsRemaining ?? 0) > 0);

  /** The list to render: the ranked agenda once it exists, otherwise as-proposed. */
  protected readonly visibleTopics = computed(() => {
    const board = this.board();
    if (!board) {
      return [];
    }
    return board.agenda.length > 0 ? board.agenda : board.topics;
  });

  protected phaseLabelFor(phase: CoffeePhase): string {
    return this.i18n.t(COFFEE_PHASE_LABEL_KEYS[phase]);
  }

  protected phaseIndex(phase: CoffeePhase): number {
    return this.phases.indexOf(phase);
  }

  protected phaseDone(phase: CoffeePhase): boolean {
    const current = this.board()?.phase;
    return current ? this.phaseIndex(phase) < this.phaseIndex(current) : false;
  }

  protected formatDue(iso: string | null): string {
    return iso ? this.i18n.formatDate(iso) : '';
  }

  /** m:ss for a duration, so "how long did we spend" reads naturally in the log. */
  protected formatSpent(seconds: number): string {
    return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
  }

  // --- Phases -------------------------------------------------------------

  protected async advancePhase(): Promise<void> {
    this.after(await this.coffee.advancePhase(this.shortCode, this.myUserId));
  }

  protected async previousPhase(): Promise<void> {
    this.after(await this.coffee.previousPhase(this.shortCode, this.myUserId));
  }

  // --- Topics -------------------------------------------------------------

  protected openComposer(): void {
    this.composing.set(true);
    this.draft = '';
  }

  protected cancelComposer(): void {
    this.composing.set(false);
    this.draft = '';
  }

  protected async addTopic(): Promise<void> {
    const text = this.draft.trim();
    if (!text) {
      return;
    }

    const result = await this.coffee.addTopic(this.shortCode, this.myUserId, text);
    if (result.status === 'Ok') {
      this.draft = '';
      this.announce(this.i18n.t('coffee.announce.topicAdded'));
    } else {
      this.error.set(this.statusMessage(result.status));
    }
  }

  protected startEdit(topic: CoffeeTopicInfo): void {
    this.editing.set(topic.id);
    this.editDraft = topic.text;
  }

  protected cancelEdit(): void {
    this.editing.set(null);
    this.editDraft = '';
  }

  protected async saveEdit(topic: CoffeeTopicInfo): Promise<void> {
    const text = this.editDraft.trim();
    if (!text) {
      return;
    }

    const result = await this.coffee.editTopic(this.shortCode, this.myUserId, topic.id, text);
    if (result.status === 'Ok') {
      this.cancelEdit();
    } else {
      this.error.set(this.statusMessage(result.status));
    }
  }

  protected async deleteTopic(topic: CoffeeTopicInfo): Promise<void> {
    this.after(await this.coffee.deleteTopic(this.shortCode, this.myUserId, topic.id));
  }

  protected canModify(topic: CoffeeTopicInfo): boolean {
    return (topic.isMine || this.canFacilitate()) && this.isProposing() && !this.board()?.isClosed;
  }

  // --- Dot voting ---------------------------------------------------------

  protected canAddDot(topic: CoffeeTopicInfo): boolean {
    const board = this.board();
    if (!board || !this.isVoting() || board.isClosed) {
      return false;
    }
    return this.hasDotsLeft() && (board.allowMultiplePerItem || topic.myDots === 0);
  }

  protected canRemoveDot(topic: CoffeeTopicInfo): boolean {
    return this.isVoting() && !this.board()?.isClosed && topic.myDots > 0;
  }

  protected async addDot(topic: CoffeeTopicInfo): Promise<void> {
    this.after(await this.coffee.castVote(this.shortCode, this.myUserId, topic.id));
  }

  protected async removeDot(topic: CoffeeTopicInfo): Promise<void> {
    this.after(await this.coffee.withdrawVote(this.shortCode, this.myUserId, topic.id));
  }

  protected dotLabel(topic: CoffeeTopicInfo, add: boolean): string {
    return this.i18n.t(add ? 'coffee.addDotTo' : 'coffee.removeDotFrom', { topic: topic.text });
  }

  // --- The discussion -----------------------------------------------------

  protected async nextTopic(): Promise<void> {
    this.after(await this.coffee.nextTopic(this.shortCode, this.myUserId));
  }

  protected async voteExtend(choice: ExtendChoice): Promise<void> {
    this.after(await this.coffee.voteOnExtension(this.shortCode, this.myUserId, choice));
  }

  protected async resolveExtension(): Promise<void> {
    this.after(await this.coffee.resolveExtension(this.shortCode, this.myUserId));
  }

  // --- Decisions ----------------------------------------------------------

  /** Whether decisions may be recorded: from Discuss on, and on a closed room (#26's carve-out). */
  protected readonly canWriteDecisions = computed(() => {
    const board = this.board();
    if (!board) {
      return false;
    }
    return board.isClosed || board.phase === 'Discuss' || board.phase === 'Done';
  });

  /** Ticking or removing an existing one is not phase-gated, matching the server. */
  protected readonly canUpdateDecisions = computed(() => !!this.board());

  protected openDecisionComposer(): void {
    this.decisionComposerOpen.set(true);
    this.decisionTitle = '';
    this.decisionOwnerUserId = '';
    this.decisionOwnerName = '';
    this.decisionDue = '';
  }

  protected closeDecisionComposer(): void {
    this.decisionComposerOpen.set(false);
  }

  protected async saveDecision(): Promise<void> {
    const title = this.decisionTitle.trim();
    if (!title) {
      return;
    }

    const result = await this.coffee.addDecision(
      this.shortCode,
      this.myUserId,
      title,
      this.board()?.currentTopicId ?? null,
      this.decisionOwnerUserId || null,
      this.decisionOwnerName.trim() || null,
      this.decisionDue ? new Date(this.decisionDue).toISOString() : null,
    );

    if (result.status === 'Ok') {
      this.closeDecisionComposer();
      this.announce(this.i18n.t('coffee.announce.decisionAdded'));
    } else {
      this.error.set(this.statusMessage(result.status));
    }
  }

  protected async toggleDecisionDone(decision: CoffeeDecisionInfo): Promise<void> {
    this.after(await this.coffee.toggleDecisionDone(this.shortCode, this.myUserId, decision.id));
  }

  protected async deleteDecision(decision: CoffeeDecisionInfo): Promise<void> {
    this.after(await this.coffee.deleteDecision(this.shortCode, this.myUserId, decision.id));
  }

  // --- Plumbing -----------------------------------------------------------

  protected dismissError(): void {
    this.error.set(null);
  }

  private after(result: CoffeeActionResult): void {
    if (result.status !== 'Ok') {
      this.error.set(this.statusMessage(result.status));
    }
  }

  private announce(message: string): void {
    this.announcement.set(message);
  }

  private statusMessage(status: string): string {
    switch (status) {
      case 'InvalidTopicText':
        return this.i18n.t('coffee.err.invalidTopic');
      case 'NotTopicAuthor':
        return this.i18n.t('coffee.err.notTopicAuthor');
      case 'BoardClosed':
        return this.i18n.t('coffee.err.closed');
      case 'WrongPhase':
        return this.i18n.t('coffee.err.wrongPhase');
      case 'IllegalPhaseTransition':
        return this.i18n.t('coffee.err.illegalPhase');
      case 'OutOfDots':
        return this.i18n.t('coffee.err.outOfDots');
      case 'AlreadyVotedForItem':
        return this.i18n.t('coffee.err.alreadyVoted');
      case 'NoVoteToWithdraw':
        return this.i18n.t('coffee.err.noDotToTakeBack');
      case 'NoExtendVoteRunning':
        return this.i18n.t('coffee.err.noExtendVote');
      case 'InvalidDecisionTitle':
        return this.i18n.t('coffee.err.invalidDecision');
      case 'NotOrganiser':
        return this.i18n.t('coffee.err.notFacilitator');
      case 'RateLimited':
        return this.i18n.t('err.create.rateLimited');
      case 'TopicNotFound':
      case 'DecisionNotFound':
        return this.i18n.t('coffee.err.gone');
      default:
        return this.i18n.t('coffee.err.generic');
    }
  }
}
