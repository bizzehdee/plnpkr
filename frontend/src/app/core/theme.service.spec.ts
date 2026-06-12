import { TestBed } from '@angular/core/testing';
import { resolveEffectiveTheme, ThemeService } from './theme.service';

describe('resolveEffectiveTheme', () => {
  it('returns the explicit preference for light/dark', () => {
    expect(resolveEffectiveTheme('light', true)).toBe('light');
    expect(resolveEffectiveTheme('dark', false)).toBe('dark');
  });

  it('follows the OS when set to system', () => {
    expect(resolveEffectiveTheme('system', true)).toBe('dark');
    expect(resolveEffectiveTheme('system', false)).toBe('light');
  });
});

describe('ThemeService', () => {
  beforeEach(() => localStorage.removeItem('pp.theme'));

  it('applies the chosen theme to <html data-bs-theme> and persists it', () => {
    const service = TestBed.inject(ThemeService);

    service.setPreference('dark');
    TestBed.tick(); // flush the effect that writes to the DOM

    expect(document.documentElement.getAttribute('data-bs-theme')).toBe('dark');
    expect(localStorage.getItem('pp.theme')).toBe('dark');
  });

  it('cycles light → dark → system', () => {
    const service = TestBed.inject(ThemeService);
    service.setPreference('light');

    service.cycle();
    expect(service.preference()).toBe('dark');
    service.cycle();
    expect(service.preference()).toBe('system');
    service.cycle();
    expect(service.preference()).toBe('light');
  });
});
