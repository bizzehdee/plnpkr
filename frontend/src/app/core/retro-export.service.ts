import { Injectable } from '@angular/core';
import { resolveApiBase } from './app-config';
import { RetroPhase, RetroTemplate } from './models';

/** A card as the export carries it. `author` is null on an anonymous board — always (#22/#28). */
export interface RetroExportCard {
  column: string;
  text: string;
  author: string | null;
  dots: number;
}

export interface RetroExportTheme {
  label: string;
  dots: number;
  cards: RetroExportCard[];
}

export interface RetroExportAction {
  title: string;
  owner: string | null;
  dueDate: string | null;
  done: boolean;
  carriedOver: boolean;
}

/**
 * A whole retro board in one payload (#28) — the same shape the Markdown and CSV renderers work
 * from, which is what lets the summary view and the downloaded file agree.
 */
export interface RetroExport {
  shortCode: string;
  name: string;
  template: RetroTemplate;
  phase: RetroPhase;
  anonymous: boolean;
  carriedFromShortCode: string | null;
  themes: RetroExportTheme[];
  looseCards: RetroExportCard[];
  actions: RetroExportAction[];
}

export type RetroExportFormat = 'md' | 'csv' | 'json';

/** Mirrors `RetroExportStatus`, plus a transport failure the server never got to answer. */
export type RetroExportStatus = 'Ok' | 'BoardNotFound' | 'PasswordRequired' | 'NotYetVisible' | 'Failed';

export interface RetroExportResult {
  status: RetroExportStatus;
  export: RetroExport | null;
}

const CONTENT_TYPES: Record<RetroExportFormat, string> = {
  md: 'text/markdown',
  csv: 'text/csv',
  json: 'application/json',
};

/**
 * The retro export's client side (#28).
 *
 * **Why `fetch` and a blob rather than a link.** The endpoint is a POST, because a protected
 * board's password would otherwise sit in a query string and from there in server logs, proxy logs
 * and the browser's history. That rules out the plain `<a download>` the poker export (#12) uses,
 * so the file is fetched and handed to the browser as an object URL instead.
 */
@Injectable({ providedIn: 'root' })
export class RetroExportService {
  /** Fetches the board as structured data, for the read-only summary view. */
  async load(shortCode: string, password?: string | null): Promise<RetroExportResult> {
    const response = await this.post(shortCode, 'json', password);
    if (!response) {
      return { status: 'Failed', export: null };
    }

    if (response.ok) {
      return { status: 'Ok', export: (await response.json()) as RetroExport };
    }

    return { status: this.statusOf(response), export: null };
  }

  /**
   * Downloads the board as a file. Returns the refusal so the caller can ask for a password or
   * explain the phase guard, rather than leaving the user with a button that did nothing.
   */
  async download(
    shortCode: string,
    format: RetroExportFormat,
    password?: string | null,
  ): Promise<RetroExportStatus> {
    const response = await this.post(shortCode, format, password);
    if (!response) {
      return 'Failed';
    }

    if (!response.ok) {
      return this.statusOf(response);
    }

    const blob = new Blob([await response.text()], { type: CONTENT_TYPES[format] });
    const url = URL.createObjectURL(blob);
    try {
      const a = document.createElement('a');
      a.href = url;
      a.download = `${shortCode}-retro.${format}`;
      a.rel = 'noopener';
      document.body.appendChild(a);
      a.click();
      a.remove();
    } finally {
      // Freed on the next task rather than immediately: revoking it in the same tick can beat the
      // browser to starting the download.
      setTimeout(() => URL.revokeObjectURL(url), 0);
    }

    return 'Ok';
  }

  private async post(
    shortCode: string,
    format: RetroExportFormat,
    password?: string | null,
  ): Promise<Response | null> {
    try {
      return await fetch(`${resolveApiBase()}/api/retro/${encodeURIComponent(shortCode)}/export`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ format, password: password || null }),
      });
    } catch {
      return null;
    }
  }

  private statusOf(response: Response): RetroExportStatus {
    switch (response.status) {
      case 404:
        return 'BoardNotFound';
      case 403:
        return 'PasswordRequired';
      case 409:
        return 'NotYetVisible';
      default:
        return 'Failed';
    }
  }
}
