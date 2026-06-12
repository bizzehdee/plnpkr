import { Injectable, computed, effect, signal } from '@angular/core';

export type ThemePreference = 'light' | 'dark' | 'system';
export type EffectiveTheme = 'light' | 'dark';

const THEME_KEY = 'pp.theme';
const PREFERENCES: ThemePreference[] = ['light', 'dark', 'system'];

/** Pure resolver: a preference + the OS dark-mode flag → the concrete theme to apply. See #40. */
export function resolveEffectiveTheme(preference: ThemePreference, prefersDark: boolean): EffectiveTheme {
  if (preference === 'system') {
    return prefersDark ? 'dark' : 'light';
  }
  return preference;
}

/**
 * Frontend-only theme. Persists the preference in localStorage (pp.theme), applies the resolved
 * theme to <html data-bs-theme> (Bootstrap color modes), and follows the OS when set to "system".
 * No server involvement. See #40.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly _preference = signal<ThemePreference>(this.readStored());
  private readonly _prefersDark = signal<boolean>(this.systemPrefersDark());

  readonly preference = this._preference.asReadonly();
  readonly effective = computed(() => resolveEffectiveTheme(this._preference(), this._prefersDark()));

  constructor() {
    effect(() => this.applyToDom(this.effective()));
    this.watchSystem();
  }

  setPreference(preference: ThemePreference): void {
    this._preference.set(preference);
    try {
      localStorage.setItem(THEME_KEY, preference);
    } catch {
      /* storage may be unavailable; theme still applies for this session */
    }
  }

  /** Cycles light → dark → system → light, for a single toggle button. */
  cycle(): void {
    const next = PREFERENCES[(PREFERENCES.indexOf(this._preference()) + 1) % PREFERENCES.length];
    this.setPreference(next);
  }

  private readStored(): ThemePreference {
    const stored = (() => {
      try {
        return localStorage.getItem(THEME_KEY);
      } catch {
        return null;
      }
    })();
    return stored === 'light' || stored === 'dark' || stored === 'system' ? stored : 'system';
  }

  private systemPrefersDark(): boolean {
    return typeof window !== 'undefined' && !!window.matchMedia
      && window.matchMedia('(prefers-color-scheme: dark)').matches;
  }

  private applyToDom(effective: EffectiveTheme): void {
    document.documentElement.setAttribute('data-bs-theme', effective);
  }

  private watchSystem(): void {
    if (typeof window === 'undefined' || !window.matchMedia) {
      return;
    }
    window
      .matchMedia('(prefers-color-scheme: dark)')
      .addEventListener('change', (e) => this._prefersDark.set(e.matches));
  }
}
