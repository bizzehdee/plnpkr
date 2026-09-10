import { Injectable } from '@angular/core';
import { saveAsFile } from './export-transport';
import { StandupBoardSnapshot } from './models';

export type StandupExportFormat = 'md' | 'csv';

/**
 * The standup export (#36) — "export it or lose it", made real.
 *
 * A standup room is idle by construction between mornings, so the idle sweep (#15) will eventually
 * take it. The spec's answer was to say so in the UI rather than carve out a retention window for
 * one tool, and saying so is only honest if there is something to press.
 *
 * **Rendered from the snapshot the client already holds, not from a new endpoint.** The other two
 * exports are server-side POSTs because they read data the client was never sent: a poker round
 * history, or a retro board whose cards a viewer may only see after the discussion phase (#28/#30).
 * A standup snapshot is different — post-to-read has *already* removed everything this viewer may
 * not read, in the same projection every broadcast passes through. So the file is exactly what is on
 * screen, the projection stays the one enforcement point, and there is no second place a password
 * check could be forgotten.
 *
 * The download mechanics are the shared ones (`saveAsFile`), so the object-URL lifetime is handled
 * in one place for all three tools.
 */
@Injectable({ providedIn: 'root' })
export class StandupExportService {
  download(board: StandupBoardSnapshot, format: StandupExportFormat): void {
    const content = format === 'md' ? this.toMarkdown(board) : this.toCsv(board);
    saveAsFile(
      content,
      format === 'md' ? 'text/markdown' : 'text/csv',
      `standup-${board.shortCode}.${format}`,
    );
  }

  toMarkdown(board: StandupBoardSnapshot): string {
    const lines = [`# ${board.name}`, '', `Short code: ${board.shortCode}`, ''];

    if (board.previousBoardShortCode) {
      lines.push(`Carried forward from \`${board.previousBoardShortCode}\`.`, '');
    }

    lines.push(`${board.postedCount} of ${board.participantCount} posted.`, '');

    for (const person of board.people) {
      lines.push(`## ${person.displayName}`, '');
      for (const question of board.questions) {
        const answer = person.answers.find((a) => a.questionId === question.id);
        if (answer) {
          lines.push(`**${question.text}**`, '', answer.text, '');
        }
      }
    }

    if (board.blockers.length > 0) {
      lines.push('## Blockers', '');
      for (const blocker of board.blockers) {
        const owner = blocker.ownerName ? ` — ${blocker.ownerName}` : '';
        lines.push(`- [${blocker.isResolved ? 'x' : ' '}] ${blocker.text}${owner}`);
      }
      lines.push('');
    }

    return lines.join('\n');
  }

  toCsv(board: StandupBoardSnapshot): string {
    const rows = [['person', 'question', 'answer'].join(',')];

    for (const person of board.people) {
      for (const question of board.questions) {
        const answer = person.answers.find((a) => a.questionId === question.id);
        if (answer) {
          rows.push([person.displayName, question.text, answer.text].map(field).join(','));
        }
      }
    }

    for (const blocker of board.blockers) {
      rows.push([
        blocker.authorDisplayName,
        'Blocker',
        `${blocker.text}${blocker.ownerName ? ` (${blocker.ownerName})` : ''}${
          blocker.isResolved ? ' [resolved]' : ''
        }`,
      ].map(field).join(','));
    }

    return rows.join('\n');
  }
}

/** Quotes a CSV field the same way the backend's `Csv.Field` does (#34). */
function field(value: string): string {
  return /[",\n\r]/.test(value) ? `"${value.replace(/"/g, '""')}"` : value;
}
