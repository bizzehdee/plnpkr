import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { HttpClient } from '@angular/common/http';
import { JoinPage } from './join.page';
import { SignalrRealtimeClient } from '../../core/poker.client';
import { SignalrRetroClient } from '../../core/retro.client';
import { IdentityService } from '../../core/identity.service';
import { SessionMembershipService } from '../../core/session-membership.service';
import { JoinStatus } from '../../core/models';

const CODE = 'blue-fox-42';

/** Either tool's client, as far as /join is concerned: connect, then take the seat (#32). */
class FakeClient {
  connect = vi.fn().mockResolvedValue(undefined);
  joinRoom = vi.fn<(...a: unknown[]) => Promise<{ status: JoinStatus; error: string | null }>>()
    .mockResolvedValue({ status: 'Ok', error: null });
}

async function setup(
  tool: 'Poker' | 'Retro' = 'Poker',
  clients: { poker?: FakeClient; retro?: FakeClient } = {},
) {
  const poker = clients.poker ?? new FakeClient();
  const retro = clients.retro ?? new FakeClient();
  TestBed.configureTestingModule({
    imports: [JoinPage],
    providers: [
      provideRouter([]),
      { provide: SignalrRealtimeClient, useValue: poker },
      { provide: SignalrRetroClient, useValue: retro },
      { provide: IdentityService, useValue: { userId: 'me', displayName: '' } },
      { provide: SessionMembershipService, useValue: { remember: vi.fn(), get: () => null } },
      {
        provide: HttpClient,
        useValue: {
          get: () => of({ name: 'Sprint 24', shortCode: CODE, requiresPassword: false, tool }),
        },
      },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => CODE } } } },
    ],
  });
  const fixture = TestBed.createComponent(JoinPage);
  fixture.detectChanges();
  // The form only renders once the landing read has resolved, so the tool is always known by the
  // time join() is reachable. Wait for that here rather than reading a half-initialised component.
  await fixture.whenStable();
  fixture.detectChanges();
  const cmp = fixture.componentInstance as unknown as {
    displayName: string;
    password: string;
    join(): Promise<void>;
  };
  return { fixture, cmp, poker, retro };
}

describe('JoinPage', () => {
  afterEach(() => TestBed.resetTestingModule());

  // --- Which hub the join goes over (#32) ---

  it('joins a poker room over the poker hub', async () => {
    const { cmp, poker, retro } = await setup('Poker');
    cmp.displayName = 'Alice';

    await cmp.join();

    expect(poker.connect).toHaveBeenCalled();
    expect(poker.joinRoom).toHaveBeenCalledWith(CODE, 'me', 'Alice', 'Voter', null);
    expect(retro.joinRoom).not.toHaveBeenCalled();
  });

  it('joins a retro room over the RETRO hub, not the poker one', async () => {
    // The bug this closes: every join went to the poker hub, which for a retro room seated the
    // participant and then failed projecting a poker snapshot — the joiner saw "could not reach the
    // server" with their seat already taken.
    const { cmp, poker, retro } = await setup('Retro');
    cmp.displayName = 'Alice';

    await cmp.join();

    expect(retro.connect).toHaveBeenCalled();
    expect(retro.joinRoom).toHaveBeenCalledWith(CODE, 'me', 'Alice', 'Voter', null);
    expect(poker.connect).not.toHaveBeenCalled();
    expect(poker.joinRoom).not.toHaveBeenCalled();
  });

  it('passes the password to whichever hub it uses', async () => {
    const { cmp, retro } = await setup('Retro');
    cmp.displayName = 'Alice';
    cmp.password = 'hunter2';

    await cmp.join();

    expect(retro.joinRoom).toHaveBeenCalledWith(CODE, 'me', 'Alice', 'Voter', 'hunter2');
  });

  // --- Outcomes ---

  it.each([
    ['NameTaken', 'already taken'],
    ['PasswordRequired', 'requires a password'],
    ['WrongPassword', 'Incorrect password'],
    ['SessionClosed', 'closed'],
    ['SessionFull', 'full'],
    ['RateLimited', 'too often'],
    ['WrongTool', 'different tool'],
  ] as const)('shows a translated message for %s, ignoring the raw server text', async (status, expectedSubstring) => {
    const poker = new FakeClient();
    poker.joinRoom.mockResolvedValue({
      status,
      error: 'SOME RAW SERVER TEXT THAT SHOULD NOT APPEAR',
    });
    const { fixture, cmp } = await setup('Poker', { poker });
    cmp.displayName = 'Alice';

    await cmp.join();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain(expectedSubstring);
    expect(text).not.toContain('SOME RAW SERVER TEXT');
  });

  it('navigates to the session on success and remembers the joined role', async () => {
    const { cmp } = await setup('Poker');
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const membership = TestBed.inject(SessionMembershipService);
    cmp.displayName = 'Alice';

    await cmp.join();

    expect(navigate).toHaveBeenCalledWith(['/poker', CODE]);
    expect(membership.remember).toHaveBeenCalledWith(CODE, 'Voter');
  });

  it('routes to the tool the short code belongs to, not always poker', async () => {
    // One invite-link shape serves both tools (#19), so /join resolves the tool from the landing
    // read and sends the joiner to that tool's page.
    const { cmp } = await setup('Retro');
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    cmp.displayName = 'Alice';

    await cmp.join();

    expect(navigate).toHaveBeenCalledWith(['/retro', CODE]);
  });
});
