import { Component, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NavigationEnd, Router, RouterOutlet, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { SignalrRealtimeClient } from './core/poker.client';
import { ThemeService } from './core/theme.service';
import { I18nService, Locale } from './core/i18n.service';
import { TranslatePipe } from './core/translate.pipe';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, FormsModule, TranslatePipe],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly realtime = inject(SignalrRealtimeClient);
  private readonly theme = inject(ThemeService);
  private readonly i18n = inject(I18nService);

  protected readonly status = this.realtime.status;
  protected readonly themePreference = this.theme.preference;

  /** The shell's "all tools" link is pointless on the picker itself. See #20. */
  private readonly url = toSignal(
    inject(Router).events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map((e) => e.urlAfterRedirects),
    ),
    { initialValue: inject(Router).url },
  );
  protected readonly showToolSwitcher = computed(() => this.url() !== '/');

  // --- Language switcher (#5) ---
  protected readonly locales = this.i18n.available;
  protected get locale(): Locale {
    return this.i18n.locale();
  }
  protected set locale(value: Locale) {
    this.i18n.setLocale(value);
  }

  /** Localized connection-status label for the badge. */
  protected readonly statusLabel = computed(() => this.i18n.t(`conn.${this.status()}`));

  protected readonly themeIcon = computed(() => {
    switch (this.themePreference()) {
      case 'light':
        return '☀️';
      case 'dark':
        return '🌙';
      default:
        return '🖥️';
    }
  });

  protected cycleTheme(): void {
    this.theme.cycle();
  }

  protected statusClass(): string {
    switch (this.status()) {
      case 'connected':
        return 'bg-success';
      case 'connecting':
        return 'bg-warning text-dark';
      default:
        return 'bg-secondary';
    }
  }
}
