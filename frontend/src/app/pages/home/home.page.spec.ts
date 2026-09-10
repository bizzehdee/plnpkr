import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HomePage } from './home.page';

/** The platform front door (#20): a tool picker, not a poker create form. */
describe('HomePage (tool picker)', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HomePage],
      providers: [provideRouter([])],
    });
  });

  function render(): HTMLElement {
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('offers both tools', () => {
    const el = render();

    const text = el.textContent ?? '';
    expect(text).toContain('Planning Poker');
    expect(text).toContain('Team Retro');
  });

  it('links Planning Poker to its create route', () => {
    const el = render();

    const link = el.querySelector<HTMLAnchorElement>('a[href="/poker/new"]');
    expect(link).toBeTruthy();
  });

  it('links Team Retro to its create route now that the tool ships', () => {
    // Was a disabled button with an explanation until #21 built the tool.
    const el = render();

    expect(el.querySelector<HTMLAnchorElement>('a[href="/retro/new"]')).toBeTruthy();
  });

  it('offers every tool as a real link, with nothing left unavailable', () => {
    const el = render();

    expect(el.querySelectorAll('a.btn').length).toBe(2);
    expect(el.querySelector('button[disabled]')).toBeNull();
  });

  it('renders the tools as a list so their number is announced', () => {
    const el = render();

    const items = el.querySelectorAll('ul.list-unstyled > li');
    expect(items.length).toBe(2);
  });
});
