import { Injectable } from '@angular/core';

const USER_ID_KEY = 'pp.userId';
const DISPLAY_NAME_KEY = 'pp.displayName';

/**
 * Anonymous identity persisted in localStorage: a stable per-browser userId (never re-prompted)
 * and the last display name (pre-filled on the join screen). No accounts. See #34.
 */
@Injectable({ providedIn: 'root' })
export class IdentityService {
  /** Stable id for this browser; generated once and reused. */
  readonly userId = this.getOrCreateUserId();

  get displayName(): string {
    return localStorage.getItem(DISPLAY_NAME_KEY) ?? '';
  }

  set displayName(name: string) {
    localStorage.setItem(DISPLAY_NAME_KEY, name);
  }

  private getOrCreateUserId(): string {
    let id = localStorage.getItem(USER_ID_KEY);
    if (!id) {
      id = crypto.randomUUID();
      localStorage.setItem(USER_ID_KEY, id);
    }
    return id;
  }
}
