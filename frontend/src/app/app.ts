import { Component, computed, inject } from '@angular/core';
import { RouterOutlet, RouterLink } from '@angular/router';
import { SignalrRealtimeClient } from './core/realtime.client';
import { ThemeService } from './core/theme.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly realtime = inject(SignalrRealtimeClient);
  private readonly theme = inject(ThemeService);

  protected readonly status = this.realtime.status;
  protected readonly themePreference = this.theme.preference;

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
