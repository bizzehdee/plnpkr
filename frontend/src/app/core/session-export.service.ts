import { Injectable } from '@angular/core';
import { ExportStatus, exportStatusOf, postExport, saveAsFile } from './export-transport';
import { SessionAnalytics } from './models';

export type SessionExportFormat = 'csv' | 'json';

export interface SessionAnalyticsResult {
  status: ExportStatus;
  analytics: SessionAnalytics | null;
}

const CONTENT_TYPES: Record<SessionExportFormat, string> = {
  csv: 'text/csv',
  json: 'application/json',
};

/**
 * The poker round-history reads (#11/#12), password-guarded and POSTed since #30.
 *
 * Both calls used to be plain GETs — the analytics modal through `HttpClient`, the download through
 * an `<a download>` link. Neither could carry a password without putting it in the URL, which is
 * exactly what the retro export refused to do (#28), so they moved to the same transport.
 */
@Injectable({ providedIn: 'root' })
export class SessionExportService {
  /** The velocity/throughput summary for the analytics modal. */
  async loadAnalytics(shortCode: string, password?: string | null): Promise<SessionAnalyticsResult> {
    const response = await postExport(`/api/sessions/${encodeURIComponent(shortCode)}/analytics`, {
      password: password || null,
    });
    if (!response) {
      return { status: 'Failed', analytics: null };
    }

    if (response.ok) {
      return { status: 'Ok', analytics: (await response.json()) as SessionAnalytics };
    }

    return { status: exportStatusOf(response), analytics: null };
  }

  /**
   * Downloads the completed-round history. Returns the refusal so the caller can ask for the
   * password rather than leaving the user with a button that did nothing.
   */
  async download(
    shortCode: string,
    format: SessionExportFormat,
    password?: string | null,
  ): Promise<ExportStatus> {
    const response = await postExport(`/api/sessions/${encodeURIComponent(shortCode)}/export`, {
      format,
      password: password || null,
    });
    if (!response) {
      return 'Failed';
    }

    if (!response.ok) {
      return exportStatusOf(response);
    }

    saveAsFile(await response.text(), CONTENT_TYPES[format], `${shortCode}-rounds.${format}`);
    return 'Ok';
  }
}
