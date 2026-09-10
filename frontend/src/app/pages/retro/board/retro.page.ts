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
  RETRO_TEMPLATE_LABEL_KEYS,
  RetroCardInfo,
  RetroColumnInfo,
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

  protected canModify(card: RetroCardInfo): boolean {
    return !this.board()?.isClosed && (card.isMine || this.canFacilitate());
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
