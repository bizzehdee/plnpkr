import { Pipe, PipeTransform, inject } from '@angular/core';
import { I18nService } from './i18n.service';

/**
 * Translates a pluralizable key via {@link I18nService.plural}: `{{ 'session.showAllParticipants' | plural:count }}`.
 * Selects the CLDR plural category (`Intl.PluralRules`) for the active locale and `count` — not just
 * a singular/plural split, since e.g. Polish has four categories (one/few/many/other).
 *
 * Impure so it re-evaluates when the locale signal changes (see {@link TranslatePipe} for the same
 * rationale).
 */
@Pipe({ name: 'plural', pure: false })
export class PluralPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(key: string, count: number, params?: Record<string, string | number>): string {
    return this.i18n.plural(key, count, params);
  }
}
