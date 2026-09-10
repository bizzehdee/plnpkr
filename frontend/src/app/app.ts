import { Component, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NavigationEnd, Router, RouterOutlet, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { SignalrRealtimeClient } from './core/poker.client';
import { SignalrRetroClient } from './core/retro.client';
import { ConnectionStatus } from './core/room.client';
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
  private readonly poker = inject(SignalrRealtimeClient);
  private readonly retro = inject(SignalrRetroClient);
  private readonly theme = inject(ThemeService);
  private readonly i18n = inject(I18nService);

  /**
   * The badge reports the tool the viewer is actually in. Each tool has its own hub connection
   * (#21), so watching only poker's would read "disconnected" on a live retro board. Whichever
   * client is doing something wins; on the picker neither is connected, which is the truth.
   */
  protected readonly status = computed<ConnectionStatus>(() => {
    const statuses = [this.poker.status(), this.retro.status()];
    if (statuses.includes('connected')) return 'connected';
    if (statuses.includes('connecting')) return 'connecting';
    return 'disconnected';
  });

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
