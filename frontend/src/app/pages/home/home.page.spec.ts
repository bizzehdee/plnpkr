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

  it('presents the unbuilt tool as unavailable rather than as a dead link', () => {
    // A card that navigates nowhere is worse than one that says so. Until #21 ships the retro
    // tool, its action is a real disabled button with an explanation, not a styled-dead anchor.
    const el = render();

    expect(el.querySelector('a[href^="/retro"]')).toBeNull();
    const disabled = el.querySelector<HTMLButtonElement>('button[disabled]');
    expect(disabled).toBeTruthy();
    expect(disabled!.getAttribute('aria-describedby')).toBe('retro-unavailable');
    expect(el.querySelector('#retro-unavailable')?.textContent).toContain('Not available yet');
  });

  it('renders the tools as a list so their number is announced', () => {
    const el = render();

    const items = el.querySelectorAll('ul.list-unstyled > li');
    expect(items.length).toBe(2);
  });
});
