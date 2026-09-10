import { flattenSession } from './room.client';
import { RoomSnapshot, SessionSnapshotWire } from './models';

/**
 * The wire→view mapping introduced by #19. The server sends room-level state once, in an embedded
 * fragment; the components read it flat. If this mapping drops a field, the session page silently
 * loses it, so every room-level field is asserted here.
 */
describe('flattenSession', () => {
  const room: RoomSnapshot = {
    id: 'room-1',
    shortCode: 'blue-fox-42',
    name: 'Sprint 24',
    tool: 'Poker',
    organiserUserId: 'alice',
    reactionsEnabled: false,
    allowRoleChange: false,
    isClosed: true,
    hasPassword: true,
    participants: [
      {
        userId: 'alice',
        displayName: 'Alice',
        isOrganiser: true,
        role: 'Observer',
        hasVoted: false,
        changedAfterReveal: false,
        vote: null,
        isConnected: true,
        isOutlier: false,
      },
    ],
  };

  const wire: SessionSnapshotWire = {
    room,
    deckType: 'Fibonacci',
    cards: ['1', '2', '3'],
    state: 'Revealed',
    autoReveal: true,
    currentStory: 'PROJ-7',
    currentStoryNote: 'Bigger than it looks',
    stats: null,
    integration: null,
    timerDurationSeconds: 300,
    timerDeadline: null,
    timerPausedRemainingSeconds: 42,
  };

  it('lifts every room-level field to the top', () => {
    const flat = flattenSession(wire);

    expect(flat.id).toBe('room-1');
    expect(flat.shortCode).toBe('blue-fox-42');
    expect(flat.name).toBe('Sprint 24');
    expect(flat.organiserUserId).toBe('alice');
    expect(flat.reactionsEnabled).toBe(false);
    expect(flat.allowRoleChange).toBe(false);
    expect(flat.isClosed).toBe(true);
    expect(flat.participants).toHaveLength(1);
  });

  it('keeps the room fragment for fields with no flat alias', () => {
    const flat = flattenSession(wire);

    expect(flat.room.tool).toBe('Poker');
    expect(flat.room.hasPassword).toBe(true);
  });

  it('passes the tool state through unchanged', () => {
    const flat = flattenSession(wire);

    expect(flat.deckType).toBe('Fibonacci');
    expect(flat.cards).toEqual(['1', '2', '3']);
    expect(flat.state).toBe('Revealed');
    expect(flat.autoReveal).toBe(true);
    expect(flat.currentStory).toBe('PROJ-7');
    expect(flat.currentStoryNote).toBe('Bigger than it looks');
    expect(flat.timerDurationSeconds).toBe(300);
    expect(flat.timerPausedRemainingSeconds).toBe(42);
  });
});
