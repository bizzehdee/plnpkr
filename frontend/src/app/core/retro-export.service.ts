import { Injectable } from '@angular/core';
import { ExportStatus, exportStatusOf, postExport, saveAsFile } from './export-transport';
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
 * A whole retro board in one payload (#28): the same shape the Markdown and CSV renderers work
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

export interface RetroExportResult {
  status: ExportStatus;
  export: RetroExport | null;
}

const CONTENT_TYPES: Record<RetroExportFormat, string> = {
  md: 'text/markdown',
  csv: 'text/csv',
  json: 'application/json',
};

/**
 * The retro export's client side (#28). The POST-and-object-URL mechanics it shares with the poker
 * export (#30) live in `export-transport.ts`; what is retro-specific is the payload shape, the three
 * formats, and the phase refusal.
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

    return { status: exportStatusOf(response), export: null };
  }

  /**
   * Downloads the board as a file. Returns the refusal so the caller can ask for a password or
   * explain the phase guard, rather than leaving the user with a button that did nothing.
   */
  async download(
    shortCode: string,
    format: RetroExportFormat,
    password?: string | null,
  ): Promise<ExportStatus> {
    const response = await this.post(shortCode, format, password);
    if (!response) {
      return 'Failed';
    }

    if (!response.ok) {
      return exportStatusOf(response);
    }

    saveAsFile(await response.text(), CONTENT_TYPES[format], `${shortCode}-retro.${format}`);
    return 'Ok';
  }

  private post(
    shortCode: string,
    format: RetroExportFormat,
    password?: string | null,
  ): Promise<Response | null> {
    return postExport(`/api/retro/${encodeURIComponent(shortCode)}/export`, {
      format,
      password: password || null,
    });
  }
}
