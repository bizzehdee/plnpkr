import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { SignalrRealtimeClient, ConnectionStatus } from './core/realtime.client';

class FakeRealtimeClient {
  private readonly _status = signal<ConnectionStatus>('connected');
  readonly status = this._status.asReadonly();
  readonly session = signal(null).asReadonly();
}

describe('App shell', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([]),
        { provide: SignalrRealtimeClient, useValue: new FakeRealtimeClient() },
      ],
    }).compileComponents();
  });

  it('renders the brand and connection status', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('PlnPkr');
    expect(text).toContain('connected');
  });

  it('renders the footer with website + GitHub links', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('footer a[href="https://www.darrenhorrocks.co.uk"]')).toBeTruthy();
    expect(el.querySelector('footer a[href="https://github.com/bizzehdee/plnpkr"]')).toBeTruthy();
  });
});
