import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { RetroSummaryPage } from './retro-summary.page';
import {
  RetroExport,
  RetroExportService,
  RetroExportStatus,
} from '../../../core/retro-export.service';

const CODE = 'blue-fox-42';

function summary(over: Partial<RetroExport> = {}): RetroExport {
  return {
    shortCode: CODE,
    name: 'Sprint 24 retro',
    template: 'MadSadGlad',
    phase: 'Discuss',
    anonymous: false,
    carriedFromShortCode: null,
    themes: [
      {
        label: 'CI is slow',
        dots: 4,
        cards: [{ column: 'Mad', text: 'CI is slow', author: 'Bob', dots: 3 }],
      },
    ],
    looseCards: [{ column: 'Glad', text: 'pairing helped', author: 'Ada', dots: 0 }],
    actions: [
      {
        title: 'Speed up CI',
        owner: 'Ada',
        dueDate: '2026-03-01T00:00:00Z',
        done: false,
        carriedOver: false,
      },
    ],
    ...over,
  };
}

class FakeExportService {
  load = vi.fn<(...a: unknown[]) => Promise<{ status: RetroExportStatus; export: RetroExport | null }>>();
  download = vi.fn<(...a: unknown[]) => Promise<RetroExportStatus>>().mockResolvedValue('Ok');

  constructor(status: RetroExportStatus = 'Ok', board: RetroExport | null = summary()) {
    this.load.mockResolvedValue({ status, export: status === 'Ok' ? board : null });
  }
}

type Cmp = {
  password: string;
  load(): Promise<void>;
  download(format: 'md' | 'csv' | 'json'): Promise<void>;
};

async function setup(exports: FakeExportService) {
  TestBed.configureTestingModule({
    imports: [RetroSummaryPage],
    providers: [
      provideRouter([]),
      { provide: RetroExportService, useValue: exports },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => CODE } } } },
    ],
  });
  const fixture = TestBed.createComponent(RetroSummaryPage);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture;
}

describe('RetroSummaryPage', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('reads the board without joining the room', async () => {
    // The point of the page: a reader gets the outcome without becoming a participant, so no
    // identity is sent and no seat is taken.
    const exports = new FakeExportService();
    await setup(exports);

    expect(exports.load).toHaveBeenCalledWith(CODE, '');
  });

  it('shows the themes highest-voted first, with their dots', async () => {
    const fixture = await setup(new FakeExportService());
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('Sprint 24 retro');
    expect(el.textContent).toContain('CI is slow');
    expect(el.textContent).toContain('4 dots');
  });

  it('shows the ungrouped cards and the action items', async () => {
    const fixture = await setup(new FakeExportService());
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('#summary-cards')).toBeTruthy();
    expect(el.textContent).toContain('pairing helped');
    expect(el.querySelector('#summary-actions')).toBeTruthy();
    expect(el.textContent).toContain('Speed up CI');
  });

  it('says so when no actions came out of the retro', async () => {
    const fixture = await setup(new FakeExportService('Ok', summary({ actions: [] })));

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'No action items were recorded',
    );
  });

  it('names nobody on an anonymous board', async () => {
    // The same guarantee the file carries (#22): the summary is a rendering of the export, not a
    // second path to the data with its own rules.
    const anonymous = summary({
      anonymous: true,
      themes: [
        {
          label: 'CI is slow',
          dots: 4,
          cards: [{ column: 'Mad', text: 'CI is slow', author: null, dots: 3 }],
        },
      ],
      looseCards: [{ column: 'Glad', text: 'pairing helped', author: null, dots: 0 }],
      actions: [],
    });
    const fixture = await setup(new FakeExportService('Ok', anonymous));
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).not.toContain('Bob');
    expect(el.textContent).not.toContain('Ada');
    expect(el.textContent).toContain('no card authors');
  });

  it('asks for the password when the board has one', async () => {
    const fixture = await setup(new FakeExportService('PasswordRequired', null));
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('#summary-password')).toBeTruthy();
    expect(el.textContent).not.toContain('Sprint 24 retro');
  });

  it('reloads with the password once it is given', async () => {
    const exports = new FakeExportService('PasswordRequired', null);
    const fixture = await setup(exports);
    const cmp = fixture.componentInstance as unknown as Cmp;

    exports.load.mockResolvedValue({ status: 'Ok', export: summary() });
    cmp.password = 'hunter2';
    await cmp.load();
    fixture.detectChanges();

    expect(exports.load).toHaveBeenLastCalledWith(CODE, 'hunter2');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Sprint 24 retro');
  });

  it('explains that a retro still in progress has nothing to summarise', async () => {
    const fixture = await setup(new FakeExportService('NotYetVisible', null));
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('has not reached its discussion yet');
    expect(el.querySelector('#summary-actions')).toBeNull();
  });

  it('says plainly when there is no such retro', async () => {
    const fixture = await setup(new FakeExportService('BoardNotFound', null));

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('could not be found');
  });

  it('downloads the file with the password already in hand', async () => {
    const exports = new FakeExportService();
    const fixture = await setup(exports);
    const cmp = fixture.componentInstance as unknown as Cmp;

    cmp.password = 'hunter2';
    await cmp.download('md');

    expect(exports.download).toHaveBeenCalledWith(CODE, 'md', 'hunter2');
  });

  it('names the retro the actions were carried from', async () => {
    const fixture = await setup(
      new FakeExportService('Ok', summary({ carriedFromShortCode: 'retro-1' })),
    );

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('retro-1');
  });
});
