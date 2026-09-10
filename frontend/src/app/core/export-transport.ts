import { resolveApiBase } from './app-config';

/**
 * Why an export was refused, or Ok. `Failed` is the client's own addition: a request the server
 * never answered at all.
 *
 * `NotYetVisible` only arises for the retro, whose export waits for the discussion (#28); poker has
 * no equivalent phase.
 */
export type ExportStatus = 'Ok' | 'NotFound' | 'PasswordRequired' | 'NotYetVisible' | 'Failed';

/**
 * The transport both tools' exports share (#28/#30).
 *
 * **Why `fetch` and a blob rather than an `<a download>` link.** Both export routes are POSTs,
 * because a protected room's password would otherwise sit in a query string and from there in server
 * logs, proxy logs and the browser's history. A plain link can only issue a GET, so the file is
 * fetched and handed to the browser as an object URL instead. That dance is identical for a retro
 * board and a poker round history, and the object-URL lifetime is easy to get subtly wrong, so it
 * lives here once rather than in each tool's service.
 */
export async function postExport(path: string, body: unknown): Promise<Response | null> {
  try {
    return await fetch(`${resolveApiBase()}${path}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
  } catch {
    // A network failure, a blocked request, an offline tab — the caller reports 'Failed'.
    return null;
  }
}

/** Maps the refusals both export endpoints share onto one status. */
export function exportStatusOf(response: Response): ExportStatus {
  switch (response.status) {
    case 404:
      return 'NotFound';
    case 403:
      return 'PasswordRequired';
    case 409:
      return 'NotYetVisible';
    default:
      return 'Failed';
  }
}

/** Hands a fetched body to the browser as a download. */
export function saveAsFile(content: string, contentType: string, fileName: string): void {
  const url = URL.createObjectURL(new Blob([content], { type: contentType }));
  try {
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    a.rel = 'noopener';
    document.body.appendChild(a);
    a.click();
    a.remove();
  } finally {
    // Freed on the next task rather than immediately: revoking it in the same tick can beat the
    // browser to starting the download.
    setTimeout(() => URL.revokeObjectURL(url), 0);
  }
}
