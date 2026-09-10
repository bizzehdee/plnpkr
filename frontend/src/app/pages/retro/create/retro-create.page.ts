import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { SignalrRetroClient } from '../../../core/retro.client';
import { IdentityService } from '../../../core/identity.service';
import { I18nService } from '../../../core/i18n.service';
import { TranslatePipe } from '../../../core/translate.pipe';
import { RETRO_TEMPLATE_LABEL_KEYS, RetroTemplate } from '../../../core/models';

/** Create a retro board (#21) — the retro counterpart of the poker create form. */
@Component({
  selector: 'app-retro-create',
  imports: [FormsModule, TranslatePipe],
  templateUrl: './retro-create.page.html',
})
export class RetroCreatePage {
  private readonly retro = inject(SignalrRetroClient);
  private readonly identity = inject(IdentityService);
  private readonly router = inject(Router);
  private readonly i18n = inject(I18nService);
  private readonly route = inject(ActivatedRoute);

  protected boardName = '';
  protected displayName = this.identity.displayName;
  protected template: RetroTemplate = 'WentWellToImprove';
  protected customColumns = '';
  protected facilitate = true;
  protected anonymous = false;
  protected password = '';

  /** Carry-over (#27): the previous retro's short code, and its password if it had one. */
  protected previousCode = '';
  protected previousPassword = '';

  constructor() {
    // "Start the next retro" from a closing board deep-links its code, so the facilitator does not
    // have to copy it across (#27).
    this.previousCode = this.route.snapshot.queryParamMap.get('from') ?? '';
  }

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /** Template options as [value, translated label] pairs, so the labels follow the locale (#5). */
  protected readonly templateOptions = computed(() =>
    (Object.keys(RETRO_TEMPLATE_LABEL_KEYS) as RetroTemplate[]).map(
      (t) => [t, this.i18n.t(RETRO_TEMPLATE_LABEL_KEYS[t])] as const,
    ),
  );

  protected async create(): Promise<void> {
    this.error.set(null);

    if (!this.boardName.trim()) {
      this.error.set(this.i18n.t('retro.create.errorNameRequired'));
      return;
    }
    if (!this.displayName.trim()) {
      this.error.set(this.i18n.t('retro.create.errorYourNameRequired'));
      return;
    }
    if (this.template === 'Custom' && !this.customColumns.trim()) {
      this.error.set(this.i18n.t('retro.create.errorColumnsRequired'));
      return;
    }

    this.busy.set(true);
    try {
      await this.retro.connect();
      this.identity.displayName = this.displayName.trim();

      const result = await this.retro.createBoard(
        this.boardName.trim(),
        this.template,
        this.template === 'Custom' ? this.customColumns.trim() : null,
        this.identity.userId,
        this.displayName.trim(),
        this.facilitate,
        this.password.trim() || null,
        true,
        this.anonymous,
        this.previousCode.trim() || null,
        this.previousPassword.trim() || null,
      );

      switch (result.status) {
        case 'Ok':
          await this.router.navigate(['/retro', result.board!.shortCode]);
          break;
        case 'RateLimited':
          this.error.set(this.i18n.t('err.create.rateLimited'));
          break;
        case 'PreviousBoardPasswordRequired':
          this.error.set(this.i18n.t('retro.create.errorPreviousPassword'));
          break;
        case 'PreviousBoardNotFound':
          this.error.set(this.i18n.t('retro.create.errorPreviousNotFound'));
          break;
        default:
          // The server's message is the specific one (which column list was unusable, etc.).
          this.error.set(result.error ?? this.i18n.t('retro.create.errorFailed'));
          break;
      }
    } catch {
      this.error.set(this.i18n.t('retro.create.errorFailed'));
    } finally {
      this.busy.set(false);
    }
  }
}
