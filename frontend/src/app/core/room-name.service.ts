import { Injectable } from '@angular/core';
import { RoomTool } from './models';

const KEY = 'pp.roomNames';

/** What a tool's create form was last filled in with. */
export interface RememberedRoomName {
  /** The name **as typed**, never with a date on it — see `compose`. */
  readonly name: string;

  /** Whether today's date should be appended when the room is created. */
  readonly appendDate: boolean;
}

const FORGOTTEN: RememberedRoomName = { name: '', appendDate: false };

/**
 * Remembers each tool's last room name, and whether to date-stamp it, in this browser's
 * localStorage — the same opt-in, per-browser, never-server-side mechanism as the display name and
 * the saved decks (#11/#34).
 *
 * Room-level and keyed by `RoomTool`, because all four create forms have the same field and the same
 * problem: a team that runs "Team Dragon" every week should not retype it, and a fifth tool should
 * need no code here. Only the *storage* is shared — each tool remembers its own name, since a poker
 * session and a retro are rarely called the same thing.
 *
 * **What is stored is the name as typed, and the flag, separately.** Storing the composed name would
 * make the date compound: "Team Dragon 2026-09-10" recalled tomorrow and stamped again is
 * "Team Dragon 2026-09-10 2026-09-11". Keeping them apart is the whole reason this is two fields
 * rather than one.
 */
@Injectable({ providedIn: 'root' })
export class RoomNameService {
  /** What this tool was last created with, or a blank name with the stamp off. */
  recall(tool: RoomTool): RememberedRoomName {
    const all = this.readAll();
    const entry = all[tool];
    if (!entry || typeof entry.name !== 'string' || typeof entry.appendDate !== 'boolean') {
      return FORGOTTEN;
    }
    return { name: entry.name, appendDate: entry.appendDate };
  }

  /**
   * Remembers what to offer next time. Pass the name **as typed** — passing a composed name is the
   * one way to break this, and the reason `compose` is a separate call.
   */
  remember(tool: RoomTool, name: string, appendDate: boolean): void {
    const trimmed = name.trim();
    if (!trimmed) {
      return; // nothing worth offering back
    }

    try {
      localStorage.setItem(KEY, JSON.stringify({
        ...this.readAll(),
        [tool]: { name: trimmed, appendDate },
      }));
    } catch {
      // A full or blocked store is not worth failing a room creation over.
    }
  }

  /**
   * The name to actually create the room under: the typed name, plus today's date as `YYYY-MM-DD`
   * when the box is ticked.
   *
   * **Today is the browser's today**, not UTC's. `toISOString()` would date a 9pm session in Sydney
   * as tomorrow and an 8am one in Los Angeles as yesterday — and "which day is this standup" is
   * exactly the question the stamp exists to answer.
   *
   * Any date already on the end is replaced rather than added to, so composing twice is the same as
   * composing once — whether the name came back from storage carrying one or was typed with one.
   */
  compose(name: string, appendDate: boolean, today: Date = new Date()): string {
    const trimmed = name.trim();
    if (!appendDate || !trimmed) {
      return trimmed;
    }

    return `${trimmed.replace(/\s+\d{4}-\d{2}-\d{2}$/, '')} ${localIsoDate(today)}`;
  }

  private readAll(): Partial<Record<RoomTool, RememberedRoomName>> {
    try {
      const raw = localStorage.getItem(KEY);
      if (!raw) {
        return {};
      }
      const parsed: unknown = JSON.parse(raw);
      return parsed && typeof parsed === 'object' && !Array.isArray(parsed)
        ? (parsed as Partial<Record<RoomTool, RememberedRoomName>>)
        : {};
    } catch {
      // Unreadable or unparseable — offer nothing rather than throw on a create form.
      return {};
    }
  }
}

/** `YYYY-MM-DD` in the browser's own timezone. */
function localIsoDate(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}
