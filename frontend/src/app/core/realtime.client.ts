import { Injectable, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { Observable, Subject } from 'rxjs';
import { resolveHubUrl } from './app-config';
import {
  CreateSessionResult,
  DeckType,
  IntegrationProvider,
  IntegrationResult,
  JoinResult,
  ParticipantRole,
  ReactionEvent,
  SessionActionResult,
  SessionSnapshot,
} from './models';

export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected';

export interface PingResponse {
  reply: string;
  connectionId: string;
  serverTimeUtc: string;
}

/**
 * Abstraction over the SignalR transport so components/stores can be unit-tested against a fake.
 * Exposes connection lifecycle, session operations, and the live session snapshot.
 */
export interface IRealtimeClient {
  readonly status: () => ConnectionStatus;
  readonly session: () => SessionSnapshot | null;
  readonly closed: () => boolean;
  connect(): Promise<void>;
  disconnect(): Promise<void>;
  createSession(
    name: string,
    deckType: DeckType,
    customCards: string | null,
    userId: string,
    displayName: string,
    organise: boolean,
    password?: string | null,
    enableReactions?: boolean,
    timerDurationSeconds?: number | null,
  ): Promise<CreateSessionResult>;
  joinSession(
    shortCode: string,
    userId: string,
    displayName: string,
    role: ParticipantRole,
    password?: string | null,
  ): Promise<JoinResult>;
  leaveSession(shortCode: string, userId: string): Promise<void>;
  castVote(shortCode: string, userId: string, card: string): Promise<SessionActionResult>;
  revealVotes(shortCode: string, userId: string): Promise<SessionActionResult>;
  resetRound(shortCode: string, userId: string): Promise<SessionActionResult>;
  resetVote(shortCode: string, userId: string, targetUserId: string): Promise<SessionActionResult>;
  setAutoReveal(shortCode: string, userId: string, enabled: boolean): Promise<SessionActionResult>;
  setStory(shortCode: string, userId: string, title: string | null): Promise<SessionActionResult>;
  setDeck(shortCode: string, userId: string, deckType: DeckType, customCards: string | null): Promise<SessionActionResult>;
  // Session lifecycle (#26), organiser-only.
  closeSession(shortCode: string, userId: string): Promise<SessionActionResult>;
  deleteSession(shortCode: string, userId: string): Promise<SessionActionResult>;
  setPassword(shortCode: string, userId: string, password: string | null): Promise<SessionActionResult>;
  setReactionsEnabled(shortCode: string, userId: string, enabled: boolean): Promise<SessionActionResult>;
  // Mid-session role changes (#21).
  setAllowRoleChange(shortCode: string, userId: string, enabled: boolean): Promise<SessionActionResult>;
  changeRole(shortCode: string, userId: string, targetUserId: string, role: ParticipantRole): Promise<SessionActionResult>;
  // Round timer (#14), organiser-controlled.
  setTimerDuration(shortCode: string, userId: string, seconds: number | null): Promise<SessionActionResult>;
  startTimer(shortCode: string, userId: string, seconds: number | null): Promise<SessionActionResult>;
  pauseTimer(shortCode: string, userId: string): Promise<SessionActionResult>;
  resumeTimer(shortCode: string, userId: string): Promise<SessionActionResult>;
  stopTimer(shortCode: string, userId: string): Promise<SessionActionResult>;
  connectTracker(
    shortCode: string,
    userId: string,
    provider: IntegrationProvider,
    baseUrl: string,
    email: string | null,
    token: string,
    storyPointsField?: string | null,
  ): Promise<IntegrationResult>;
  disconnectTracker(shortCode: string, userId: string): Promise<IntegrationResult>;
  linkIssue(shortCode: string, userId: string, issueKey: string): Promise<IntegrationResult>;
  submitStoryPoints(shortCode: string, userId: string, points: number): Promise<IntegrationResult>;
  loadQueueFromUrl(shortCode: string, userId: string, url: string): Promise<IntegrationResult>;
  loadQueueFromKeys(shortCode: string, userId: string, keys: string[]): Promise<IntegrationResult>;
  selectQueueItem(shortCode: string, userId: string, issueKey: string): Promise<IntegrationResult>;
  clearQueue(shortCode: string, userId: string): Promise<IntegrationResult>;
  /** Ephemeral emoji reaction (#17); fire-and-forget. */
  react(emoji: string): Promise<void>;
  /** Stream of reactions received from the session group (transient; never persisted). */
  readonly reactions$: Observable<ReactionEvent>;
}

interface LastJoin {
  shortCode: string;
  displayName: string;
  role: ParticipantRole;
}

@Injectable({ providedIn: 'root' })
export class SignalrRealtimeClient implements IRealtimeClient {
  private connection: HubConnection | null = null;
  private readonly _status = signal<ConnectionStatus>('disconnected');
  private readonly _session = signal<SessionSnapshot | null>(null);
  private readonly _closed = signal(false);
  /** Remembers how to re-join after a transient reconnect (new connection => lost group). */
  private lastJoin: LastJoin | null = null;
  private lastUserId = '';

  private readonly _reactions = new Subject<ReactionEvent>();

  readonly status = this._status.asReadonly();
  readonly session = this._session.asReadonly();
  /** Set when the server reports the current session has ended (idle eviction). */
  readonly closed = this._closed.asReadonly();
  readonly reactions$ = this._reactions.asObservable();

  async connect(): Promise<void> {
    if (this.connection && this.connection.state === HubConnectionState.Connected) {
      return;
    }

    this._status.set('connecting');
    this.connection = new HubConnectionBuilder()
      .withUrl(resolveHubUrl())
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.onreconnecting(() => this._status.set('connecting'));
    this.connection.onreconnected(async () => {
      this._status.set('connected');
      // A reconnect is a brand-new connection with no group membership, and the server marked us
      // away — re-join to reclaim our seat and resume receiving broadcasts. See #34.
      if (this.lastJoin) {
        const { shortCode, displayName, role } = this.lastJoin;
        await this.joinSession(shortCode, this.lastUserId, displayName, role).catch(() => {});
      }
    });
    this.connection.onclose(() => this._status.set('disconnected'));

    // The server pushes the full session snapshot whenever membership/state changes.
    this.connection.on('SessionUpdated', (snapshot: SessionSnapshot) => this._session.set(snapshot));
    this.connection.on('SessionClosed', () => {
      this._session.set(null);
      this._closed.set(true);
    });
    // Ephemeral emoji reactions (#17) — pushed to subscribers, never stored.
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

  async disconnect(): Promise<void> {
    await this.connection?.stop();
    this._status.set('disconnected');
  }

  async createSession(
    name: string,
    deckType: DeckType,
    customCards: string | null,
    userId: string,
    displayName: string,
    organise: boolean,
    password: string | null = null,
    enableReactions = true,
    timerDurationSeconds: number | null = null,
  ): Promise<CreateSessionResult> {
    const result = await this.invoke<CreateSessionResult>(
      'CreateSession',
      name,
      deckType,
      customCards,
      userId,
      displayName,
      organise,
      password,
      enableReactions,
      timerDurationSeconds,
    );
    if (result.status === 'Ok' && result.session) {
      this._closed.set(false);
      this.lastUserId = userId;
      this.lastJoin = { shortCode: result.session.shortCode, displayName, role: organise ? 'Observer' : 'Voter' };
      this._session.set(result.session);
    }
    return result;
  }

  async joinSession(
    shortCode: string,
    userId: string,
    displayName: string,
    role: ParticipantRole,
    password: string | null = null,
  ): Promise<JoinResult> {
    const result = await this.invoke<JoinResult>('JoinSession', shortCode, userId, displayName, role, password);
    if (result.status === 'Ok' && result.session) {
      this._closed.set(false);
      this.lastUserId = userId;
      this.lastJoin = { shortCode, displayName, role };
      this._session.set(result.session);
    }
    return result;
  }

  async leaveSession(shortCode: string, userId: string): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) {
      await this.connection.invoke('LeaveSession', shortCode, userId);
    }
    this.lastJoin = null; // intentional leave — don't auto-rejoin on a later reconnect
    this._session.set(null);
  }

  castVote(shortCode: string, userId: string, card: string): Promise<SessionActionResult> {
    return this.action('CastVote', shortCode, userId, card);
  }

  revealVotes(shortCode: string, userId: string): Promise<SessionActionResult> {
    return this.action('RevealVotes', shortCode, userId);
  }

  resetRound(shortCode: string, userId: string): Promise<SessionActionResult> {
    return this.action('ResetRound', shortCode, userId);
  }

  resetVote(shortCode: string, userId: string, targetUserId: string): Promise<SessionActionResult> {
    return this.action('ResetVote', shortCode, userId, targetUserId);
  }

  setAutoReveal(shortCode: string, userId: string, enabled: boolean): Promise<SessionActionResult> {
    return this.action('SetAutoReveal', shortCode, userId, enabled);
  }

  setStory(shortCode: string, userId: string, title: string | null): Promise<SessionActionResult> {
    return this.action('SetStory', shortCode, userId, title);
  }

  setPassword(shortCode: string, userId: string, password: string | null): Promise<SessionActionResult> {
    return this.action('SetPassword', shortCode, userId, password);
  }

  setDeck(shortCode: string, userId: string, deckType: DeckType, customCards: string | null): Promise<SessionActionResult> {
    return this.action('SetDeck', shortCode, userId, deckType, customCards);
  }

  closeSession(shortCode: string, userId: string): Promise<SessionActionResult> {
    return this.action('CloseSession', shortCode, userId);
  }

  deleteSession(shortCode: string, userId: string): Promise<SessionActionResult> {
    // On success the server broadcasts SessionClosed (handled in connect()); no snapshot to apply.
    return this.action('DeleteSession', shortCode, userId);
  }

  setReactionsEnabled(shortCode: string, userId: string, enabled: boolean): Promise<SessionActionResult> {
    return this.action('SetReactionsEnabled', shortCode, userId, enabled);
  }

  setAllowRoleChange(shortCode: string, userId: string, enabled: boolean): Promise<SessionActionResult> {
    return this.action('SetAllowRoleChange', shortCode, userId, enabled);
  }

  changeRole(shortCode: string, userId: string, targetUserId: string, role: ParticipantRole): Promise<SessionActionResult> {
    return this.action('ChangeRole', shortCode, userId, targetUserId, role);
  }

  setTimerDuration(shortCode: string, userId: string, seconds: number | null): Promise<SessionActionResult> {
    return this.action('SetTimerDuration', shortCode, userId, seconds);
  }

  startTimer(shortCode: string, userId: string, seconds: number | null): Promise<SessionActionResult> {
    return this.action('StartTimer', shortCode, userId, seconds);
  }

  pauseTimer(shortCode: string, userId: string): Promise<SessionActionResult> {
    return this.action('PauseTimer', shortCode, userId);
  }

  resumeTimer(shortCode: string, userId: string): Promise<SessionActionResult> {
    return this.action('ResumeTimer', shortCode, userId);
  }

  stopTimer(shortCode: string, userId: string): Promise<SessionActionResult> {
    return this.action('StopTimer', shortCode, userId);
  }

  connectTracker(
    shortCode: string,
    userId: string,
    provider: IntegrationProvider,
    baseUrl: string,
    email: string | null,
    token: string,
    storyPointsField: string | null = null,
  ): Promise<IntegrationResult> {
    return this.integrationAction('ConnectTracker', shortCode, userId, provider, baseUrl, email, token, storyPointsField);
  }

  disconnectTracker(shortCode: string, userId: string): Promise<IntegrationResult> {
    return this.integrationAction('DisconnectTracker', shortCode, userId);
  }

  linkIssue(shortCode: string, userId: string, issueKey: string): Promise<IntegrationResult> {
    return this.integrationAction('LinkIssue', shortCode, userId, issueKey);
  }

  submitStoryPoints(shortCode: string, userId: string, points: number): Promise<IntegrationResult> {
    return this.integrationAction('SubmitStoryPoints', shortCode, userId, points);
  }

  loadQueueFromUrl(shortCode: string, userId: string, url: string): Promise<IntegrationResult> {
    return this.integrationAction('LoadQueueFromUrl', shortCode, userId, url);
  }

  loadQueueFromKeys(shortCode: string, userId: string, keys: string[]): Promise<IntegrationResult> {
    return this.integrationAction('LoadQueueFromKeys', shortCode, userId, keys);
  }

  selectQueueItem(shortCode: string, userId: string, issueKey: string): Promise<IntegrationResult> {
    return this.integrationAction('SelectQueueItem', shortCode, userId, issueKey);
  }

  clearQueue(shortCode: string, userId: string): Promise<IntegrationResult> {
    return this.integrationAction('ClearQueue', shortCode, userId);
  }

  async react(emoji: string): Promise<void> {
    // Fire-and-forget; the server derives session/user from the connection. Ignore transport errors.
    if (this.connection?.state === HubConnectionState.Connected) {
      await this.connection.invoke('React', emoji).catch(() => {});
    }
  }

  private async integrationAction(method: string, ...args: unknown[]): Promise<IntegrationResult> {
    const result = await this.invoke<IntegrationResult>(method, ...args);
    if (result.status === 'Ok' && result.session) {
      this._session.set(result.session);
    }
    return result;
  }

  /** Invokes a mutation; the authoritative new state arrives via the SessionUpdated broadcast. */
  private async action(method: string, ...args: unknown[]): Promise<SessionActionResult> {
    const result = await this.invoke<SessionActionResult>(method, ...args);
    if (result.status === 'Ok' && result.session) {
      this._session.set(result.session);
    }
    return result;
  }

  private async invoke<T>(method: string, ...args: unknown[]): Promise<T> {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) {
      await this.connect();
    }
    return this.connection!.invoke<T>(method, ...args);
  }
}
