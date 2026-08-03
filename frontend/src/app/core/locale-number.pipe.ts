import { Pipe, PipeTransform, inject } from '@angular/core';
import { I18nService } from './i18n.service';

/**
 * Locale-aware number formatting via {@link I18nService.formatNumber}: `{{ value | ln:{ maximumFractionDigits: 2 } }}`.
 * Unlike Angular's built-in `number` pipe (fixed to the app's compiled `LOCALE_ID`), this re-formats
 * against whichever UI language is currently selected — decimal separators, grouping, etc. change
 * with the locale switcher instead of always rendering en-US style.
 *
 * Impure so it re-evaluates when the locale signal changes (see {@link TranslatePipe} for the same
 * rationale).
 */
@Pipe({ name: 'ln', pure: false })
export class LocaleNumberPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(value: number | null | undefined, options?: Intl.NumberFormatOptions): string {
    if (value === null || value === undefined) return '';
    return this.i18n.formatNumber(value, options);
  }
}
