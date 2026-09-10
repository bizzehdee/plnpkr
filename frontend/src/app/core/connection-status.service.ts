import { Injectable, Signal, computed, signal } from '@angular/core';

export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected';

/**
 * The shell's connection badge, decoupled from the tools that feed it (#31).
 *
 * **Why a registry rather than reading the clients directly.** The badge has to report the tool the
 * viewer is actually in — each tool has its own hub connection (#21), so watching only poker's would
 * read "disconnected" on a live retro board. The shell used to inject *both* clients to fold their
 * statuses together, which had two costs: adding a third tool meant editing the shell, and the
 * shell's import pinned `@microsoft/signalr` and both tool clients into the initial bundle, so the
 * picker downloaded the realtime transport it will never use.
 *
 * Now each client registers itself when it is first constructed — which happens when a tool's page
 * is loaded, and not before. On the picker nothing has registered, so the badge reads
 * "disconnected", which is the truth.
 */
@Injectable({ providedIn: 'root' })
export class ConnectionStatusService {
  private readonly sources = signal<(() => ConnectionStatus)[]>([]);

  /** Whichever registered client is doing something wins. */
  readonly status: Signal<ConnectionStatus> = computed<ConnectionStatus>(() => {
    const statuses = this.sources().map((read) => read());
    if (statuses.includes('connected')) return 'connected';
    if (statuses.includes('connecting')) return 'connecting';
    return 'disconnected';
  });

  /**
   * Registers a live status source. Takes a thunk rather than a signal so a client can register
   * from its base-class constructor, before its own fields have been initialised.
   */
  register(source: () => ConnectionStatus): void {
    this.sources.update((sources) => [...sources, source]);
  }
}
