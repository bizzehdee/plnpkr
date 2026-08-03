import { Injectable } from '@angular/core';
import { ParticipantRole } from './models';

const KEY = 'pp.joinedSessions';

type MembershipMap = Record<string, ParticipantRole>;

/**
 * Remembers, per browser, which sessions this identity has already joined (and in which role), so a
 * full page reload (F5) can silently reconnect instead of bouncing back to the join screen. The
 * server already treats a rejoin with a known userId as reclaiming the existing seat (no password
 * re-challenge, vote/role preserved) — this just lets the client know it's safe to try.
 */
@Injectable({ providedIn: 'root' })
export class SessionMembershipService {
  private map(): MembershipMap {
    const raw = localStorage.getItem(KEY);
    if (!raw) return {};
    try {
      const parsed = JSON.parse(raw);
      return parsed && typeof parsed === 'object' ? (parsed as MembershipMap) : {};
    } catch {
      return {};
    }
  }

  /** The role last known for this session, or null if we've never joined it in this browser. */
  get(shortCode: string): ParticipantRole | null {
    return this.map()[shortCode] ?? null;
  }

  /** Record (or update) the role we joined/hold in a session. */
  remember(shortCode: string, role: ParticipantRole): void {
    const next = { ...this.map(), [shortCode]: role };
    localStorage.setItem(KEY, JSON.stringify(next));
  }

  /** Forget a session (e.g. on an intentional leave, or once it's gone). */
  forget(shortCode: string): void {
    const next = this.map();
    delete next[shortCode];
    localStorage.setItem(KEY, JSON.stringify(next));
  }
}
