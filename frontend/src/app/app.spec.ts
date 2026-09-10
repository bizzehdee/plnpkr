import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { ConnectionStatus, ConnectionStatusService } from './core/connection-status.service';
import { I18nService } from './core/i18n.service';

describe('App shell', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  afterEach(() => localStorage.removeItem('pp.locale'));

  /**
   * The badge's exact text, not the page's. Asserting `textContent` of the whole shell would pass
   * for "disconnected" whenever it looked for "connected" — the words are substrings of each other,
   * which is how this spec used to pass without the shell reading a real status at all.
   */
  function badge(): string {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement)
      .querySelector('#connection-status')!
      .textContent!.trim();
  }

  it('renders the brand', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('TeamTools');
  });

  it('reports disconnected until a tool registers a connection', () => {
    // On the picker no tool client has been constructed (#31), which is the truth: there is no hub
    // connection to report.
    expect(badge()).toBe('disconnected');
  });

  it('reports the status of whichever tool the viewer is in', () => {
    // Each tool registers as its page loads, so the shell does not have to know how many exist.
    const status = signal<ConnectionStatus>('connected');
    TestBed.inject(ConnectionStatusService).register(() => status());

    expect(badge()).toBe('connected');
  });

  it('prefers a live connection when more than one tool has registered', () => {
    // Both clients survive a navigation between tools; the one doing something wins.
    const connections = TestBed.inject(ConnectionStatusService);
    connections.register(() => 'disconnected');
    connections.register(() => 'connecting');

    expect(badge()).toBe('connecting');
  });

  it('renders the footer with website + GitHub links', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('footer a[href="https://www.darrenhorrocks.co.uk"]')).toBeTruthy();
    expect(el.querySelector('footer a[href="https://github.com/bizzehdee/plnpkr"]')).toBeTruthy();
  });

  it('localizes the connection status when the locale is switched (#5)', () => {
    const status = signal<ConnectionStatus>('connected');
    TestBed.inject(ConnectionStatusService).register(() => status());
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const el = () =>
      (fixture.nativeElement as HTMLElement).querySelector('#connection-status')!.textContent!.trim();

    expect(el()).toBe('connected');

    TestBed.inject(I18nService).setLocale('es');
    fixture.detectChanges();

    expect(el()).toBe('conectado');
  });
});
