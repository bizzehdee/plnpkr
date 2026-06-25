import { Pipe, PipeTransform, inject } from '@angular/core';
import { I18nService } from './i18n.service';

/**
 * Translates a key via {@link I18nService}: `{{ 'home.title' | t }}`.
 *
 * Impure so it re-evaluates when the locale signal changes (the active locale isn't part of the
 * pipe's argument list, so a pure pipe would cache the first translation). The catalog lookup is
 * a cheap map access, so per-change-detection evaluation is fine at this app's scale.
 */
@Pipe({ name: 't', pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(key: string, params?: Record<string, string | number>): string {
    return this.i18n.t(key, params);
  }
}
