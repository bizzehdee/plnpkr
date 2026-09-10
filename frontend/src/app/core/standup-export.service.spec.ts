import { StandupExportService } from './standup-export.service';
import { StandupBoardSnapshot } from './models';

const Q1 = 'q-1';
const Q2 = 'q-2';

function board(over: Partial<StandupBoardSnapshot> = {}): StandupBoardSnapshot {
  return {
    room: {} as StandupBoardSnapshot['room'],
    id: 'room-1',
    shortCode: 'blue-fox-42',
    name: 'Monday standup',
    organiserUserId: 'me',
    reactionsEnabled: true,
    allowRoleChange: true,
    isClosed: false,
    participants: [],
    questions: [
      { id: Q1, text: 'What did you do?', order: 0 },
      { id: Q2, text: 'Anything in your way?', order: 1 },
    ],
    iHavePosted: true,
    postedCount: 1,
    participantCount: 3,
    people: [
      {
        userId: 'me',
        displayName: 'Alice',
        isMe: true,
        answers: [{ questionId: Q1, text: 'Shipped the export', updatedAt: null }],
        postedAt: '2026-01-01T09:00:00Z',
      },
    ],
    blockers: [],
    previousBoardShortCode: null,
    ...over,
  };
}

describe('StandupExportService', () => {
  const sut = new StandupExportService();

  it('writes the standup as Markdown, a heading per person', () => {
    const md = sut.toMarkdown(board());

    expect(md).toContain('# Monday standup');
    expect(md).toContain('## Alice');
    expect(md).toContain('**What did you do?**');
    expect(md).toContain('Shipped the export');
  });

  it('leaves out questions a person did not answer', () => {
    // A blank heading with nothing under it reads as an answer that went missing.
    expect(sut.toMarkdown(board())).not.toContain('Anything in your way?');
  });

  it('carries the posted count and the provenance', () => {
    const md = sut.toMarkdown(board({ previousBoardShortCode: 'friday-standup' }));

    expect(md).toContain('1 of 3 posted.');
    expect(md).toContain('friday-standup');
  });

  it('lists blockers as a checklist, with the owner', () => {
    const md = sut.toMarkdown(board({
      blockers: [
        {
          id: 'b1', text: 'Waiting on the platform team', authorUserId: 'me',
          authorDisplayName: 'Alice', ownerUserId: null, ownerName: 'Dana',
          isResolved: false, carriedOver: false, createdAt: '2026-01-01T09:00:00Z',
        },
        {
          id: 'b2', text: 'Already sorted', authorUserId: 'me',
          authorDisplayName: 'Alice', ownerUserId: null, ownerName: null,
          isResolved: true, carriedOver: false, createdAt: '2026-01-01T09:00:00Z',
        },
      ],
    }));

    expect(md).toContain('- [ ] Waiting on the platform team — Dana');
    expect(md).toContain('- [x] Already sorted');
  });

  it('writes a row per answer as CSV', () => {
    const csv = sut.toCsv(board());

    expect(csv.split('\n')[0]).toBe('person,question,answer');
    expect(csv).toContain('Alice,What did you do?,Shipped the export');
  });

  it('quotes a field that would otherwise break the row', () => {
    // The same rule as the backend's shared `Csv.Field` (#34), which the other two exports use.
    const csv = sut.toCsv(board({
      people: [{
        userId: 'me',
        displayName: 'Alice',
        isMe: true,
        answers: [{ questionId: Q1, text: 'Shipped it, then said "done"', updatedAt: null }],
        postedAt: null,
      }],
    }));

    expect(csv).toContain('"Shipped it, then said ""done"""');
  });

  it('exports only what the snapshot holds, which is only what the viewer may read', () => {
    // Post-to-read is enforced in the projection (#36), so an un-posted viewer's export is their
    // own answers and nothing else — no second place for the rule to be forgotten.
    const csv = sut.toCsv(board({ iHavePosted: false }));

    expect(csv).toContain('Alice');
    expect(csv).not.toContain('Bob');
  });
});
