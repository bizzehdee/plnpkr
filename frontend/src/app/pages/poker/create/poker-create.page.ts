import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { SignalrRealtimeClient } from '../../../core/poker.client';
import { IdentityService } from '../../../core/identity.service';
import { DeckStorageService } from '../../../core/deck-storage.service';
import { DeckType, DECK_LABEL_KEYS, SavedDeck } from '../../../core/models';
import { I18nService } from '../../../core/i18n.service';
import { RoomNameField } from '../../../core/room-name-field';
import { RoomNameService } from '../../../core/room-name.service';
import { TranslatePipe } from '../../../core/translate.pipe';

@Component({
  selector: 'app-poker-create',
  imports: [FormsModule, RoomNameField, TranslatePipe],
  templateUrl: './poker-create.page.html',
})
export class PokerCreatePage {
  private readonly realtime = inject(SignalrRealtimeClient);
  private readonly identity = inject(IdentityService);
  private readonly router = inject(Router);
  private readonly deckStorage = inject(DeckStorageService);
  private readonly i18n = inject(I18nService);
  private readonly names = inject(RoomNameService);

  /** What this browser last created a poker session as, and whether it was date-stamped. */
  private readonly remembered = this.names.recall('Poker');

  protected readonly deckOptions = computed(() =>
    (Object.keys(DECK_LABEL_KEYS) as DeckType[]).map((id) => [id, this.i18n.t(DECK_LABEL_KEYS[id])] as [DeckType, string]),
  );

  protected sessionName = this.remembered.name;
  protected appendDate = this.remembered.appendDate;
  protected displayName = this.identity.displayName;
  protected deckType: DeckType = 'Fibonacci';
  protected customCards = '';
  /** Named custom decks remembered in this browser (#11). */
  protected readonly savedDecks = signal<SavedDeck[]>(this.deckStorage.list());
  protected deckName = '';
  protected organise = true;
  protected password = '';
  protected enableReactions = true;
  /** Configured round-timer duration in seconds; 0 = no timer. Changeable later in-session (#14). */
  protected timerDurationSeconds = 0;

  protected readonly timerOptions: { value: number; label: string }[] = [
    { value: 0, label: 'No timer' },
    { value: 30, label: '30 seconds' },
    { value: 60, label: '1 minute' },
    { value: 120, label: '2 minutes' },
    { value: 300, label: '5 minutes' },
  ];

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /** Apply a saved deck: switch to a custom deck pre-filled with its cards (#11). */
  protected applyDeck(deck: SavedDeck): void {
    this.deckType = 'Custom';
    this.customCards = deck.cards;
    this.deckName = deck.name;
  }

  /** Remember the current custom deck under a name for next time (#11). */
  protected saveDeck(): void {
    if (this.deckType !== 'Custom' || !this.customCards.trim() || !this.deckName.trim()) return;
    this.savedDecks.set(this.deckStorage.save({ name: this.deckName.trim(), cards: this.customCards.trim() }));
  }

  protected removeDeck(name: string): void {
    this.savedDecks.set(this.deckStorage.remove(name));
  }

  protected async create(): Promise<void> {
    this.error.set(null);

    if (!this.sessionName.trim()) {
      this.error.set(this.i18n.t('home.errorNameRequired'));
      return;
    }
    if (!this.displayName.trim()) {
      this.error.set(this.i18n.t('home.errorYourNameRequired'));
      return;
    }

    this.busy.set(true);
    try {
      await this.realtime.connect();
      this.identity.displayName = this.displayName.trim();

      const result = await this.realtime.createSession(
        this.names.compose(this.sessionName, this.appendDate),
        this.deckType,
        this.deckType === 'Custom' ? this.customCards : null,
        this.identity.userId,
        this.displayName.trim(),
        this.organise,
        this.password.trim() || null,
        this.enableReactions,
        this.timerDurationSeconds > 0 ? this.timerDurationSeconds : null,
      );

      if (result.status === 'Ok' && result.session) {
        // The name *as typed*: storing the stamped one would compound the date next time.
        this.names.remember('Poker', this.sessionName, this.appendDate);
        await this.router.navigate(['/poker', result.session.shortCode]);
      } else if (result.status === 'RateLimited') {
        this.error.set(this.i18n.t('err.rateLimited'));
      } else {
        this.error.set(result.error ?? this.i18n.t('home.errorGeneric'));
      }
    } catch {
      this.error.set(this.i18n.t('common.errorUnreachable'));
    } finally {
      this.busy.set(false);
    }
  }
}
