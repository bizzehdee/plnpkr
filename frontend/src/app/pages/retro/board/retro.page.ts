import { Component, OnDestroy, OnInit, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { SignalrRetroClient } from '../../../core/retro.client';
import { IdentityService } from '../../../core/identity.service';
import { SessionMembershipService } from '../../../core/session-membership.service';
import { I18nService } from '../../../core/i18n.service';
import { TranslatePipe } from '../../../core/translate.pipe';
import {
  RETRO_MAX_CARD_LENGTH,
  RETRO_PHASE_LABEL_KEYS,
  RETRO_PHASE_ORDER,
  RETRO_TEMPLATE_LABEL_KEYS,
  RetroActionResult,
  RetroCardInfo,
  RetroColumnInfo,
  RetroActionInfo,
  RetroGroupInfo,
  RetroPhase,
  RetroVoteTarget,
} from '../../../core/models';

/**
 * The retro board (#21): columns of cards the team writes, edits, moves and deletes in real time.
 *
 * Drag-and-drop lands in #24 alongside grouping; every card here already carries a keyboard-
 * reachable "move to column" control, because a mouse-only affordance would put the board out of
 * reach for keyboard and screen-reader users (#4).
 */
@Component({
  selector: 'app-retro',
  imports: [FormsModule, TranslatePipe],
  templateUrl: './retro.page.html',
})
export class RetroPage implements OnInit, OnDestroy {
  private readonly retro = inject(SignalrRetroClient);
  private readonly identity = inject(IdentityService);
  private readonly membership = inject(SessionMembershipService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly i18n = inject(I18nService);

  protected readonly maxCardLength = RETRO_MAX_CARD_LENGTH;

  protected shortCode = '';
  protected readonly board = this.retro.board;
  protected readonly closed = this.retro.closed;
  protected readonly error = signal<string | null>(null);

  /** Which column's composer is open, and its draft text. Only one is open at a time. */
  protected readonly composingIn = signal<string | null>(null);
  protected draft = '';

  /** Which card is being edited, and its draft text. */
  protected readonly editingCard = signal<string | null>(null);
  protected editDraft = '';

  /** Announced through the live region after a change, for screen readers (#4). */
  protected readonly announcement = signal('');

  protected readonly myUserId = this.identity.userId;

  protected readonly templateLabel = computed(() => {
    const board = this.board();
    return board ? this.i18n.t(RETRO_TEMPLATE_LABEL_KEYS[board.template]) : '';
  });

  protected readonly cardCount = computed(() =>
    (this.board()?.columns ?? []).reduce((total, c) => total + c.cards.length, 0),
  );

  // --- Phases (#23) ------------------------------------------------------

  protected readonly phases = RETRO_PHASE_ORDER;

  protected readonly phaseLabel = computed(() => {
    const board = this.board();
    return board ? this.i18n.t(RETRO_PHASE_LABEL_KEYS[board.phase]) : '';
  });

  /** Cards other people have written but this viewer may not see yet, during Collect. */
  protected readonly hiddenCardCount = computed(() =>
    (this.board()?.columns ?? []).reduce((total, c) => total + c.hiddenCardCount, 0),
  );

  protected readonly isCollecting = computed(() => this.board()?.phase === 'Collect');

  /** Ticks once a second so the countdown re-renders; the deadline itself is the server's. */
  private readonly now = signal(Date.now());
  private tick?: ReturnType<typeof setInterval>;

  /** Whole seconds left on the phase countdown, or null when none is running. */
  protected readonly secondsLeft = computed(() => {
    const deadline = this.board()?.phaseDeadline;
    if (!deadline) {
      return null;
    }
    return Math.max(0, Math.ceil((new Date(deadline).getTime() - this.now()) / 1000));
  });

  protected phaseLabelFor(phase: RetroPhase): string {
    return this.i18n.t(RETRO_PHASE_LABEL_KEYS[phase]);
  }

  protected phaseIndex(phase: RetroPhase): number {
    return RETRO_PHASE_ORDER.indexOf(phase);
  }

  /** True for phases the retro has already been through, so the rail can show progress. */
  protected phaseDone(phase: RetroPhase): boolean {
    const board = this.board();
    return !!board && this.phaseIndex(phase) < this.phaseIndex(board.phase);
  }

  protected async advancePhase(): Promise<void> {
    const result = await this.retro.advancePhase(this.shortCode, this.myUserId, null);
    this.afterPhaseChange(result);
  }

  protected async previousPhase(): Promise<void> {
    const result = await this.retro.previousPhase(this.shortCode, this.myUserId);
    this.afterPhaseChange(result);
  }

  private afterPhaseChange(result: RetroActionResult): void {
    if (result.status === 'Ok' && result.board) {
      // Announce it: a phase change silently rearranges what everyone can do (#4).
      this.announce(
        this.i18n
          .t('retro.announce.phaseChanged')
          .replace('{phase}', this.i18n.t(RETRO_PHASE_LABEL_KEYS[result.board.phase])),
      );
      this.cancelComposer();
      this.cancelEdit();
    } else {
      this.error.set(this.statusMessage(result.status));
    }
  }

  /** An organiser (or anyone, on a board with no organiser) may moderate and change settings. */
  protected readonly canFacilitate = computed(() => {
    const board = this.board();
    if (!board) {
      return false;
    }
    const organisers = board.participants.filter((p) => p.isOrganiser);
    if (organisers.length === 0 && !board.organiserUserId) {
      return true;
    }
    return board.participants.some((p) => p.userId === this.myUserId && p.isOrganiser);
  });

  constructor() {
    // One shared ticker drives the countdown display; the deadline is server-authoritative (#23).
    this.tick = setInterval(() => this.now.set(Date.now()), 1000);

    // Remember (shortCode -> role) so a later full page reload can silently rejoin instead of
    // bouncing back to the join screen (F5 shouldn't kick you out). Same rule as the poker table.
    effect(() => {
      const me = this.board()?.participants.find((p) => p.userId === this.myUserId);
      if (me && this.shortCode) {
        this.membership.remember(this.shortCode, me.role);
      }
    });
  }

  async ngOnInit(): Promise<void> {
    this.shortCode = this.route.snapshot.paramMap.get('shortCode') ?? '';

    // Arriving straight from the create form, the board is already in hand and joined — don't
    // bounce a facilitator who just made this retro back through the join gate.
    const current = this.board();
    if (current && current.shortCode === this.shortCode) {
      return;
    }

    const rejoined = await this.tryRejoin();
    if (!rejoined) {
      // Nothing to reconnect to — the join gate is also where the password prompt lives.
      await this.router.navigate(['/join', this.shortCode]);
    }
  }

  /** Attempts a silent rejoin using the remembered identity + role. See ngOnInit. */
  private async tryRejoin(): Promise<boolean> {
    const role = this.membership.get(this.shortCode);
    const displayName = this.identity.displayName;
    if (!role || !displayName) {
      return false;
    }

    try {
      await this.retro.connect();
      const result = await this.retro.joinBoard(
        this.shortCode,
        this.identity.userId,
        displayName,
        role,
      );
      // The join contract is shared with poker, so a missing room is SessionNotFound here.
      if (result.status === 'SessionNotFound') {
        this.membership.forget(this.shortCode); // gone for good — stop trying to rejoin it
      }
      return result.status === 'Ok';
    } catch {
      this.error.set(this.i18n.t('retro.errorConnect'));
      return false;
    }
  }

  async ngOnDestroy(): Promise<void> {
    if (this.tick !== undefined) {
      clearInterval(this.tick);
    }
    // Leave the group but keep the seat: a page change is not the same as leaving the retro.
    await this.retro.disconnect().catch(() => {});
  }

  // --- Composing ---------------------------------------------------------

  protected openComposer(columnId: string): void {
    this.composingIn.set(columnId);
    this.draft = '';
  }

  protected cancelComposer(): void {
    this.composingIn.set(null);
    this.draft = '';
  }

  protected async addCard(column: RetroColumnInfo): Promise<void> {
    const text = this.draft.trim();
    if (!text) {
      return;
    }

    const result = await this.retro.addCard(this.shortCode, this.myUserId, column.id, text);
    if (result.status === 'Ok') {
      this.draft = '';
      this.announce(this.i18n.t('retro.announce.cardAdded').replace('{column}', column.title));
      // The composer stays open: adding one card usually means adding several.
    } else {
      this.error.set(this.statusMessage(result.status));
    }
  }

  // --- Editing -----------------------------------------------------------

  protected startEdit(card: RetroCardInfo): void {
    this.editingCard.set(card.id);
    this.editDraft = card.text;
  }

  protected cancelEdit(): void {
    this.editingCard.set(null);
    this.editDraft = '';
  }

  protected async saveEdit(card: RetroCardInfo): Promise<void> {
    const text = this.editDraft.trim();
    if (!text) {
      return;
    }

    const result = await this.retro.editCard(this.shortCode, this.myUserId, card.id, text);
    if (result.status === 'Ok') {
      this.cancelEdit();
      this.announce(this.i18n.t('retro.announce.cardEdited'));
    } else {
      this.error.set(this.statusMessage(result.status));
    }
  }

  protected async deleteCard(card: RetroCardInfo): Promise<void> {
    const result = await this.retro.deleteCard(this.shortCode, this.myUserId, card.id);
    if (result.status === 'Ok') {
      this.announce(this.i18n.t('retro.announce.cardDeleted'));
    } else {
      this.error.set(this.statusMessage(result.status));
    }
  }

  /**
   * Moves a card to another column. This is the keyboard path — a menu on every card — and it is
   * the primary one until #24 adds dragging on top of it.
   */
  protected async moveCard(card: RetroCardInfo, targetColumnId: string): Promise<void> {
    const target = this.board()?.columns.find((c) => c.id === targetColumnId);
    const result = await this.retro.moveCard(
      this.shortCode,
      this.myUserId,
      card.id,
      targetColumnId,
      target?.cards.length ?? 0,
    );
    if (result.status === 'Ok') {
      this.announce(
        this.i18n.t('retro.announce.cardMoved').replace('{column}', target?.title ?? ''),
      );
    } else {
      this.error.set(this.statusMessage(result.status));
    }
  }

  /** Columns a card can move to — every column but the one it is in. */
  protected otherColumns(card: RetroCardInfo): RetroColumnInfo[] {
    const columns = this.board()?.columns ?? [];
    return columns.filter((c) => !c.cards.some((existing) => existing.id === card.id));
  }

  /** May this viewer move or delete this card? Allowed in any phase — that is how a facilitator
   * tidies the board during Group. */
  protected canModify(card: RetroCardInfo): boolean {
    return !this.board()?.isClosed && (card.isMine || this.canFacilitate());
  }

  /**
   * May this viewer reword this card? Only during Collect: once grouping and voting are built on
   * the words, changing them moves the ground under the team (#23). The server enforces it; the UI
   * hides the control rather than offering one that fails.
   */
  protected canEditText(card: RetroCardInfo): boolean {
    return this.canModify(card) && this.isCollecting();
  }

  // --- Action items (#26) ------------------------------------------------

  /**
   * Whether actions may be written. During Discuss and Actions, and — deliberately — on a **closed**
   * board too: "mark done" happens days after the retro ended, and that is the one write a closed
   * room still accepts.
   */
  protected readonly canWriteActions = computed(() => {
    const board = this.board();
    if (!board) {
      return false;
    }
    return board.isClosed || board.phase === 'Discuss' || board.phase === 'Actions';
  });

  protected readonly actionComposerOpen = signal(false);
  protected actionTitle = '';
  protected actionOwnerUserId = '';
  protected actionOwnerName = '';
  protected actionDue = '';

  /** Which action is being edited. */
  protected readonly editingAction = signal<string | null>(null);

  /** Formats a due date in the viewer's locale — dates are locale-shaped (#5). */
  protected formatDue(iso: string | null): string {
    return iso ? this.i18n.formatDate(iso) : '';
  }

  protected openActionComposer(fromGroup?: RetroGroupInfo): void {
    this.actionComposerOpen.set(true);
    this.editingAction.set(null);
    // Prefilled from the theme so the commitment starts attached to what prompted it.
    this.actionTitle = fromGroup?.label ?? '';
    this.actionOwnerUserId = '';
    this.actionOwnerName = '';
    this.actionDue = '';
    this.actionSourceGroupId = fromGroup?.id ?? null;
  }

  protected closeActionComposer(): void {
    this.actionComposerOpen.set(false);
    this.editingAction.set(null);
    this.actionTitle = '';
    this.actionOwnerUserId = '';
    this.actionOwnerName = '';
    this.actionDue = '';
    this.actionSourceGroupId = null;
  }

  private actionSourceGroupId: string | null = null;

  protected startEditAction(action: RetroActionInfo): void {
    this.actionComposerOpen.set(true);
    this.editingAction.set(action.id);
    this.actionTitle = action.title;
    this.actionOwnerUserId = action.ownerUserId ?? '';
    this.actionOwnerName = action.ownerUserId ? '' : (action.ownerName ?? '');
    // <input type="date"> wants yyyy-MM-dd, not an instant.
    this.actionDue = action.dueDate ? action.dueDate.slice(0, 10) : '';
    this.actionSourceGroupId = action.sourceGroupId;
  }

  protected async saveAction(): Promise<void> {
    const title = this.actionTitle.trim();
    if (!title) {
      return;
    }

    const ownerUserId = this.actionOwnerUserId || null;
    const ownerName = ownerUserId ? null : this.actionOwnerName.trim() || null;
    const due = this.actionDue ? new Date(`${this.actionDue}T00:00:00Z`).toISOString() : null;

    const editing = this.editingAction();
    const result = editing
      ? await this.retro.editAction(
          this.shortCode, this.myUserId, editing, title, ownerUserId, ownerName, due)
      : await this.retro.addAction(
          this.shortCode, this.myUserId, title, ownerUserId, ownerName, due,
          this.actionSourceGroupId);

    if (result.status === 'Ok') {
      this.announce(this.i18n.t(editing ? 'retro.announce.actionSaved' : 'retro.announce.actionAdded'));
      this.closeActionComposer();
    } else {
      this.error.set(this.statusMessage(result.status));
    }
  }

  protected async toggleActionDone(action: RetroActionInfo): Promise<void> {
    const result = await this.retro.toggleActionDone(this.shortCode, this.myUserId, action.id);
    if (result.status === 'Ok') {
      this.announce(
        this.i18n.t(action.isDone ? 'retro.announce.actionReopened' : 'retro.announce.actionDone'),
      );
    } else {
      this.error.set(this.statusMessage(result.status));
    }
  }

  protected async deleteAction(action: RetroActionInfo): Promise<void> {
    const result = await this.retro.deleteAction(this.shortCode, this.myUserId, action.id);
    if (result.status === 'Ok') {
      this.announce(this.i18n.t('retro.announce.actionDeleted'));
    } else {
      this.error.set(this.statusMessage(result.status));
    }
  }

  // --- Dot voting (#25) --------------------------------------------------

  protected readonly isVoting = computed(() => this.board()?.phase === 'Vote');

  /** Whether this viewer still has dots to spend. */
  protected readonly hasDotsLeft = computed(() => (this.board()?.myDotsRemaining ?? 0) > 0);

  /**
   * Whether a dot may be added to this item right now: voting is open, the viewer has dots left,
   * and either the board allows stacking or they have not already voted for it. Mirrors the
   * server's rule so the button disables instead of failing on click.
   */
  protected canAddDot(item: { myDots: number }): boolean {
    const board = this.board();
    if (!board || board.isClosed || board.phase !== 'Vote' || board.myDotsRemaining <= 0) {
      return false;
    }
    return board.allowMultiplePerItem || item.myDots === 0;
  }

  protected canRemoveDot(item: { myDots: number }): boolean {
    return this.isVoting() && !this.board()?.isClosed && item.myDots > 0;
  }

  protected async addDot(kind: RetroVoteTarget, item: { id: string; myDots: number }): Promise<void> {
    const result = await this.retro.castVote(this.shortCode, this.myUserId, kind, item.id);
    if (result.status === 'Ok') {
      this.announce(
        this.i18n
          .t('retro.announce.dotSpent')
          .replace('{left}', String(result.board!.myDotsRemaining)),
      );
    } else {
      this.error.set(this.statusMessage(result.status));
    }
  }

  protected async removeDot(kind: RetroVoteTarget, item: { id: string }): Promise<void> {
    const result = await this.retro.withdrawVote(this.shortCode, this.myUserId, kind, item.id);
    if (result.status === 'Ok') {
      this.announce(
        this.i18n
          .t('retro.announce.dotTakenBack')
          .replace('{left}', String(result.board!.myDotsRemaining)),
      );
    } else {
      this.error.set(this.statusMessage(result.status));
    }
  }

  // --- Grouping (#24) ----------------------------------------------------

  protected readonly isGrouping = computed(() => this.board()?.phase === 'Group');

  /** Whether this viewer may group cards: a facilitator, or anyone if the board allows it. */
  protected readonly canGroup = computed(() => {
    const board = this.board();
    if (!board || board.isClosed || board.phase !== 'Group') {
      return false;
    }
    return board.allowParticipantGrouping || this.canFacilitate();
  });

  /** Which theme is being renamed, and its draft label. */
  protected readonly renamingGroup = signal<string | null>(null);
  protected renameDraft = '';

  /** The card currently being dragged, so a drop target knows what it is receiving. */
  private readonly dragging = signal<string | null>(null);

  protected startDrag(card: RetroCardInfo, event: DragEvent): void {
    this.dragging.set(card.id);
    event.dataTransfer?.setData('text/plain', card.id);
  }

  protected endDrag(): void {
    this.dragging.set(null);
  }

  protected allowDrop(event: DragEvent): void {
    if (this.canGroup() && this.dragging()) {
      event.preventDefault(); // marks this element as a valid drop target
    }
  }

  /** Dropping a card onto a theme adds it; dropping onto another card forms a theme from the pair. */
  protected async dropOnGroup(groupId: string, event: DragEvent): Promise<void> {
    event.preventDefault();
    const cardId = this.dragging() ?? event.dataTransfer?.getData('text/plain');
    this.endDrag();
    if (cardId) {
      await this.groupCards([cardId], groupId);
    }
  }

  protected async dropOnCard(target: RetroCardInfo, event: DragEvent): Promise<void> {
    event.preventDefault();
    event.stopPropagation();
    const cardId = this.dragging() ?? event.dataTransfer?.getData('text/plain');
    this.endDrag();
    if (!cardId || cardId === target.id) {
      return;
    }

    // Dropping onto a card that is already in a theme joins that theme; otherwise the pair
    // becomes a new one.
    await this.groupCards(target.groupId ? [cardId] : [cardId, target.id], target.groupId);
  }

  /**
   * The keyboard path (#4): "group with…" on every card. Present from the start, because dragging
   * is unavailable to keyboard and screen-reader users — the drag handlers above are the addition,
   * not the other way round.
   */
  protected async groupWith(card: RetroCardInfo, value: string): Promise<void> {
    if (!value) {
      return;
    }
    if (value === 'new') {
      await this.groupCards([card.id], null);
      return;
    }
    await this.groupCards([card.id], value);
  }

  private async groupCards(cardIds: string[], targetGroupId: string | null): Promise<void> {
    const result = await this.retro.groupCards(
      this.shortCode,
      this.myUserId,
      cardIds,
      targetGroupId,
    );
    if (result.status === 'Ok') {
      this.announce(this.i18n.t('retro.announce.grouped'));
    } else {
      this.error.set(this.statusMessage(result.status));
    }
  }

  protected async ungroup(card: RetroCardInfo): Promise<void> {
    const result = await this.retro.ungroupCard(this.shortCode, this.myUserId, card.id);
    if (result.status === 'Ok') {
      this.announce(this.i18n.t('retro.announce.ungrouped'));
    } else {
      this.error.set(this.statusMessage(result.status));
    }
  }

  protected startRename(group: RetroGroupInfo): void {
    this.renamingGroup.set(group.id);
    this.renameDraft = group.label;
  }

  protected cancelRename(): void {
    this.renamingGroup.set(null);
    this.renameDraft = '';
  }

  protected async saveRename(group: RetroGroupInfo): Promise<void> {
    const label = this.renameDraft.trim();
    if (!label) {
      return;
    }

    const result = await this.retro.renameGroup(this.shortCode, this.myUserId, group.id, label);
    if (result.status === 'Ok') {
      this.cancelRename();
      this.announce(this.i18n.t('retro.announce.themeRenamed'));
    } else {
      this.error.set(this.statusMessage(result.status));
    }
  }

  protected async toggleParticipantGrouping(): Promise<void> {
    const board = this.board();
    if (!board) {
      return;
    }

    const result = await this.retro.setAllowParticipantGrouping(
      this.shortCode,
      this.myUserId,
      !board.allowParticipantGrouping,
    );
    if (result.status !== 'Ok') {
      this.error.set(this.statusMessage(result.status));
    }
  }

  /**
   * Facilitator-only: switch the board between attributed and anonymous cards (#22). The control is
   * only offered while `canChangeAnonymity` holds — once a card exists the server refuses, because
   * flipping it would retroactively expose or hide what people wrote.
   */
  protected async toggleAnonymous(): Promise<void> {
    const board = this.board();
    if (!board) {
      return;
    }

    const result = await this.retro.setAnonymous(this.shortCode, this.myUserId, !board.anonymous);
    if (result.status === 'Ok') {
      this.announce(
        this.i18n.t(result.board!.anonymous ? 'retro.announce.nowAnonymous' : 'retro.announce.nowAttributed'),
      );
    } else {
      this.error.set(this.statusMessage(result.status));
    }
  }

  protected dismissError(): void {
    this.error.set(null);
  }

  private announce(message: string): void {
    this.announcement.set(message);
  }

  private statusMessage(status: string): string {
    switch (status) {
      case 'InvalidCardText':
        return this.i18n.t('retro.err.invalidCardText');
      case 'NotCardAuthor':
        return this.i18n.t('retro.err.notCardAuthor');
      case 'BoardClosed':
        return this.i18n.t('retro.err.boardClosed');
      case 'AnonymityLocked':
        return this.i18n.t('retro.err.anonymityLocked');
      case 'WrongPhase':
        return this.i18n.t('retro.err.wrongPhase');
      case 'IllegalPhaseTransition':
        return this.i18n.t('retro.err.illegalPhase');
      case 'GroupNotFound':
        return this.i18n.t('retro.err.gone');
      case 'InvalidGroupLabel':
        return this.i18n.t('retro.err.invalidGroupLabel');
      case 'OutOfDots':
        return this.i18n.t('retro.err.outOfDots');
      case 'AlreadyVotedForItem':
        return this.i18n.t('retro.err.alreadyVoted');
      case 'NoVoteToWithdraw':
        return this.i18n.t('retro.err.noDotToTakeBack');
      case 'ActionNotFound':
        return this.i18n.t('retro.err.gone');
      case 'InvalidActionTitle':
        return this.i18n.t('retro.err.invalidActionTitle');
      case 'RateLimited':
        return this.i18n.t('err.create.rateLimited');
      case 'CardNotFound':
      case 'ColumnNotFound':
        return this.i18n.t('retro.err.gone');
      default:
        return this.i18n.t('retro.err.generic');
    }
  }
}
