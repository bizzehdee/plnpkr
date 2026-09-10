import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { provideZonelessChangeDetection } from '@angular/core';
import { routes } from './app.routes';

/**
 * Platform routing (#20). The redirect matters most: `/session/:shortCode` links are already in
 * people's calendars and chat history, and they must keep working after the split into per-tool
 * prefixes.
 */
describe('platform routes', () => {
  let router: Router;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideRouter(routes)],
    });
    router = TestBed.inject(Router);
  });

  it('keeps legacy /session links working by redirecting to the poker tool', async () => {
    await router.navigateByUrl('/session/blue-fox-42');

    expect(router.url).toBe('/poker/blue-fox-42');
  });

  it('serves the poker table under its own tool prefix', async () => {
    await router.navigateByUrl('/poker/blue-fox-42');

    expect(router.url).toBe('/poker/blue-fox-42');
  });

  it('serves the poker create form at /poker/new', async () => {
    await router.navigateByUrl('/poker/new');

    expect(router.url).toBe('/poker/new');
  });

  it('sends an unknown path back to the picker', async () => {
    await router.navigateByUrl('/nonsense/path');

    expect(router.url).toBe('/');
  });
});
