import { Injector, ProviderToken } from '@angular/core';
import type { RoomClientBase } from './room.client';
import { RoomTool } from './models';

/**
 * What the platform knows about each tool, in one place (#35).
 *
 * Two things every tool needs and nothing else should have to know: which route its board lives at,
 * and how to reach its hub client. Both were previously if/else chains that grew a branch per tool —
 * the `/join` landing had one for each (#32), and adding a third would have meant editing both.
 *
 * The client is behind a thunk so importing this file does not pull every tool's realtime transport
 * into the initial bundle (#31): the import happens when a tool is actually needed.
 */
interface ToolEntry {
  /** The route prefix for a room of this tool: `/poker/<code>`, `/retro/<code>`, … */
  readonly route: string;

  /** i18n key for the tool's display name, so it follows the locale (#5). */
  readonly nameKey: string;

  /** Loads the tool's hub client class, on demand. */
  readonly client: () => Promise<ProviderToken<RoomClientBase>>;
}

export const TOOLS: Record<RoomTool, ToolEntry> = {
  Poker: {
    route: '/poker',
    nameKey: 'tool.poker.name',
    client: async () => (await import('./poker.client')).SignalrRealtimeClient,
  },
  Retro: {
    route: '/retro',
    nameKey: 'tool.retro.name',
    client: async () => (await import('./retro.client')).SignalrRetroClient,
  },
  Coffee: {
    route: '/coffee',
    nameKey: 'tool.coffee.name',
    client: async () => (await import('./coffee.client')).SignalrCoffeeClient,
  },
};

/** The route a room of this tool lives at. */
export function routeFor(tool: RoomTool, shortCode: string): unknown[] {
  return [TOOLS[tool].route, shortCode];
}

/**
 * The hub client for this tool, imported on demand. `Injector.get` resolves the same root-provided
 * singleton the tool's own page injects, so a seat taken here is the seat that page finds.
 */
export async function clientFor(injector: Injector, tool: RoomTool): Promise<RoomClientBase> {
  return injector.get(await TOOLS[tool].client());
}
