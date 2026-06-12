import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { SignalrRealtimeClient } from '../../core/realtime.client';
import { IdentityService } from '../../core/identity.service';
import { DeckStorageService } from '../../core/deck-storage.service';
import { DeckType, DECK_LABELS, SavedDeck } from '../../core/models';

@Component({
  selector: 'app-home',
  imports: [FormsModule],
  templateUrl: './home.page.html',
})
export class HomePage {
  private readonly realtime = inject(SignalrRealtimeClient);
  private readonly identity = inject(IdentityService);
  private readonly router = inject(Router);
  private readonly deckStorage = inject(DeckStorageService);

  protected readonly deckOptions = Object.entries(DECK_LABELS) as [DeckType, string][];

  protected sessionName = '';
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
      this.error.set('Please give the session a name.');
      return;
    }
    if (!this.displayName.trim()) {
      this.error.set('Please enter your name.');
      return;
    }

    this.busy.set(true);
    try {
      await this.realtime.connect();
      this.identity.displayName = this.displayName.trim();

      const result = await this.realtime.createSession(
        this.sessionName.trim(),
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
        await this.router.navigate(['/session', result.session.shortCode]);
      } else {
        this.error.set(result.error ?? 'Could not create the session.');
      }
    } catch {
      this.error.set('Could not reach the server. Is the API running?');
    } finally {
      this.busy.set(false);
    }
  }
}
