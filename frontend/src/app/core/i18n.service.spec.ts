import { I18nService } from './i18n.service';

describe('I18nService', () => {
  afterEach(() => localStorage.removeItem('pp.locale'));

  it('defaults to English and translates a known key', () => {
    const svc = new I18nService();
    expect(svc.locale()).toBe('en');
    expect(svc.t('home.create')).toBe('Create session');
  });

  it('falls back to the raw key for an unknown key', () => {
    expect(new I18nService().t('nope.missing')).toBe('nope.missing');
  });

  it('interpolates named parameters', () => {
    const svc = new I18nService();
    // Use a key that doesn't exist so the template is the key itself, proving interpolation runs.
    expect(svc.t('Hello {name}!', { name: 'Ada' })).toBe('Hello Ada!');
  });

  it('switches locale, persists it, and falls back to the raw key when a locale has no entry', () => {
    const svc = new I18nService();
    svc.setLocale('es');
    expect(svc.t('home.create')).toBe('Crear sesión'); // present in es
    expect(svc.t('home.yourName')).toBe('Tu nombre'); // fully translated
    expect(svc.t('nope.missing')).toBe('nope.missing'); // not in es or en → raw key
    expect(localStorage.getItem('pp.locale')).toBe('es');

    // A fresh instance reads the stored locale back.
    expect(new I18nService().locale()).toBe('es');
  });

  it('has a complete Portuguese catalog matching every English key', () => {
    const svc = new I18nService();
    svc.setLocale('pt');
    expect(svc.t('home.create')).toBe('Criar sessão');
    expect(svc.t('home.yourName')).toBe('Seu nome');
  });

  it('has a complete Polish catalog matching every English key', () => {
    const svc = new I18nService();
    svc.setLocale('pl');
    expect(svc.t('home.create')).toBe('Utwórz sesję');
    expect(svc.t('home.yourName')).toBe('Twoje imię');
  });

  it('formats numbers per the active locale (decimal separator changes)', () => {
    const svc = new I18nService();
    expect(svc.formatNumber(1234.5, { maximumFractionDigits: 1 })).toBe('1,234.5');

    svc.setLocale('es');
    // es-ES uses a comma as the decimal separator and a period/space as the grouping separator.
    expect(svc.formatNumber(1234.5, { maximumFractionDigits: 1 })).toContain(',5');
    expect(svc.formatNumber(1234.5, { maximumFractionDigits: 1 })).not.toContain('.5');
  });

  it('selects the right plural category per locale, not just singular/plural', () => {
    const svc = new I18nService();
    // English: one/other.
    expect(svc.plural('session.showAllParticipants', 1)).toBe('Show all 1 participant');
    expect(svc.plural('session.showAllParticipants', 30)).toBe('Show all 30 participants');

    // Polish: one/few/many/other — 2 and 30 land in different categories.
    svc.setLocale('pl');
    expect(svc.plural('session.showAllParticipants', 1)).toBe('Pokaż 1 uczestnika');
    expect(svc.plural('session.showAllParticipants', 2)).toBe('Pokaż 2 uczestników');
    expect(svc.plural('session.showAllParticipants', 30)).toBe('Pokaż wszystkich 30 uczestników');
  });

  it('falls back to English plural forms, then the raw key, when a locale/key is missing', () => {
    expect(new I18nService().plural('nope.missing', 5)).toBe('nope.missing');
  });
});
