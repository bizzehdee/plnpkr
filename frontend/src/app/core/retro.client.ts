import { Injectable, signal } from '@angular/core';
import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { Observable } from 'rxjs';
import { resolveHubUrl } from './app-config';
import {
  CreateRetroResult,
  ParticipantRole,
  ReactionEvent,
  RetroActionResult,
  RetroBoardSnapshot,
  RetroBoardSnapshotWire,
  RetroJoinResult,
  RetroTemplate,
  RetroVoteTarget,
} from './models';
import { ConnectionStatus, RoomClientBase } from './room.client';

/**
 * Turns a wire retro board into the flat view model the components read (#21) — the retro
 * counterpart of `flattenSession`, spreading the shared room fragment up so the board page reads
 * one shape.
 */
export function flattenBoard(wire: RetroBoardSnapshotWire): RetroBoardSnapshot {
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
    template: wire.template,
    phase: wire.phase,
    nextPhase: wire.nextPhase,
    previousPhase: wire.previousPhase,
    phaseDurationSeconds: wire.phaseDurationSeconds,
    phaseDeadline: wire.phaseDeadline,
    anonymous: wire.anonymous,
    canChangeAnonymity: wire.canChangeAnonymity,
    allowParticipantGrouping: wire.allowParticipantGrouping,
    voteBudget: wire.voteBudget,
    allowMultiplePerItem: wire.allowMultiplePerItem,
    myDotsRemaining: wire.myDotsRemaining,
    voteTotalsVisible: wire.voteTotalsVisible,
    columns: wire.columns,
    groups: wire.groups,
    ranking: wire.ranking,
    actions: wire.actions,
  };
}

/**
 * Abstraction over the retro hub so components test against a fake (no live socket), mirroring
 * `IRealtimeClient` for poker.
 */
export interface IRetroClient {
  readonly status: () => ConnectionStatus;
  readonly board: () => RetroBoardSnapshot | null;
  readonly closed: () => boolean;
  connect(): Promise<void>;
  disconnect(): Promise<void>;
  createBoard(
    name: string,
    template: RetroTemplate,
    customColumns: string | null,
    userId: string,
    displayName: string,
    organise: boolean,
    password?: string | null,
    enableReactions?: boolean,
    anonymous?: boolean,
  ): Promise<CreateRetroResult>;
  joinBoard(
    shortCode: string,
    userId: string,
    displayName: string,
    role: ParticipantRole,
    password?: string | null,
  ): Promise<RetroJoinResult>;
  leaveBoard(shortCode: string, userId: string): Promise<void>;
  addCard(shortCode: string, userId: string, columnId: string, text: string): Promise<RetroActionResult>;
  editCard(shortCode: string, userId: string, cardId: string, text: string): Promise<RetroActionResult>;
  deleteCard(shortCode: string, userId: string, cardId: string): Promise<RetroActionResult>;
  moveCard(
    shortCode: string,
    userId: string,
    cardId: string,
    targetColumnId: string,
    targetOrder: number,
  ): Promise<RetroActionResult>;
  setTemplate(
    shortCode: string,
    userId: string,
    template: RetroTemplate,
    customColumns: string | null,
  ): Promise<RetroActionResult>;
  setAnonymous(shortCode: string, userId: string, anonymous: boolean): Promise<RetroActionResult>;
  advancePhase(shortCode: string, userId: string, seconds: number | null): Promise<RetroActionResult>;
  previousPhase(shortCode: string, userId: string): Promise<RetroActionResult>;
  setPhaseDuration(shortCode: string, userId: string, seconds: number | null): Promise<RetroActionResult>;
  groupCards(
    shortCode: string,
    userId: string,
    cardIds: string[],
    targetGroupId: string | null,
  ): Promise<RetroActionResult>;
  ungroupCard(shortCode: string, userId: string, cardId: string): Promise<RetroActionResult>;
  renameGroup(shortCode: string, userId: string, groupId: string, label: string): Promise<RetroActionResult>;
  setAllowParticipantGrouping(shortCode: string, userId: string, allowed: boolean): Promise<RetroActionResult>;
  castVote(
    shortCode: string,
    userId: string,
    kind: RetroVoteTarget,
    targetId: string,
  ): Promise<RetroActionResult>;
  withdrawVote(
    shortCode: string,
    userId: string,
    kind: RetroVoteTarget,
    targetId: string,
  ): Promise<RetroActionResult>;
  setVoteBudget(
    shortCode: string,
    userId: string,
    budget: number,
    allowMultiplePerItem: boolean,
  ): Promise<RetroActionResult>;
  addAction(
    shortCode: string,
    userId: string,
    title: string,
    ownerUserId: string | null,
    ownerName: string | null,
    dueDate: string | null,
    sourceGroupId: string | null,
  ): Promise<RetroActionResult>;
  editAction(
    shortCode: string,
    userId: string,
    actionId: string,
    title: string,
    ownerUserId: string | null,
    ownerName: string | null,
    dueDate: string | null,
  ): Promise<RetroActionResult>;
  toggleActionDone(shortCode: string, userId: string, actionId: string): Promise<RetroActionResult>;
  deleteAction(shortCode: string, userId: string, actionId: string): Promise<RetroActionResult>;
  closeBoard(shortCode: string, userId: string): Promise<RetroActionResult>;
  deleteBoard(shortCode: string, userId: string): Promise<RetroActionResult>;
  setPassword(shortCode: string, userId: string, password: string | null): Promise<RetroActionResult>;
  setReactionsEnabled(shortCode: string, userId: string, enabled: boolean): Promise<RetroActionResult>;
  setAllowRoleChange(shortCode: string, userId: string, enabled: boolean): Promise<RetroActionResult>;
  changeRole(
    shortCode: string,
    userId: string,
    targetUserId: string,
    role: ParticipantRole,
  ): Promise<RetroActionResult>;
  promoteToOrganiser(shortCode: string, userId: string, targetUserId: string): Promise<RetroActionResult>;
  demoteOrganiser(shortCode: string, userId: string, targetUserId: string): Promise<RetroActionResult>;
  transferOrganiser(shortCode: string, userId: string, targetUserId: string): Promise<RetroActionResult>;
  react(emoji: string): Promise<void>;
  readonly reactions$: Observable<ReactionEvent>;
}

/**
 * The Team Retro half of the realtime client (#21). Connection lifecycle, the terminal
 * board-closed event and emoji reactions come from {@link RoomClientBase}, shared with poker.
 *
 * Note the board arrives via a per-connection `BoardUpdated` push rather than a group broadcast:
 * what a recipient may see differs (anonymity #22, hidden collection #23), so the server projects
 * per viewer.
 */
@Injectable({ providedIn: 'root' })
export class SignalrRetroClient extends RoomClientBase implements IRetroClient {
  private readonly _board = signal<RetroBoardSnapshot | null>(null);

  readonly board = this._board.asReadonly();

  async connect(): Promise<void> {
    if (this.connection && this.connection.state === HubConnectionState.Connected) {
      return;
    }

    this._status.set('connecting');
    this.connection = new HubConnectionBuilder()
      .withUrl(resolveHubUrl('retro'))
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.onreconnecting(() => this._status.set('connecting'));
    this.connection.onreconnected(async () => {
      this._status.set('connected');
      // A reconnect is a brand-new connection with no group membership, and the server marked us
      // away — re-join to reclaim our seat and resume receiving pushes. See #34.
      if (this.lastJoin) {
        const { shortCode, displayName, role } = this.lastJoin;
        await this.joinBoard(shortCode, this.lastUserId, displayName, role).catch(() => {});
      }
    });
    this.connection.onclose(() => this._status.set('disconnected'));

    this.connection.on('BoardUpdated', (board: RetroBoardSnapshotWire) =>
      this._board.set(flattenBoard(board)),
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
    template: RetroTemplate,
    customColumns: string | null,
    userId: string,
    displayName: string,
    organise: boolean,
    password: string | null = null,
    enableReactions = true,
    anonymous = false,
  ): Promise<CreateRetroResult> {
    const result = this.mapResult(
      await this.invoke<CreateRetroResult>(
        'CreateBoard',
        name,
        template,
        customColumns,
        userId,
        displayName,
        organise,
        password,
        enableReactions,
        anonymous,
      ),
    );

    if (result.status === 'Ok' && result.board) {
      this._closed.set(false);
      this.lastUserId = userId;
      this.lastJoin = {
        shortCode: result.board.shortCode,
        displayName,
        role: organise ? 'Observer' : 'Voter',
      };
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
  ): Promise<RetroJoinResult> {
    const result = this.mapResult(
      await this.invoke<RetroJoinResult>('JoinBoard', shortCode, userId, displayName, role, password),
    );

    if (result.status === 'Ok' && result.board) {
      this._closed.set(false);
      this.lastUserId = userId;
      this.lastJoin = { shortCode, displayName, role };
      this._board.set(result.board);
    }

    return result;
  }

  async leaveBoard(shortCode: string, userId: string): Promise<void> {
    if (this.isConnected) {
      await this.connection!.invoke('LeaveBoard', shortCode, userId);
    }
    this.lastJoin = null; // intentional leave — don't auto-rejoin on a later reconnect
  }

  addCard(shortCode: string, userId: string, columnId: string, text: string) {
    return this.action('AddCard', shortCode, userId, columnId, text);
  }

  editCard(shortCode: string, userId: string, cardId: string, text: string) {
    return this.action('EditCard', shortCode, userId, cardId, text);
  }

  deleteCard(shortCode: string, userId: string, cardId: string) {
    return this.action('DeleteCard', shortCode, userId, cardId);
  }

  moveCard(
    shortCode: string,
    userId: string,
    cardId: string,
    targetColumnId: string,
    targetOrder: number,
  ) {
    return this.action('MoveCard', shortCode, userId, cardId, targetColumnId, targetOrder);
  }

  setTemplate(
    shortCode: string,
    userId: string,
    template: RetroTemplate,
    customColumns: string | null,
  ) {
    return this.action('SetTemplate', shortCode, userId, template, customColumns);
  }

  setAnonymous(shortCode: string, userId: string, anonymous: boolean) {
    return this.action('SetAnonymous', shortCode, userId, anonymous);
  }

  advancePhase(shortCode: string, userId: string, seconds: number | null) {
    return this.action('AdvancePhase', shortCode, userId, seconds);
  }

  previousPhase(shortCode: string, userId: string) {
    return this.action('PreviousPhase', shortCode, userId);
  }

  setPhaseDuration(shortCode: string, userId: string, seconds: number | null) {
    return this.action('SetPhaseDuration', shortCode, userId, seconds);
  }

  groupCards(shortCode: string, userId: string, cardIds: string[], targetGroupId: string | null) {
    return this.action('GroupCards', shortCode, userId, cardIds, targetGroupId);
  }

  ungroupCard(shortCode: string, userId: string, cardId: string) {
    return this.action('UngroupCard', shortCode, userId, cardId);
  }

  renameGroup(shortCode: string, userId: string, groupId: string, label: string) {
    return this.action('RenameGroup', shortCode, userId, groupId, label);
  }

  setAllowParticipantGrouping(shortCode: string, userId: string, allowed: boolean) {
    return this.action('SetAllowParticipantGrouping', shortCode, userId, allowed);
  }

  castVote(shortCode: string, userId: string, kind: RetroVoteTarget, targetId: string) {
    return this.action('CastRetroVote', shortCode, userId, kind, targetId);
  }

  withdrawVote(shortCode: string, userId: string, kind: RetroVoteTarget, targetId: string) {
    return this.action('WithdrawVote', shortCode, userId, kind, targetId);
  }

  setVoteBudget(shortCode: string, userId: string, budget: number, allowMultiplePerItem: boolean) {
    return this.action('SetVoteBudget', shortCode, userId, budget, allowMultiplePerItem);
  }

  addAction(
    shortCode: string,
    userId: string,
    title: string,
    ownerUserId: string | null,
    ownerName: string | null,
    dueDate: string | null,
    sourceGroupId: string | null,
  ) {
    return this.action(
      'AddAction', shortCode, userId, title, ownerUserId, ownerName, dueDate, sourceGroupId);
  }

  editAction(
    shortCode: string,
    userId: string,
    actionId: string,
    title: string,
    ownerUserId: string | null,
    ownerName: string | null,
    dueDate: string | null,
  ) {
    return this.action(
      'EditAction', shortCode, userId, actionId, title, ownerUserId, ownerName, dueDate);
  }

  toggleActionDone(shortCode: string, userId: string, actionId: string) {
    return this.action('ToggleActionDone', shortCode, userId, actionId);
  }

  deleteAction(shortCode: string, userId: string, actionId: string) {
    return this.action('DeleteAction', shortCode, userId, actionId);
  }

  closeBoard(shortCode: string, userId: string) {
    return this.action('CloseBoard', shortCode, userId);
  }

  deleteBoard(shortCode: string, userId: string) {
    return this.action('DeleteBoard', shortCode, userId);
  }

  setPassword(shortCode: string, userId: string, password: string | null) {
    return this.action('SetPassword', shortCode, userId, password);
  }

  setReactionsEnabled(shortCode: string, userId: string, enabled: boolean) {
    return this.action('SetReactionsEnabled', shortCode, userId, enabled);
  }

  setAllowRoleChange(shortCode: string, userId: string, enabled: boolean) {
    return this.action('SetAllowRoleChange', shortCode, userId, enabled);
  }

  changeRole(shortCode: string, userId: string, targetUserId: string, role: ParticipantRole) {
    return this.action('ChangeRole', shortCode, userId, targetUserId, role);
  }

  promoteToOrganiser(shortCode: string, userId: string, targetUserId: string) {
    return this.action('PromoteToOrganiser', shortCode, userId, targetUserId);
  }

  demoteOrganiser(shortCode: string, userId: string, targetUserId: string) {
    return this.action('DemoteOrganiser', shortCode, userId, targetUserId);
  }

  transferOrganiser(shortCode: string, userId: string, targetUserId: string) {
    return this.action('TransferOrganiser', shortCode, userId, targetUserId);
  }

  /** Invokes a mutation; the authoritative new board also arrives via the BoardUpdated push. */
  private async action(method: string, ...args: unknown[]): Promise<RetroActionResult> {
    const result = this.mapResult(await this.invoke<RetroActionResult>(method, ...args));
    if (result.status === 'Ok' && result.board) {
      this._board.set(result.board);
    }
    return result;
  }

  /** Results arrive with the wire board; map them so callers see one shape (#19). */
  private mapResult<T extends { status: string; board: RetroBoardSnapshot | null }>(raw: T): T {
    const wire = raw as unknown as { board: RetroBoardSnapshotWire | null };
    return wire.board ? { ...raw, board: flattenBoard(wire.board) } : raw;
  }

  private async invoke<T>(method: string, ...args: unknown[]): Promise<T> {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) {
      await this.connect();
    }
    return this.connection!.invoke<T>(method, ...args);
  }
}
