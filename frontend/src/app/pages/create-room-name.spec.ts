import { TestBed } from '@angular/core/testing';
import { signal, Type } from '@angular/core';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { vi } from 'vitest';
import { CoffeeCreatePage } from './coffee/create/coffee-create.page';
import { PokerCreatePage } from './poker/create/poker-create.page';
import { RetroCreatePage } from './retro/create/retro-create.page';
import { StandupCreatePage } from './standup/create/standup-create.page';
import { IdentityService } from '../core/identity.service';
import { RoomNameService } from '../core/room-name.service';
import { SessionMembershipService } from '../core/session-membership.service';
import { SignalrCoffeeClient } from '../core/coffee.client';
import { SignalrRealtimeClient } from '../core/poker.client';
import { SignalrRetroClient } from '../core/retro.client';
import { SignalrStandupClient } from '../core/standup.client';
import { RoomTool } from '../core/models';

/**
 * The room-name memory, proved on each of the four create forms.
 *
 * The service and the field have their own specs; what these cover is the *wiring*, which is the
 * one part that is genuinely four copies: recall the name into the field, compose the stamped name
 * for the create call, and remember the typed name afterwards. A form that composed but forgot to
 * remember, or remembered the composed name, would pass both other specs.
 */

const ME = 'me';
const CODE = 'blue-fox-42';

/** Today, as the service stamps it. */
function todayStamp(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

/** One create form under test: how to reach its name field and how its client reports success. */
interface FormUnderTest {
  readonly tool: RoomTool;
  readonly page: Type<unknown>;
  readonly clientToken: unknown;
  /** The create method the page calls, and which argument position carries the name. */
  readonly method: string;
  /** The page's own property name for the room name. */
  readonly nameField: string;
  /** What that client's success result looks like. */
  readonly success: unknown;
}

const FORMS: FormUnderTest[] = [
  {
    tool: 'Poker',
    page: PokerCreatePage,
    clientToken: SignalrRealtimeClient,
    method: 'createSession',
    nameField: 'sessionName',
    success: { status: 'Ok', session: { shortCode: CODE }, error: null },
  },
  {
    tool: 'Retro',
    page: RetroCreatePage,
    clientToken: SignalrRetroClient,
    method: 'createBoard',
    nameField: 'boardName',
    success: { status: 'Ok', board: { shortCode: CODE }, error: null },
  },
  {
    tool: 'Coffee',
    page: CoffeeCreatePage,
    clientToken: SignalrCoffeeClient,
    method: 'createBoard',
    nameField: 'boardName',
    success: { status: 'Ok', board: { shortCode: CODE }, error: null },
  },
  {
    tool: 'Standup',
    page: StandupCreatePage,
    clientToken: SignalrStandupClient,
    method: 'createBoard',
    nameField: 'boardName',
    success: { status: 'Ok', board: { shortCode: CODE }, error: null },
  },
];

describe.each(FORMS)('$tool create form — the room name', (form) => {
  let create: ReturnType<typeof vi.fn>;
  let names: RoomNameService;

  function setup() {
    create = vi.fn().mockResolvedValue(form.success);
    const client = {
      connect: vi.fn().mockResolvedValue(undefined),
      board: signal(null),
      session: signal(null),
      closed: signal(false),
      status: signal('connected'),
      reactions$: new Subject(),
      [form.method]: create,
    };

    TestBed.configureTestingModule({
      imports: [form.page],
      providers: [
        provideRouter([]),
        { provide: form.clientToken, useValue: client },
        { provide: IdentityService, useValue: { userId: ME, displayName: 'Me' } },
        { provide: SessionMembershipService, useValue: { get: () => 'Voter', remember: vi.fn() } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => null }, queryParamMap: { get: () => null } } },
        },
      ],
    });

    names = TestBed.inject(RoomNameService);
    const fixture = TestBed.createComponent(form.page);
    fixture.detectChanges();
    return {
      fixture,
      cmp: fixture.componentInstance as Record<string, unknown> & { create(): Promise<void> },
    };
  }

  /** The name the client was actually asked to create the room under. */
  const createdName = () => create.mock.calls[0][0] as string;

  beforeEach(() => localStorage.clear());

  afterEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
  });

  it('starts blank on a browser that has never created one', () => {
    const { cmp } = setup();

    expect(cmp[form.nameField]).toBe('');
    expect(cmp['appendDate']).toBe(false);
  });

  it('offers back the name used last time', () => {
    new RoomNameService().remember(form.tool, 'Team Dragon', true);

    const { cmp } = setup();

    expect(cmp[form.nameField]).toBe('Team Dragon');
    expect(cmp['appendDate']).toBe(true);
  });

  it('does not offer back another tool’s name', () => {
    // Shared storage, separate entries — a poker session and a retro are rarely called the same.
    const other = FORMS.find((f) => f.tool !== form.tool)!.tool;
    new RoomNameService().remember(other, 'Somebody else’s room', false);

    expect(setup().cmp[form.nameField]).toBe('');
  });

  it('creates the room under the stamped name when the box is ticked', async () => {
    const { cmp } = setup();
    cmp[form.nameField] = 'Team Dragon';
    cmp['appendDate'] = true;

    await cmp.create();

    expect(createdName()).toBe(`Team Dragon ${todayStamp()}`);
  });

  it('creates the room under the plain name when it is not', async () => {
    const { cmp } = setup();
    cmp[form.nameField] = 'Team Dragon';
    cmp['appendDate'] = false;

    await cmp.create();

    expect(createdName()).toBe('Team Dragon');
  });

  it('remembers the name as typed, not as stamped', async () => {
    // The bug this guards: storing "Team Dragon 2026-09-10" would create
    // "Team Dragon 2026-09-10 2026-09-11" tomorrow.
    const { cmp } = setup();
    cmp[form.nameField] = 'Team Dragon';
    cmp['appendDate'] = true;

    await cmp.create();

    expect(names.recall(form.tool)).toEqual({ name: 'Team Dragon', appendDate: true });
  });

  it('remembers the tickbox being off', async () => {
    const { cmp } = setup();
    cmp[form.nameField] = 'Team Dragon';
    cmp['appendDate'] = false;

    await cmp.create();

    expect(names.recall(form.tool).appendDate).toBe(false);
  });

  it('still requires a name of its own — the stamp is not one', async () => {
    const { cmp } = setup();
    cmp[form.nameField] = '   ';
    cmp['appendDate'] = true;

    await cmp.create();

    expect(create).not.toHaveBeenCalled();
    expect(names.recall(form.tool).name).toBe('');
  });

  it('remembers nothing when the room was not created', async () => {
    const { cmp } = setup();
    create.mockResolvedValue({ status: 'RateLimited', session: null, board: null, error: null });
    cmp[form.nameField] = 'Team Dragon';
    cmp['appendDate'] = true;

    await cmp.create();

    expect(names.recall(form.tool).name).toBe('');
  });
});
