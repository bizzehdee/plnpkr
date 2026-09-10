import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { SignalrCoffeeClient } from '../../../core/coffee.client';
import { IdentityService } from '../../../core/identity.service';
import { SessionMembershipService } from '../../../core/session-membership.service';
import { I18nService } from '../../../core/i18n.service';
import { TranslatePipe } from '../../../core/translate.pipe';

/** Create a Lean Coffee (#35) — the third tool's counterpart of the other two create forms. */
@Component({
  selector: 'app-coffee-create',
  imports: [FormsModule, TranslatePipe],
  templateUrl: './coffee-create.page.html',
})
export class CoffeeCreatePage {
  private readonly coffee = inject(SignalrCoffeeClient);
  private readonly identity = inject(IdentityService);
  private readonly membership = inject(SessionMembershipService);
  private readonly router = inject(Router);
  private readonly i18n = inject(I18nService);

  protected boardName = '';
  protected displayName = this.identity.displayName;
  protected facilitate = true;
  protected password = '';

  /** The per-topic timebox, in minutes — the unit facilitators actually think in. */
  protected timeboxMinutes = 5;

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /** Five minutes is the Lean Coffee convention; the rest are the usual variations. */
  protected readonly timeboxOptions = [2, 3, 5, 8, 10];

  protected async create(): Promise<void> {
    this.error.set(null);

    if (!this.boardName.trim()) {
      this.error.set(this.i18n.t('coffee.create.errorNameRequired'));
      return;
    }

    if (!this.displayName.trim()) {
      this.error.set(this.i18n.t('coffee.create.errorYourNameRequired'));
      return;
    }

    this.busy.set(true);
    try {
      await this.coffee.connect();
      this.identity.displayName = this.displayName.trim();

      const result = await this.coffee.createBoard(
        this.boardName.trim(),
        this.identity.userId,
        this.displayName.trim(),
        this.facilitate,
        this.password.trim() || null,
        true,
        this.timeboxMinutes * 60,
      );

      if (result.status === 'Ok' && result.board) {
        // Remember the seat, or a reload would bounce the creator to the join gate (#27's lesson).
        this.membership.remember(result.board.shortCode, 'Voter');
        await this.router.navigate(['/coffee', result.board.shortCode]);
        return;
      }

      this.error.set(
        result.status === 'RateLimited'
          ? this.i18n.t('err.create.rateLimited')
          : result.error ?? this.i18n.t('coffee.create.errorGeneric'),
      );
    } catch {
      this.error.set(this.i18n.t('common.errorUnreachable'));
    } finally {
      this.busy.set(false);
    }
  }
}
