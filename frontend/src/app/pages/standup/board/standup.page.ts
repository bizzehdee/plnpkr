import { Component, OnInit, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { IdentityService } from '../../../core/identity.service';
import { SessionMembershipService } from '../../../core/session-membership.service';
import { I18nService } from '../../../core/i18n.service';
import {
  STANDUP_MAX_ANSWER_LENGTH,
  STANDUP_MAX_BLOCKER_LENGTH,
  StandupActionResult,
  StandupBlockerInfo,
  StandupQuestionInfo,
} from '../../../core/models';
import { StandupExportFormat, StandupExportService } from '../../../core/standup-export.service';
import { SignalrStandupClient } from '../../../core/standup.client';
import { TranslatePipe } from '../../../core/translate.pipe';

/**
 * The Async Standup board (#36): answer the questions in your own time, and read everyone else's
 * once you have.
 *
 * **The page does not enforce post-to-read** — it renders what the server sent. Until you have
 * posted, other people's answers are simply not in the snapshot (#22's rule, applied for the fourth
 * time), so there is nothing here to hide and nothing to leak by getting the template wrong. What
 * the page does is explain the gate, so the empty state reads as a rule rather than as an empty
 * room.
 *
 * Notice what is not here: no phase rail and no countdown. A standup opens, people post, it closes.
 */
@Component({
  selector: 'app-standup',
  imports: [FormsModule, RouterLink, TranslatePipe],
  templateUrl: './standup.page.html',
})
export class StandupPage implements OnInit {
  private readonly standup = inject(SignalrStandupClient);
  private readonly identity = inject(IdentityService);
  private readonly membership = inject(SessionMembershipService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly i18n = inject(I18nService);
  private readonly exports = inject(StandupExportService);

  protected readonly maxAnswerLength = STANDUP_MAX_ANSWER_LENGTH;
  protected readonly maxBlockerLength = STANDUP_MAX_BLOCKER_LENGTH;

  protected shortCode = '';
  protected readonly board = this.standup.board;
  protected readonly closed = this.standup.closed;
  protected readonly error = signal<string | null>(null);
  protected readonly announcement = signal('');
  protected readonly myUserId = this.identity.userId;

  /** The answer being typed, per question id. Local until saved, so typing is not a broadcast. */
  protected drafts: Record<string, string> = {};

  /** Which answer is mid-save, so its button can say so. */
  protected readonly saving = signal<string | null>(null);

  /** The blocker composer. */
  protected readonly blockerComposerOpen = signal(false);
  protected blockerText = '';

  /** Which blocker is being assigned an owner. */
  protected readonly assigning = signal<string | null>(null);
  protected assignOwnerUserId = '';
  protected assignOwnerName = '';

  constructor() {
    // Remember the seat so a reload reconnects instead of bouncing to the join gate.
    effect(() => {
      const board = this.board();
      if (board && board.participants.some((p) => p.userId === this.myUserId)) {
        this.membership.remember(board.shortCode, 'Voter');
      }
    });

    // Seed the drafts from what the server has for me, so a reload shows my answers editable
    // rather than blank — the alternative silently invites overwriting them with nothing.
    effect(() => {
      const mine = this.board()?.people.find((p) => p.isMe);
      if (!mine) {
        return;
      }
      for (const answer of mine.answers) {
        if (this.drafts[answer.questionId] === undefined) {
          this.drafts[answer.questionId] = answer.text;
        }
      }
    });
  }

  async ngOnInit(): Promise<void> {
    this.shortCode = this.route.snapshot.paramMap.get('shortCode') ?? '';

    const current = this.board();
    if (current && current.shortCode === this.shortCode) {
      return; // arrived straight from the create form — already seated
    }

    if (!(await this.tryRejoin())) {
      await this.router.navigate(['/join', this.shortCode]);
    }
  }

  /** Reclaims a remembered seat on a reload; false if this browser has never joined. */
  private async tryRejoin(): Promise<boolean> {
    const role = this.membership.get(this.shortCode);
    if (!role) {
      return false;
    }

    try {
      await this.standup.connect();
      const result = await this.standup.joinBoard(
        this.shortCode, this.myUserId, this.identity.displayName, role);
      return result.status === 'Ok';
    } catch {
      return false;
    }
  }

  // --- Derived state ------------------------------------------------------

  protected readonly questions = computed<StandupQuestionInfo[]>(
    () => this.board()?.questions ?? []);

  /** Everyone but me — my own standup is the form at the top, not a card in the list. */
  protected readonly others = computed(
    () => this.board()?.people.filter((p) => !p.isMe) ?? []);

  protected readonly iHavePosted = computed(() => this.board()?.iHavePosted === true);

  /**
   * "N of the M people in this room have posted." A count, never a list of who is missing (#36):
   * presence only knows who opened the room, so naming the absent would name the wrong people.
   */
  protected readonly postedSummary = computed(() => {
    const board = this.board();
    if (!board) {
      return '';
    }
    return this.i18n.t('standup.postedCount', {
      posted: board.postedCount,
      total: board.participantCount,
    });
  });

  protected readonly canWrite = computed(() => {
    const board = this.board();
    return !!board && !board.isClosed;
  });

  protected readonly openBlockers = computed(
    () => this.board()?.blockers.filter((b) => !b.isResolved) ?? []);

  protected readonly resolvedBlockers = computed(
    () => this.board()?.blockers.filter((b) => b.isResolved) ?? []);

  protected answerFor(userId: string, questionId: string): string | null {
    const person = this.board()?.people.find((p) => p.userId === userId);
    return person?.answers.find((a) => a.questionId === questionId)?.text ?? null;
  }

  protected wasEdited(userId: string, questionId: string): boolean {
    const person = this.board()?.people.find((p) => p.userId === userId);
    return !!person?.answers.find((a) => a.questionId === questionId)?.updatedAt;
  }

  protected formatPosted(iso: string | null): string {
    return iso ? this.i18n.formatDate(iso) : '';
  }

  // --- Answering ----------------------------------------------------------

  protected draftFor(questionId: string): string {
    return this.drafts[questionId] ?? '';
  }

  protected setDraft(questionId: string, text: string): void {
    this.drafts = { ...this.drafts, [questionId]: text };
  }

  protected async saveAnswer(question: StandupQuestionInfo): Promise<void> {
    const text = this.draftFor(question.id).trim();
    this.saving.set(question.id);
    try {
      const result = await this.standup.answer(
        this.shortCode, this.myUserId, question.id, text);
      if (result.status === 'Ok') {
        this.announce(
          this.i18n.t(text ? 'standup.announce.saved' : 'standup.announce.cleared'));
      } else {
        this.error.set(this.statusMessage(result.status));
      }
    } finally {
      this.saving.set(null);
    }
  }

  /** Saves every non-empty draft in one go, which is how most people will actually post. */
  protected async postAll(): Promise<void> {
    for (const question of this.questions()) {
      const text = this.draftFor(question.id).trim();
      if (!text) {
        continue;
      }
      const result = await this.standup.answer(
        this.shortCode, this.myUserId, question.id, text);
      if (result.status !== 'Ok') {
        this.error.set(this.statusMessage(result.status));
        return;
      }
    }
    this.announce(this.i18n.t('standup.announce.posted'));
  }

  protected anyDraftFilled(): boolean {
    return this.questions().some((q) => this.draftFor(q.id).trim().length > 0);
  }

  // --- Blockers -----------------------------------------------------------

  protected openBlockerComposer(): void {
    this.blockerComposerOpen.set(true);
    this.blockerText = '';
  }

  protected closeBlockerComposer(): void {
    this.blockerComposerOpen.set(false);
    this.blockerText = '';
  }

  protected async addBlocker(): Promise<void> {
    const text = this.blockerText.trim();
    if (!text) {
      return;
    }

    const result = await this.standup.addBlocker(this.shortCode, this.myUserId, text);
    if (result.status === 'Ok') {
      this.closeBlockerComposer();
      this.announce(this.i18n.t('standup.announce.blockerAdded'));
    } else {
      this.error.set(this.statusMessage(result.status));
    }
  }

  protected startAssign(blocker: StandupBlockerInfo): void {
    this.assigning.set(blocker.id);
    this.assignOwnerUserId = blocker.ownerUserId ?? '';
    this.assignOwnerName = blocker.ownerUserId ? '' : blocker.ownerName ?? '';
  }

  protected cancelAssign(): void {
    this.assigning.set(null);
  }

  protected async saveAssign(blocker: StandupBlockerInfo): Promise<void> {
    const result = await this.standup.assignBlocker(
      this.shortCode,
      this.myUserId,
      blocker.id,
      this.assignOwnerUserId || null,
      this.assignOwnerName.trim() || null,
    );
    if (result.status === 'Ok') {
      this.cancelAssign();
    } else {
      this.error.set(this.statusMessage(result.status));
    }
  }

  /** Takes it on yourself — the common case, one click instead of the assign form. */
  protected async takeBlocker(blocker: StandupBlockerInfo): Promise<void> {
    this.after(await this.standup.assignBlocker(
      this.shortCode, this.myUserId, blocker.id, this.myUserId, null));
  }

  protected async toggleBlocker(blocker: StandupBlockerInfo): Promise<void> {
    this.after(await this.standup.toggleBlockerResolved(
      this.shortCode, this.myUserId, blocker.id));
  }

  protected async deleteBlocker(blocker: StandupBlockerInfo): Promise<void> {
    this.after(await this.standup.deleteBlocker(this.shortCode, this.myUserId, blocker.id));
  }

  // --- Export -------------------------------------------------------------

  /**
   * "Export it or lose it" (#36). A standup room is idle by construction between mornings, so the
   * idle sweep (#15) will take it — the platform's retention rule applies rather than being carved
   * out for one tool, and the button is what makes saying so honest.
   */
  protected download(format: StandupExportFormat): void {
    const board = this.board();
    if (board) {
      this.exports.download(board, format);
      this.announce(this.i18n.t('standup.announce.exported'));
    }
  }

  // --- Plumbing -----------------------------------------------------------

  protected dismissError(): void {
    this.error.set(null);
  }

  private after(result: StandupActionResult): void {
    if (result.status !== 'Ok') {
      this.error.set(this.statusMessage(result.status));
    }
  }

  private announce(message: string): void {
    this.announcement.set(message);
  }

  private statusMessage(status: string): string {
    switch (status) {
      case 'InvalidAnswer':
        return this.i18n.t('standup.err.invalidAnswer');
      case 'InvalidBlockerText':
        return this.i18n.t('standup.err.invalidBlocker');
      case 'BoardClosed':
        return this.i18n.t('standup.err.closed');
      case 'NotOrganiser':
        return this.i18n.t('standup.err.notOrganiser');
      case 'RateLimited':
        return this.i18n.t('err.create.rateLimited');
      case 'QuestionNotFound':
      case 'BlockerNotFound':
        return this.i18n.t('standup.err.gone');
      default:
        return this.i18n.t('standup.err.generic');
    }
  }
}
