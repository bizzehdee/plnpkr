import { Injectable } from '@angular/core';
import { SavedTrackerConnection } from './models';

const KEY = 'pp.tracker';

/**
 * Optionally remembers the organiser's issue-tracker connection (incl. token) in their own browser's
 * localStorage, so future sessions can reconnect without re-entering it. Opt-in; same mechanism as
 * the display name (#34). The token never leaves this browser except to the server over WSS. See #45.
 */
@Injectable({ providedIn: 'root' })
export class TrackerStorageService {
  get(): SavedTrackerConnection | null {
    const raw = localStorage.getItem(KEY);
    if (!raw) return null;
    try {
      return JSON.parse(raw) as SavedTrackerConnection;
    } catch {
      return null;
    }
  }

  save(connection: SavedTrackerConnection): void {
    localStorage.setItem(KEY, JSON.stringify(connection));
  }

  clear(): void {
    localStorage.removeItem(KEY);
  }
}
