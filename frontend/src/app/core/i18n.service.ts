import { Injectable, signal } from '@angular/core';

/** Supported UI locales. Add a locale by extending this union and the CATALOGS map below. */
export type Locale = 'en' | 'es';

const LOCALE_KEY = 'pp.locale';

/** Human-readable names for the language switcher. */
export const LOCALE_LABELS: Record<Locale, string> = {
  en: 'English',
  es: 'Español',
};

/**
 * Translation catalogs. `en` is the complete base; other locales may be partial and fall back
 * to English (then to the raw key) for anything missing. Keys are dotted by area, e.g. "home.title".
 */
const CATALOGS: Record<Locale, Record<string, string>> = {
  en: {
    'app.brand': 'PlnPkr',
    'app.toggleTheme': 'Toggle theme',
    'app.language': 'Language',
    'app.support': 'Support on Ko-Fi',
    'app.github': 'GitHub',
    'app.copyright': 'PlnPkr — © 2026',
    'conn.connected': 'connected',
    'conn.connecting': 'connecting',
    'conn.disconnected': 'disconnected',
    'home.title': 'Start a planning session',
    'home.sessionName': 'Session name',
    'home.sessionNamePlaceholder': 'e.g. Sprint 24 backlog',
    'home.yourName': 'Your name',
    'home.yourNamePlaceholder': 'e.g. Alice',
    'home.cardDeck': 'Card deck',
    'home.customCards': 'Custom cards (comma-separated)',
    'home.customCardsPlaceholder': 'e.g. 1, 2, 3, 5, 8',
    'home.password': 'Password',
    'home.optional': '(optional)',
    'home.passwordPlaceholder': 'Leave blank for no password',
    'home.passwordHelp': 'If set, joiners must enter this password.',
    'home.organise': "I'll organise this session",
    'home.creating': 'Creating…',
    'home.create': 'Create session',
  },
  es: {
    // A starter Spanish catalog covering the app shell; anything missing falls back to English.
    'app.toggleTheme': 'Cambiar tema',
    'app.language': 'Idioma',
    'app.support': 'Apóyanos en Ko-Fi',
    'app.github': 'GitHub',
    'app.copyright': 'PlnPkr — © 2026',
    'conn.connected': 'conectado',
    'conn.connecting': 'conectando',
    'conn.disconnected': 'desconectado',
    'home.title': 'Iniciar una sesión de planificación',
    'home.create': 'Crear sesión',
  },
};

/**
 * Runtime, signal-based i18n (#5). No build-per-locale step — the locale is a signal so switching
 * re-renders the app instantly. Persists the choice in localStorage. New UI strings should be added
 * to the `en` catalog and referenced via the `t` pipe rather than hard-coded.
 */
@Injectable({ providedIn: 'root' })
export class I18nService {
  private readonly _locale = signal<Locale>(this.readStored());
  readonly locale = this._locale.asReadonly();

  /** Locales offered in the switcher, with display labels. */
  readonly available: { id: Locale; label: string }[] = (Object.keys(LOCALE_LABELS) as Locale[]).map(
    (id) => ({ id, label: LOCALE_LABELS[id] }),
  );

  setLocale(locale: Locale): void {
    this._locale.set(locale);
    try {
      localStorage.setItem(LOCALE_KEY, locale);
    } catch {
      /* storage may be unavailable; the locale still applies for this session */
    }
  }

  /**
   * Translate a key for the active locale, falling back to English then the raw key.
   * `params` interpolate `{name}` placeholders.
   */
  t(key: string, params?: Record<string, string | number>): string {
    const locale = this._locale();
    const template = CATALOGS[locale][key] ?? CATALOGS.en[key] ?? key;
    if (!params) return template;
    return template.replace(/\{(\w+)\}/g, (_, name: string) =>
      name in params ? String(params[name]) : `{${name}}`,
    );
  }

  private readStored(): Locale {
    try {
      const stored = localStorage.getItem(LOCALE_KEY);
      if (stored && stored in CATALOGS) return stored as Locale;
    } catch {
      /* ignore */
    }
    return 'en';
  }
}
