import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { IdentityService } from '../../../core/identity.service';
import { SessionMembershipService } from '../../../core/session-membership.service';
import { I18nService } from '../../../core/i18n.service';
import { STANDUP_DEFAULT_QUESTIONS, STANDUP_MAX_QUESTIONS } from '../../../core/models';
import { RoomNameField } from '../../../core/room-name-field';
import { RoomNameService } from '../../../core/room-name.service';
import { SignalrStandupClient } from '../../../core/standup.client';
import { TranslatePipe } from '../../../core/translate.pipe';

/**
 * Open a standup (#36) — the fourth tool's counterpart of the other three create forms.
 *
 * Two things here are not on the other forms. The questions are editable, because "what did you do
 * / what's next / anything in your way" is a convention rather than a law and plenty of teams ask
 * something else. And there is a "carry forward from" field, because each day is its own room: a
 * standup that runs every morning is a chain of rooms, and this is the link.
 */
@Component({
  selector: 'app-standup-create',
  imports: [FormsModule, RoomNameField, TranslatePipe],
  templateUrl: './standup-create.page.html',
})
export class StandupCreatePage {
  private readonly standup = inject(SignalrStandupClient);
  private readonly identity = inject(IdentityService);
  private readonly membership = inject(SessionMembershipService);
  private readonly router = inject(Router);
  private readonly i18n = inject(I18nService);
  private readonly names = inject(RoomNameService);

  /**
   * What this browser last opened a standup as, and whether it was date-stamped. This is the tool
   * the stamp exists for: each day is its own room (#36), so "Team Dragon" plus today's date is how
   * a daily standup gets a name at all.
   */
  private readonly remembered = this.names.recall('Standup');

  protected boardName = this.remembered.name;
  protected appendDate = this.remembered.appendDate;
  protected displayName = this.identity.displayName;
  protected facilitate = true;
  protected password = '';

  /** Editable, starting from the three almost every team already asks. */
  protected questions = [...STANDUP_DEFAULT_QUESTIONS];

  /** Yesterday's standup to carry the questions and open blockers forward from (#36). */
  protected previousShortCode = '';
  protected previousPassword = '';
  protected readonly showCarryOver = signal(false);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly maxQuestions = STANDUP_MAX_QUESTIONS;

  protected get canAddQuestion(): boolean {
    return this.questions.length < STANDUP_MAX_QUESTIONS;
  }

  protected addQuestion(): void {
    if (this.canAddQuestion) {
      this.questions = [...this.questions, ''];
    }
  }

  protected removeQuestion(index: number): void {
    this.questions = this.questions.filter((_, i) => i !== index);
  }

  /** `[(ngModel)]` on an array element needs an explicit setter to keep the array replaced. */
  protected setQuestion(index: number, text: string): void {
    this.questions = this.questions.map((q, i) => (i === index ? text : q));
  }

  protected toggleCarryOver(): void {
    this.showCarryOver.update((shown) => !shown);
  }

  protected async create(): Promise<void> {
    this.error.set(null);

    if (!this.boardName.trim()) {
      this.error.set(this.i18n.t('standup.create.errorNameRequired'));
      return;
    }

    if (!this.displayName.trim()) {
      this.error.set(this.i18n.t('standup.create.errorYourNameRequired'));
      return;
    }

    const asked = this.questions.map((q) => q.trim()).filter((q) => q.length > 0);
    if (asked.length === 0) {
      this.error.set(this.i18n.t('standup.create.errorQuestionsRequired'));
      return;
    }

    this.busy.set(true);
    try {
      await this.standup.connect();
      this.identity.displayName = this.displayName.trim();

      const carryFrom = this.previousShortCode.trim();
      const result = await this.standup.createBoard(
        this.names.compose(this.boardName, this.appendDate),
        this.identity.userId,
        this.displayName.trim(),
        this.facilitate,
        this.password.trim() || null,
        true,
        asked,
        carryFrom || null,
        carryFrom ? this.previousPassword.trim() || null : null,
      );

      if (result.status === 'Ok' && result.board) {
        // The name *as typed*: storing the stamped one would compound the date next time.
        this.names.remember('Standup', this.boardName, this.appendDate);
        // Remember the seat, or a reload would bounce the creator to the join gate (#27's lesson).
        this.membership.remember(result.board.shortCode, 'Voter');
        await this.router.navigate(['/standup', result.board.shortCode]);
        return;
      }

      this.error.set(
        result.status === 'RateLimited'
          ? this.i18n.t('err.create.rateLimited')
          : result.error ?? this.i18n.t('standup.create.errorGeneric'),
      );
    } catch {
      this.error.set(this.i18n.t('common.errorUnreachable'));
    } finally {
      this.busy.set(false);
    }
  }
}
