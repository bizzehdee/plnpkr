// Mirrors the backend Core contracts (string enums via JsonStringEnumConverter).

export type DeckType =
  | 'Sequential'
  | 'Fibonacci'
  | 'ModifiedFibonacci'
  | 'TShirt'
  | 'PowersOfTwo'
  | 'Custom';

export type SessionState = 'Voting' | 'Revealed';
export type ParticipantRole = 'Voter' | 'Observer';

export interface ParticipantInfo {
  userId: string;
  displayName: string;
  isOrganiser: boolean;
  role: ParticipantRole;
  hasVoted: boolean;
  changedAfterReveal: boolean;
  vote: string | null;
  isConnected: boolean;
  isOutlier: boolean;
}

export interface VoteCount {
  value: string;
  count: number;
}

export interface VoteStats {
  average: number | null;
  consensus: boolean;
  voteCount: number;
  distribution: VoteCount[];
  min: number | null;
  max: number | null;
  stdDev: number | null;
  outlierValues: string[];
}

export type IntegrationProvider = 'Jira' | 'AzureDevOps';

/** An ephemeral emoji reaction broadcast to the session (#17). Never persisted. */
export interface ReactionEvent {
  userId: string;
  emoji: string;
}

/** Emoji the UI offers / the server allows for reactions (mirrors backend `ReactionPolicy`). */
export const REACTION_EMOJI = ['👍', '👎', '🎉', '😂', '🤔', '❤️', '🚀', '👀'];

export interface LinkedIssueInfo {
  key: string;
  title: string;
  description: string | null;
  url: string;
  storyPoints: number | null;
  storyPointsFieldAvailable: boolean;
}

export interface QueuedTicketInfo {
  key: string;
  title: string;
  status: string | null;
  storyPoints: number | null;
  url: string;
  isSelected: boolean;
}

export interface IntegrationInfo {
  provider: IntegrationProvider;
  linkedIssue: LinkedIssueInfo | null;
  queue: QueuedTicketInfo[];
}

export interface SessionSnapshot {
  id: string;
  shortCode: string;
  name: string;
  deckType: DeckType;
  cards: string[];
  state: SessionState;
  organiserUserId: string | null;
  autoReveal: boolean;
  reactionsEnabled: boolean;
  allowRoleChange: boolean;
  isClosed: boolean;
  currentStory: string | null;
  participants: ParticipantInfo[];
  stats: VoteStats | null;
  integration: IntegrationInfo | null;
  // Round timer (#14). duration = configured length; deadline (ISO UTC) set while running (tick
  // locally against it); pausedRemainingSeconds set while paused. All null ⇒ idle/no timer.
  timerDurationSeconds: number | null;
  timerDeadline: string | null;
  timerPausedRemainingSeconds: number | null;
}

export type IntegrationStatus =
  | 'Ok'
  | 'Disabled'
  | 'SessionNotFound'
  | 'NotParticipant'
  | 'NotOrganiser'
  | 'NotConnected'
  | 'AuthFailed'
  | 'IssueNotFound'
  | 'ProviderError';

export interface IntegrationResult {
  status: IntegrationStatus;
  session: SessionSnapshot | null;
  accountName: string | null;
  error: string | null;
}

/** Remembered tracker connection in the organiser's localStorage (#45). */
export interface SavedTrackerConnection {
  provider: IntegrationProvider;
  baseUrl: string;
  email: string | null;
  token: string;
  storyPointsField?: string | null;
}

export type SessionActionStatus =
  | 'Ok'
  | 'SessionNotFound'
  | 'NotParticipant'
  | 'NotOrganiser'
  | 'ObserverCannotVote'
  | 'InvalidCard'
  | 'TargetNotFound'
  | 'InvalidDeck'
  | 'RoleChangeDisabled'
  | 'SessionClosed';

/** A named custom deck remembered in the organiser's browser (#11). `cards` is comma-separated. */
export interface SavedDeck {
  name: string;
  cards: string;
}

export interface SessionActionResult {
  status: SessionActionStatus;
  session: SessionSnapshot | null;
}

export type CreateSessionStatus = 'Ok' | 'InvalidName' | 'InvalidDeck';
export interface CreateSessionResult {
  status: CreateSessionStatus;
  session: SessionSnapshot | null;
  error: string | null;
}

export type JoinStatus =
  | 'Ok'
  | 'SessionNotFound'
  | 'InvalidName'
  | 'NameTaken'
  | 'PasswordRequired'
  | 'WrongPassword'
  | 'SessionClosed';

/** Lean /join landing info from GET /api/sessions/{shortCode} (#9). */
export interface SessionLanding {
  name: string;
  shortCode: string;
  requiresPassword: boolean;
}
export interface JoinResult {
  status: JoinStatus;
  session: SessionSnapshot | null;
  participant: ParticipantInfo | null;
  error: string | null;
}

export const DECK_LABELS: Record<DeckType, string> = {
  Sequential: 'Sequential (0–10)',
  Fibonacci: 'Fibonacci',
  ModifiedFibonacci: 'Modified Fibonacci',
  TShirt: 'T-shirt sizes',
  PowersOfTwo: 'Powers of two',
  Custom: 'Custom…',
};
