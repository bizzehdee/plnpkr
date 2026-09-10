import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { I18nService } from '../../../core/i18n.service';
import { TranslatePipe } from '../../../core/translate.pipe';
import { ExportStatus } from '../../../core/export-transport';
import {
  RetroExport,
  RetroExportFormat,
  RetroExportService,
} from '../../../core/retro-export.service';

/**
 * The read-only post-retro summary (#28): the board's outcome as a page, at a URL that can be
 * pasted into a chat thread.
 *
 * **Why this is not just the board in read-only mode.** Opening the board joins the room, which
 * puts a name in the participant list and a seat in the room's state. Someone reading last week's
 * outcome — a manager, a joiner, whoever the actions were assigned to — should not have to become
 * a participant to do it. So this page reads the export instead: no hub connection, no seat, no
 * identity sent, and exactly the same data the downloaded file carries, including the anonymity
 * guarantee (#22) that no author is named on an anonymous board.
 */
@Component({
  selector: 'app-retro-summary',
  imports: [FormsModule, RouterLink, TranslatePipe],
  templateUrl: './retro-summary.page.html',
})
export class RetroSummaryPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly exports = inject(RetroExportService);
  private readonly i18n = inject(I18nService);

  protected shortCode = '';
  protected readonly summary = signal<RetroExport | null>(null);
  protected readonly status = signal<ExportStatus | null>(null);
  protected readonly busy = signal(false);
  protected password = '';

  protected readonly formats: RetroExportFormat[] = ['md', 'csv', 'json'];
  protected readonly downloading = signal<RetroExportFormat | null>(null);

  async ngOnInit(): Promise<void> {
    this.shortCode = this.route.snapshot.paramMap.get('shortCode') ?? '';
    await this.load();
  }

  /** Loads (or reloads, after a password) the board. */
  protected async load(): Promise<void> {
    this.busy.set(true);
    try {
      const result = await this.exports.load(this.shortCode, this.password);
      this.status.set(result.status);
      this.summary.set(result.export);
    } finally {
      this.busy.set(false);
    }
  }

  protected async download(format: RetroExportFormat): Promise<void> {
    this.downloading.set(format);
    try {
      const status = await this.exports.download(this.shortCode, format, this.password);
      if (status !== 'Ok') {
        this.status.set(status);
        this.summary.set(null);
      }
    } finally {
      this.downloading.set(null);
    }
  }

  protected formatLabel(format: RetroExportFormat): string {
    return this.i18n.t(`retro.export.${format}`);
  }

  protected formatDue(iso: string | null): string {
    return iso ? this.i18n.formatDate(iso) : '';
  }

  protected dotLabel(dots: number): string {
    return this.i18n.t(dots === 1 ? 'retro.dot' : 'retro.dots');
  }

  /** The message for a refusal, or null while the board is loading or loaded. */
  protected readonly problem = computed<string | null>(() => {
    switch (this.status()) {
      case 'NotFound':
        return this.i18n.t('retro.summary.notFound');
      case 'NotYetVisible':
        return this.i18n.t('retro.summary.notYet');
      case 'Failed':
        return this.i18n.t('retro.export.errFailed');
      default:
        return null;
    }
  });
}
