import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { HttpClient } from '@angular/common/http';
import { JoinPage } from './join.page';
import { SignalrRealtimeClient } from '../../core/poker.client';
import { IdentityService } from '../../core/identity.service';
import { JoinResult } from '../../core/models';

const CODE = 'blue-fox-42';

class FakeRealtimeClient {
  connect = vi.fn().mockResolvedValue(undefined);
  joinSession = vi.fn<(...a: unknown[]) => Promise<JoinResult>>();
}

function setup(fake: FakeRealtimeClient) {
  TestBed.configureTestingModule({
    imports: [JoinPage],
    providers: [
      provideRouter([]),
      { provide: SignalrRealtimeClient, useValue: fake },
      { provide: IdentityService, useValue: { userId: 'me', displayName: '' } },
      { provide: HttpClient, useValue: { get: () => of({ name: 'Sprint 24', shortCode: CODE, requiresPassword: false }) } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => CODE } } } },
    ],
  });
  const fixture = TestBed.createComponent(JoinPage);
  fixture.detectChanges();
  return fixture;
}

describe('JoinPage', () => {
  afterEach(() => TestBed.resetTestingModule());

  it.each([
    ['NameTaken', 'already taken'],
    ['PasswordRequired', 'requires a password'],
    ['WrongPassword', 'Incorrect password'],
    ['SessionClosed', 'closed'],
    ['SessionFull', 'full'],
    ['RateLimited', 'too often'],
  ] as const)('shows a translated message for %s, ignoring the raw server text', async (status, expectedSubstring) => {
    const fake = new FakeRealtimeClient();
    fake.joinSession.mockResolvedValue({
      status,
      session: null,
      participant: null,
      error: 'SOME RAW SERVER TEXT THAT SHOULD NOT APPEAR',
    });
    const fixture = setup(fake);
    const cmp = fixture.componentInstance as unknown as { displayName: string; join(): Promise<void> };
    cmp.displayName = 'Alice';

    await cmp.join();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain(expectedSubstring);
    expect(text).not.toContain('SOME RAW SERVER TEXT');
  });

  it('navigates to the session on success and remembers the joined role', async () => {
    const fake = new FakeRealtimeClient();
    fake.joinSession.mockResolvedValue({
      status: 'Ok',
      session: null,
      participant: null,
      error: null,
    });
    const fixture = setup(fake);
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const cmp = fixture.componentInstance as unknown as { displayName: string; join(): Promise<void> };
    cmp.displayName = 'Alice';

    await cmp.join();

    expect(navigate).toHaveBeenCalledWith(['/session', CODE]);
  });
});
