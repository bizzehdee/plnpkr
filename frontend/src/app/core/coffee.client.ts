import { Injectable, signal } from '@angular/core';
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { resolveHubUrl } from './app-config';
import {
  CoffeeActionResult,
  CoffeeBoardSnapshot,
  CoffeeBoardSnapshotWire,
  CoffeeJoinResult,
  CoffeePhase,
  CreateCoffeeResult,
  ExtendChoice,
  ParticipantRole,
} from './models';
import { RoomClientBase, RoomJoinResult } from './room.client';

/**
 * Turns a wire coffee board into the flat view model the components read — the third
 * `flatten*` mapper (#19/#35), spreading the shared room fragment up so the page reads one shape.
 */
export function flattenCoffee(wire: CoffeeBoardSnapshotWire): CoffeeBoardSnapshot {
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
    phase: wire.phase,
    nextPhase: wire.nextPhase,
    previousPhase: wire.previousPhase,
    phaseDurationSeconds: wire.phaseDurationSeconds,
    phaseDeadline: wire.phaseDeadline,
    voteBudget: wire.voteBudget,
    allowMultiplePerItem: wire.allowMultiplePerItem,
    myDotsRemaining: wire.myDotsRemaining,
    voteTotalsVisible: wire.voteTotalsVisible,
    topics: wire.topics,
    hiddenTopicCount: wire.hiddenTopicCount,
    agenda: wire.agenda,
    currentTopicId: wire.currentTopicId,
    extendVote: wire.extendVote,
    decisions: wire.decisions,
  };
}

/** Abstraction over the coffee hub so components test against a fake (no live socket). */
export interface ICoffeeClient {
  readonly board: () => CoffeeBoardSnapshot | null;
  readonly closed: () => boolean;
  connect(): Promise<void>;
  createBoard(
    name: string,
    userId: string,
    displayName: string,
    organise: boolean,
    password?: string | null,
    enableReactions?: boolean,
    timeboxSeconds?: number | null,
  ): Promise<CreateCoffeeResult>;
  joinBoard(
    shortCode: string,
    userId: string,
    displayName: string,
    role: ParticipantRole,
    password?: string | null,
  ): Promise<CoffeeJoinResult>;
  addTopic(shortCode: string, userId: string, text: string): Promise<CoffeeActionResult>;
  castVote(shortCode: string, userId: string, topicId: string): Promise<CoffeeActionResult>;
  advancePhase(shortCode: string, userId: string): Promise<CoffeeActionResult>;
}

@Injectable({ providedIn: 'root' })
export class SignalrCoffeeClient extends RoomClientBase implements ICoffeeClient {
  private readonly _board = signal<CoffeeBoardSnapshot | null>(null);

  readonly board = this._board.asReadonly();

  async connect(): Promise<void> {
    if (this.connection && this.connection.state === HubConnectionState.Connected) {
      return;
    }

    this._status.set('connecting');
    this.connection = new HubConnectionBuilder()
      .withUrl(resolveHubUrl('coffee'))
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

    this.connection.on('BoardUpdated', (board: CoffeeBoardSnapshotWire) =>
      this._board.set(flattenCoffee(board)),
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
    timeboxSeconds: number | null = null,
  ): Promise<CreateCoffeeResult> {
    const result = this.mapResult(
      await this.invoke<CreateCoffeeResult>(
        'CreateBoard', name, userId, displayName, organise, password, enableReactions, timeboxSeconds),
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
  ): Promise<CoffeeJoinResult> {
    const result = this.mapResult(
      await this.invoke<CoffeeJoinResult>(
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

  /** The room-level join contract (#32) — coffee's own `joinBoard` without the snapshot. */
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

  // --- Topics ---------------------------------------------------------------

  addTopic = (shortCode: string, userId: string, text: string) =>
    this.mutate('AddTopic', shortCode, userId, text);

  editTopic = (shortCode: string, userId: string, topicId: string, text: string) =>
    this.mutate('EditTopic', shortCode, userId, topicId, text);

  deleteTopic = (shortCode: string, userId: string, topicId: string) =>
    this.mutate('DeleteTopic', shortCode, userId, topicId);

  // --- Dot voting -----------------------------------------------------------

  castVote = (shortCode: string, userId: string, topicId: string) =>
    this.mutate('CastVote', shortCode, userId, topicId);

  withdrawVote = (shortCode: string, userId: string, topicId: string) =>
    this.mutate('WithdrawVote', shortCode, userId, topicId);

  setVoteBudget = (shortCode: string, userId: string, budget: number) =>
    this.mutate('SetVoteBudget', shortCode, userId, budget);

  setAllowMultiplePerItem = (shortCode: string, userId: string, allow: boolean) =>
    this.mutate('SetAllowMultiplePerItem', shortCode, userId, allow);

  // --- Phases and the discussion -------------------------------------------

  advancePhase = (shortCode: string, userId: string) =>
    this.mutate('AdvancePhase', shortCode, userId);

  previousPhase = (shortCode: string, userId: string) =>
    this.mutate('PreviousPhase', shortCode, userId);

  setPhase = (shortCode: string, userId: string, phase: CoffeePhase) =>
    this.mutate('SetPhase', shortCode, userId, phase);

  setTimebox = (shortCode: string, userId: string, seconds: number | null) =>
    this.mutate('SetTimebox', shortCode, userId, seconds);

  nextTopic = (shortCode: string, userId: string) =>
    this.mutate('NextTopic', shortCode, userId);

  voteOnExtension = (shortCode: string, userId: string, choice: ExtendChoice) =>
    this.mutate('VoteOnExtension', shortCode, userId, choice);

  resolveExtension = (shortCode: string, userId: string) =>
    this.mutate('ResolveExtension', shortCode, userId);

  // --- Decisions ------------------------------------------------------------

  addDecision = (
    shortCode: string,
    userId: string,
    title: string,
    topicId: string | null,
    ownerUserId: string | null,
    ownerName: string | null,
    dueDate: string | null,
  ) => this.mutate('AddDecision', shortCode, userId, title, topicId, ownerUserId, ownerName, dueDate);

  editDecision = (
    shortCode: string,
    userId: string,
    decisionId: string,
    title: string,
    ownerUserId: string | null,
    ownerName: string | null,
    dueDate: string | null,
  ) => this.mutate('EditDecision', shortCode, userId, decisionId, title, ownerUserId, ownerName, dueDate);

  toggleDecisionDone = (shortCode: string, userId: string, decisionId: string) =>
    this.mutate('ToggleDecisionDone', shortCode, userId, decisionId);

  deleteDecision = (shortCode: string, userId: string, decisionId: string) =>
    this.mutate('DeleteDecision', shortCode, userId, decisionId);

  // --- Room-level -----------------------------------------------------------

  setReactionsEnabled = (shortCode: string, userId: string, enabled: boolean) =>
    this.mutate('SetReactionsEnabled', shortCode, userId, enabled);

  changeRole = (shortCode: string, userId: string, targetUserId: string, role: ParticipantRole) =>
    this.mutate('ChangeRole', shortCode, userId, targetUserId, role);

  closeBoard = (shortCode: string, userId: string) =>
    this.mutate('CloseBoard', shortCode, userId);

  deleteBoard = (shortCode: string, userId: string) =>
    this.mutate('DeleteBoard', shortCode, userId);

  // --- Plumbing -------------------------------------------------------------

  private async mutate(method: string, ...args: unknown[]): Promise<CoffeeActionResult> {
    const result = this.mapResult(await this.invoke<CoffeeActionResult>(method, ...args));
    if (result.status === 'Ok' && result.board) {
      this._board.set(result.board);
    }
    return result;
  }

  /** Results arrive with the wire board; map them so callers see one shape (#19). */
  private mapResult<T extends { status: string; board: CoffeeBoardSnapshot | null }>(raw: T): T {
    const wire = raw as unknown as { board: CoffeeBoardSnapshotWire | null };
    return wire.board ? { ...raw, board: flattenCoffee(wire.board) } : raw;
  }

  private async invoke<T>(method: string, ...args: unknown[]): Promise<T> {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) {
      await this.connect();
    }
    return this.connection!.invoke<T>(method, ...args);
  }
}
