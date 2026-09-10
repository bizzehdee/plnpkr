import { Injectable, signal } from '@angular/core';
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { resolveHubUrl } from './app-config';
import {
  CreateStandupResult,
  ParticipantRole,
  StandupActionResult,
  StandupBoardSnapshot,
  StandupBoardSnapshotWire,
  StandupJoinResult,
} from './models';
import { RoomClientBase, RoomJoinResult } from './room.client';

/**
 * Turns a wire standup board into the flat view model the components read — the fourth `flatten*`
 * mapper (#19/#36), spreading the shared room fragment up so the page reads one shape.
 */
export function flattenStandup(wire: StandupBoardSnapshotWire): StandupBoardSnapshot {
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
    questions: wire.questions,
    iHavePosted: wire.iHavePosted,
    postedCount: wire.postedCount,
    participantCount: wire.participantCount,
    people: wire.people,
    blockers: wire.blockers,
    previousBoardShortCode: wire.previousBoardShortCode,
  };
}

/** Abstraction over the standup hub so components test against a fake (no live socket). */
export interface IStandupClient {
  readonly board: () => StandupBoardSnapshot | null;
  readonly closed: () => boolean;
  connect(): Promise<void>;
  createBoard(
    name: string,
    userId: string,
    displayName: string,
    organise: boolean,
    password?: string | null,
    enableReactions?: boolean,
    questions?: string[] | null,
    previousBoardShortCode?: string | null,
    previousBoardPassword?: string | null,
  ): Promise<CreateStandupResult>;
  joinBoard(
    shortCode: string,
    userId: string,
    displayName: string,
    role: ParticipantRole,
    password?: string | null,
  ): Promise<StandupJoinResult>;
  answer(
    shortCode: string,
    userId: string,
    questionId: string,
    text: string,
  ): Promise<StandupActionResult>;
  addBlocker(shortCode: string, userId: string, text: string): Promise<StandupActionResult>;
}

@Injectable({ providedIn: 'root' })
export class SignalrStandupClient extends RoomClientBase implements IStandupClient {
  private readonly _board = signal<StandupBoardSnapshot | null>(null);

  readonly board = this._board.asReadonly();

  async connect(): Promise<void> {
    if (this.connection && this.connection.state === HubConnectionState.Connected) {
      return;
    }

    this._status.set('connecting');
    this.connection = new HubConnectionBuilder()
      .withUrl(resolveHubUrl('standup'))
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.onreconnecting(() => this._status.set('connecting'));
    this.connection.onreconnected(async () => {
      this._status.set('connected');
      // A reconnect is a new connection with no group membership — re-join to reclaim the seat.
      if (this.lastJoin) {
        const { shortCode, displayName, role } = this.lastJoin;
        await this.joinBoard(shortCode, this.lastUserId, displayName, role).catch(() => {});
      }
    });
    this.connection.onclose(() => this._status.set('disconnected'));

    this.connection.on('BoardUpdated', (board: StandupBoardSnapshotWire) =>
      this._board.set(flattenStandup(board)),
    );
    this.connection.on('BoardClosed', () => {
      this._board.set(null);
      this._closed.set(true);
    });
    this.connection.on('ReactionReceived', (userId: string, emoji: string) =>
      this._reactions.next({ userId, emoji }),
    );

    try {
      await this.connection.start();
      this._status.set('connected');
    } catch (err) {
      this._status.set('disconnected');
      throw err;
    }
  }

  async createBoard(
    name: string,
    userId: string,
    displayName: string,
    organise: boolean,
    password: string | null = null,
    enableReactions = true,
    questions: string[] | null = null,
    previousBoardShortCode: string | null = null,
    previousBoardPassword: string | null = null,
  ): Promise<CreateStandupResult> {
    const result = this.mapResult(
      await this.invoke<CreateStandupResult>(
        'CreateBoard', name, userId, displayName, organise, password, enableReactions, questions,
        previousBoardShortCode, previousBoardPassword),
    );
    if (result.status === 'Ok' && result.board) {
      this._closed.set(false);
      this.lastUserId = userId;
      this.lastJoin = { shortCode: result.board.shortCode, displayName, role: 'Voter' };
      this._board.set(result.board);
    }
    return result;
  }

  async joinBoard(
    shortCode: string,
    userId: string,
    displayName: string,
    role: ParticipantRole,
    password: string | null = null,
  ): Promise<StandupJoinResult> {
    const result = this.mapResult(
      await this.invoke<StandupJoinResult>(
        'JoinBoard', shortCode, userId, displayName, role, password),
    );
    if (result.status === 'Ok' && result.board) {
      this._closed.set(false);
      this.lastUserId = userId;
      this.lastJoin = { shortCode, displayName, role };
      this._board.set(result.board);
    }
    return result;
  }

  /** The room-level join contract (#32) — standup's own `joinBoard` without the snapshot. */
  joinRoom(
    shortCode: string,
    userId: string,
    displayName: string,
    role: ParticipantRole,
    password: string | null = null,
  ): Promise<RoomJoinResult> {
    return this.joinBoard(shortCode, userId, displayName, role, password);
  }

  async leaveBoard(shortCode: string, userId: string): Promise<void> {
    if (this.isConnected) {
      await this.connection!.invoke('LeaveBoard', shortCode, userId);
    }
    this.lastJoin = null; // intentional leave — don't auto-rejoin on a later reconnect
    this._board.set(null);
  }

  // --- Answering ------------------------------------------------------------

  /** Saves one answer. An empty string clears it — and clearing them all un-posts you (#36). */
  answer = (shortCode: string, userId: string, questionId: string, text: string) =>
    this.mutate('Answer', shortCode, userId, questionId, text);

  // --- Blockers -------------------------------------------------------------

  addBlocker = (shortCode: string, userId: string, text: string) =>
    this.mutate('AddBlocker', shortCode, userId, text);

  assignBlocker = (
    shortCode: string,
    userId: string,
    blockerId: string,
    ownerUserId: string | null,
    ownerName: string | null,
  ) => this.mutate('AssignBlocker', shortCode, userId, blockerId, ownerUserId, ownerName);

  toggleBlockerResolved = (shortCode: string, userId: string, blockerId: string) =>
    this.mutate('ToggleBlockerResolved', shortCode, userId, blockerId);

  deleteBlocker = (shortCode: string, userId: string, blockerId: string) =>
    this.mutate('DeleteBlocker', shortCode, userId, blockerId);

  // --- Room-level -----------------------------------------------------------

  setReactionsEnabled = (shortCode: string, userId: string, enabled: boolean) =>
    this.mutate('SetReactionsEnabled', shortCode, userId, enabled);

  closeBoard = (shortCode: string, userId: string) =>
    this.mutate('CloseBoard', shortCode, userId);

  deleteBoard = (shortCode: string, userId: string) =>
    this.mutate('DeleteBoard', shortCode, userId);

  // --- Plumbing -------------------------------------------------------------

  private async mutate(method: string, ...args: unknown[]): Promise<StandupActionResult> {
    const result = this.mapResult(await this.invoke<StandupActionResult>(method, ...args));
    if (result.status === 'Ok' && result.board) {
      this._board.set(result.board);
    }
    return result;
  }

  /** Results arrive with the wire board; map them so callers see one shape (#19). */
  private mapResult<T extends { status: string; board: StandupBoardSnapshot | null }>(raw: T): T {
    const wire = raw as unknown as { board: StandupBoardSnapshotWire | null };
    return wire.board ? { ...raw, board: flattenStandup(wire.board) } : raw;
  }

  private async invoke<T>(method: string, ...args: unknown[]): Promise<T> {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) {
      await this.connect();
    }
    return this.connection!.invoke<T>(method, ...args);
  }
}
