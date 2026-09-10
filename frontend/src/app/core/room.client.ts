import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { signal } from '@angular/core';
import {
  ParticipantRole,
  ReactionEvent,
  SessionSnapshot,
  SessionSnapshotWire,
} from './models';

export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected';

export interface PingResponse {
  reply: string;
  connectionId: string;
  serverTimeUtc: string;
}

/** Remembers how to re-join after a transient reconnect (new connection => lost group). */
export interface LastJoin {
  shortCode: string;
  displayName: string;
  role: ParticipantRole;
}

/**
 * Turns the wire snapshot into the flat view model the components read (#19).
 *
 * The server defines room-level state once, in an embedded `room` fragment shared by both tools,
 * which is what keeps a room-level addition from having to be restated per tool. The UI would
 * rather not write `snapshot.room.participants` in a hundred places, so the fragment is spread up
 * here — one mapper at the boundary instead of a change to every call site. `room` is kept on the
 * result for anything without a flat alias (`tool`, `hasPassword`).
 */
export function flattenSession(wire: SessionSnapshotWire): SessionSnapshot {
  return {
    room: wire.room,
    id: wire.room.id,
    shortCode: wire.room.shortCode,
    name: wire.room.name,
    organiserUserId: wire.room.organiserUserId,
    reactionsEnabled: wire.room.reactionsEnabled,
    allowRoleChange: wire.room.allowRoleChange,
    isClosed: wire.room.isClosed,
    participants: wire.room.participants,
    deckType: wire.deckType,
    cards: wire.cards,
    state: wire.state,
    autoReveal: wire.autoReveal,
    currentStory: wire.currentStory,
    currentStoryNote: wire.currentStoryNote,
    stats: wire.stats,
    integration: wire.integration,
    timerDurationSeconds: wire.timerDurationSeconds,
    timerDeadline: wire.timerDeadline,
    timerPausedRemainingSeconds: wire.timerPausedRemainingSeconds,
  };
}

/**
 * The room-level half of the realtime transport (#19): connection lifecycle, presence of a live
 * connection, the shared `RoomClosed` terminal event and ephemeral emoji reactions — everything
 * that is true of any tool's hub. A tool client extends this and adds its own hub methods and
 * snapshot handling.
 */
export abstract class RoomClientBase {
  protected connection: HubConnection | null = null;
  protected readonly _status = signal<ConnectionStatus>('disconnected');
  protected readonly _closed = signal(false);
  protected readonly _reactions = new Subject<ReactionEvent>();

  /** Remembers the last join so a reconnect can reclaim the seat. See #34. */
  protected lastJoin: LastJoin | null = null;
  protected lastUserId = '';

  readonly status = this._status.asReadonly();
  /** Set when the server reports the current room has ended (deleted, or retention-evicted). */
  readonly closed = this._closed.asReadonly();
  readonly reactions$ = this._reactions.asObservable();

  get isConnected(): boolean {
    return this.connection?.state === HubConnectionState.Connected;
  }

  async disconnect(): Promise<void> {
    await this.connection?.stop();
    this._status.set('disconnected');
  }

  /** Ephemeral emoji reaction (#17). Fire-and-forget: never blocks or surfaces an error. */
  async react(emoji: string): Promise<void> {
    if (this.isConnected) {
      await this.connection!.invoke('React', emoji).catch(() => {});
    }
  }
}
