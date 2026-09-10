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

  function render(): { el: HTMLElement; tools: number } {
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();
    const tools = (fixture.componentInstance as unknown as { tools: unknown[] }).tools.length;
    return { el: fixture.nativeElement as HTMLElement, tools };
  }

  it('offers every tool the platform hosts', () => {
    const { el } = render();

    const text = el.textContent ?? '';
    expect(text).toContain('Planning Poker');
    expect(text).toContain('Team Retro');
    expect(text).toContain('Lean Coffee');
    expect(text).toContain('Async Standup');
  });

  it.each([
    ['Planning Poker', '/poker/new'],
    ['Team Retro', '/retro/new'],
    ['Lean Coffee', '/coffee/new'],
    ['Async Standup', '/standup/new'],
  ])('links %s to its create route', (_name, href) => {
    const { el } = render();

    expect(el.querySelector<HTMLAnchorElement>(`a[href="${href}"]`)).toBeTruthy();
  });

  it('offers every tool as a real link, with nothing left unavailable', () => {
    // Counted against the component's own list rather than a literal, so a fourth tool does not
    // need this spec edited — only the route assertion above.
    const { el, tools } = render();

    expect(el.querySelectorAll('a.btn').length).toBe(tools);
    expect(el.querySelector('button[disabled]')).toBeNull();
  });

  it('renders the tools as a list so their number is announced', () => {
    const { el, tools } = render();

    expect(el.querySelectorAll('ul.list-unstyled > li').length).toBe(tools);
  });
});
