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

  it('switches locale, persists it, and falls back to English for missing keys', () => {
    const svc = new I18nService();
    svc.setLocale('es');
    expect(svc.t('home.create')).toBe('Crear sesión'); // present in es
    expect(svc.t('home.yourName')).toBe('Your name'); // missing in es → English fallback
    expect(localStorage.getItem('pp.locale')).toBe('es');

    // A fresh instance reads the stored locale back.
    expect(new I18nService().locale()).toBe('es');
  });
});
