// Mirrors the backend Core contracts (string enums via JsonStringEnumConverter).

export type DeckType =
  | 'Sequential'
  | 'Fibonacci'
  | 'ModifiedFibonacci'
  | 'TShirt'
  | 'PowersOfTwo'
  | 'Custom';

export type SessionState = 'Voting' | 'Revealed' | 'Discussion';
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

export type IntegrationProvider = 'Jira' | 'AzureDevOps' | 'GitHub' | 'GitLab';

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

export type RoomTool = 'Poker' | 'Retro';

/**
 * The room-level half of every tool snapshot (#19): identity, the participant list with presence
 * and organiser flags, and the closed state. Both tools embed this exact shape, so a room-level
 * field is mirrored here once rather than once per tool.
 */
export interface RoomSnapshot {
  id: string;
  shortCode: string;
  name: string;
  tool: RoomTool;
  organiserUserId: string | null;
  reactionsEnabled: boolean;
  allowRoleChange: boolean;
  isClosed: boolean;
  hasPassword: boolean;
  participants: ParticipantInfo[];
}

/**
 * The poker snapshot exactly as it arrives on the wire (#19): tool state alongside the embedded
 * room fragment. `flattenSession` turns it into the flat {@link SessionSnapshot} the components
 * use, so the room-level fields are defined once here and the UI keeps one shape to read.
 */
export interface SessionSnapshotWire {
  room: RoomSnapshot;
  deckType: DeckType;
  cards: string[];
  state: SessionState;
  autoReveal: boolean;
  currentStory: string | null;
  currentStoryNote: string | null;
  stats: VoteStats | null;
  integration: IntegrationInfo | null;
  timerDurationSeconds: number | null;
  timerDeadline: string | null;
  timerPausedRemainingSeconds: number | null;
}

/** The flat view model the components read; `room` is kept for room-level fields with no alias. */
export interface SessionSnapshot {
  room: RoomSnapshot;
  id: string;
  shortCode: string;
  name: string;
  organiserUserId: string | null;
  reactionsEnabled: boolean;
  allowRoleChange: boolean;
  isClosed: boolean;
  participants: ParticipantInfo[];
  deckType: DeckType;
  cards: string[];
  state: SessionState;
  autoReveal: boolean;
  currentStory: string | null;
  currentStoryNote: string | null;
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
  | 'ProviderError'
  | 'SessionClosed';

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

export type CreateSessionStatus = 'Ok' | 'InvalidName' | 'InvalidDeck' | 'RateLimited';
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
  | 'SessionClosed'
  | 'SessionFull'
  | 'RateLimited';

/** Lean /join landing info from GET /api/sessions/{shortCode} (#9). */
export interface SessionLanding {
  name: string;
  shortCode: string;
  requiresPassword: boolean;
  /** Which tool the short code belongs to, so /join can route to the right page (#19). */
  tool: RoomTool;
}
export interface JoinResult {
  status: JoinStatus;
  session: SessionSnapshot | null;
  participant: ParticipantInfo | null;
  error: string | null;
}

/** One completed round in the session history (#11). */
export interface RoundResultInfo {
  story: string | null;
  note: string | null;
  finalEstimate: string | null;
  average: number | null;
  consensus: boolean;
  voteCount: number;
  recordedAt: string;
}

/** Velocity/throughput summary for a session (#11). */
export interface SessionAnalytics {
  shortCode: string;
  name: string;
  roundsCompleted: number;
  consensusRounds: number;
  consensusRate: number;
  averageVotesPerRound: number | null;
  rounds: RoundResultInfo[];
}

/** The session retention windows (#15), from GET /api/config — server-configured, not hard-coded. */
export interface RetentionConfig {
  closedRetentionMonths: number;
  softDeleteRetentionDays: number;
  idleRetentionDays: number;
}

/** Server-driven runtime config (#15). Small and growable — see `GET /api/config`. */
export interface AppConfig {
  retention: RetentionConfig;
}

/** i18n catalog key for each deck's display label (translated at render time — see `deck.*` keys). */
export const DECK_LABEL_KEYS: Record<DeckType, string> = {
  Sequential: 'deck.sequential',
  Fibonacci: 'deck.fibonacci',
  ModifiedFibonacci: 'deck.modifiedFibonacci',
  TShirt: 'deck.tshirt',
  PowersOfTwo: 'deck.powersOfTwo',
  Custom: 'deck.custom',
};

// --- Team Retro (#21) -------------------------------------------------------
// Mirrors TeamTools.Core.Contracts.RetroSnapshots.

export type RetroVoteTarget = 'Card' | 'Group';

/** One row of the ranked discussion agenda (#25). */
export interface RetroRankedItem {
  kind: RetroVoteTarget;
  id: string;
  label: string;
  dots: number;
}

export type RetroPhase = 'Collect' | 'Group' | 'Vote' | 'Discuss' | 'Actions' | 'Closed';

/** i18n catalog key for each phase's display label. */
export const RETRO_PHASE_LABEL_KEYS: Record<RetroPhase, string> = {
  Collect: 'retro.phase.collect',
  Group: 'retro.phase.group',
  Vote: 'retro.phase.vote',
  Discuss: 'retro.phase.discuss',
  Actions: 'retro.phase.actions',
  Closed: 'retro.phase.closed',
};

/** The phases in facilitation order, for the phase rail. */
export const RETRO_PHASE_ORDER: RetroPhase[] = [
  'Collect',
  'Group',
  'Vote',
  'Discuss',
  'Actions',
  'Closed',
];

export type RetroTemplate =
  | 'WentWellToImprove'
  | 'StartStopContinue'
  | 'FourLs'
  | 'MadSadGlad'
  | 'Custom';

/** i18n catalog key for each retro template's display label. */
export const RETRO_TEMPLATE_LABEL_KEYS: Record<RetroTemplate, string> = {
  WentWellToImprove: 'retro.template.wentWell',
  StartStopContinue: 'retro.template.startStopContinue',
  FourLs: 'retro.template.fourLs',
  MadSadGlad: 'retro.template.madSadGlad',
  Custom: 'retro.template.custom',
};

/**
 * A card as this viewer may see it. `authorUserId` is absent on an anonymous board (#22) — the
 * server omits it rather than the client hiding it — so `isMine` is what tells the UI whether the
 * edit and delete controls belong to this viewer.
 */
export interface RetroCardInfo {
  id: string;
  text: string;
  /** The theme this card belongs to, or null when it stands alone (#24). */
  groupId: string | null;
  authorUserId: string | null;
  authorDisplayName: string | null;
  isMine: boolean;
  order: number;
  createdAt: string;
  /** This viewer's own dots — always visible to them (#25). */
  myDots: number;
  /** Everyone's dots, or null while voting is still open (#25). */
  totalDots: number | null;
}

/** A theme and the cards gathered into it (#24). */
export interface RetroGroupInfo {
  id: string;
  label: string;
  order: number;
  cards: RetroCardInfo[];
  myDots: number;
  totalDots: number | null;
}

export interface RetroColumnInfo {
  id: string;
  title: string;
  order: number;
  cards: RetroCardInfo[];
  /**
   * Cards in this column that this viewer may not see. During Collect a participant sees only
   * their own, so the count is what tells them the team is writing without showing what (#23).
   */
  hiddenCardCount: number;
}

/** The retro board as it arrives on the wire: tool state plus the shared room fragment (#19). */
export interface RetroBoardSnapshotWire {
  room: RoomSnapshot;
  template: RetroTemplate;
  phase: RetroPhase;
  /** Null at either end of the phase order — nothing to advance to or step back from (#23). */
  nextPhase: RetroPhase | null;
  previousPhase: RetroPhase | null;
  phaseDurationSeconds: number | null;
  /** ISO UTC instant the running phase countdown expires; clients tick locally against it. */
  phaseDeadline: string | null;
  anonymous: boolean;
  /** False once the first card exists — anonymity is then locked (#22). */
  canChangeAnonymity: boolean;
  /** Whether any participant may group cards, or only a facilitator (#24). */
  allowParticipantGrouping: boolean;
  voteBudget: number;
  allowMultiplePerItem: boolean;
  myDotsRemaining: number;
  /** Whether dot totals are being sent at all — false while voting is open (#25). */
  voteTotalsVisible: boolean;
  columns: RetroColumnInfo[];
  groups: RetroGroupInfo[];
  /** The ranked discussion agenda; empty until totals are visible (#25). */
  ranking: RetroRankedItem[];
}

/** The flat view model the retro components read; `room` is kept for fields with no flat alias. */
export interface RetroBoardSnapshot {
  room: RoomSnapshot;
  id: string;
  shortCode: string;
  name: string;
  organiserUserId: string | null;
  reactionsEnabled: boolean;
  allowRoleChange: boolean;
  isClosed: boolean;
  participants: ParticipantInfo[];
  template: RetroTemplate;
  phase: RetroPhase;
  nextPhase: RetroPhase | null;
  previousPhase: RetroPhase | null;
  phaseDurationSeconds: number | null;
  phaseDeadline: string | null;
  anonymous: boolean;
  canChangeAnonymity: boolean;
  allowParticipantGrouping: boolean;
  voteBudget: number;
  allowMultiplePerItem: boolean;
  myDotsRemaining: number;
  /** Whether dot totals are being sent at all — false while voting is open (#25). */
  voteTotalsVisible: boolean;
  columns: RetroColumnInfo[];
  groups: RetroGroupInfo[];
  /** The ranked discussion agenda; empty until totals are visible (#25). */
  ranking: RetroRankedItem[];
}

export type RetroActionStatus =
  | 'Ok'
  | 'BoardNotFound'
  | 'NotParticipant'
  | 'NotOrganiser'
  | 'BoardClosed'
  | 'ColumnNotFound'
  | 'CardNotFound'
  | 'NotCardAuthor'
  | 'InvalidCardText'
  | 'AnonymityLocked'
  | 'WrongPhase'
  | 'IllegalPhaseTransition'
  | 'GroupNotFound'
  | 'InvalidGroupLabel'
  | 'OutOfDots'
  | 'AlreadyVotedForItem'
  | 'NoVoteToWithdraw'
  | 'InvalidTemplate'
  | 'RateLimited';

export interface RetroActionResult {
  status: RetroActionStatus;
  board: RetroBoardSnapshot | null;
}

export type CreateRetroStatus = 'Ok' | 'InvalidName' | 'InvalidTemplate' | 'RateLimited';

export interface CreateRetroResult {
  status: CreateRetroStatus;
  board: RetroBoardSnapshot | null;
  error: string | null;
}

export interface RetroJoinResult {
  status: JoinStatus;
  board: RetroBoardSnapshot | null;
  participant: ParticipantInfo | null;
  error: string | null;
}

/** Longest a card may be — mirrors RetroService.MaxCardLength so the UI can cap the input. */
export const RETRO_MAX_CARD_LENGTH = 500;
