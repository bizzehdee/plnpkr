import { Injectable, signal } from '@angular/core';

const MUTE_KEY = 'pp.sound-muted';

/**
 * Plays a short audible cue when votes are revealed and tracks a per-browser mute
 * preference (persisted in localStorage). The sound is synthesised with the Web Audio
 * API so nothing is fetched — keeping the page self-contained (#2).
 *
 * The visual flourish on reveal is CSS-driven (and disabled under prefers-reduced-motion);
 * this service is only responsible for the sound + the mute toggle.
 */
@Injectable({ providedIn: 'root' })
export class RevealCueService {
  private readonly _muted = signal<boolean>(this.readStored());
  readonly muted = this._muted.asReadonly();

  private audioContext: AudioContext | null = null;

  toggleMute(): void {
    this.setMuted(!this._muted());
  }

  setMuted(muted: boolean): void {
    this._muted.set(muted);
    try {
      localStorage.setItem(MUTE_KEY, muted ? '1' : '0');
    } catch {
      /* storage may be unavailable; the preference still applies for this session */
    }
  }

  /** Plays a brief two-note chime, unless muted or Web Audio is unavailable. */
  playReveal(): void {
    if (this._muted()) return;

    const ctx = this.ensureContext();
    if (!ctx) return;

    try {
      // A quick rising two-note blip: pleasant, short, unmistakable as "revealed".
      this.blip(ctx, 660, ctx.currentTime, 0.12);
      this.blip(ctx, 880, ctx.currentTime + 0.12, 0.16);
    } catch {
      /* audio can fail on locked-down or backgrounded tabs — the cue is non-essential */
    }
  }

  private blip(ctx: AudioContext, frequency: number, startAt: number, duration: number): void {
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    osc.type = 'sine';
    osc.frequency.value = frequency;
    // Short attack + decay so notes don't click or run together.
    gain.gain.setValueAtTime(0.0001, startAt);
    gain.gain.exponentialRampToValueAtTime(0.18, startAt + 0.02);
    gain.gain.exponentialRampToValueAtTime(0.0001, startAt + duration);
    osc.connect(gain).connect(ctx.destination);
    osc.start(startAt);
    osc.stop(startAt + duration);
  }

  private ensureContext(): AudioContext | null {
    if (this.audioContext) return this.audioContext;
    const Ctor =
      typeof window !== 'undefined'
        ? (window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext)
        : undefined;
    if (!Ctor) return null;
    try {
      this.audioContext = new Ctor();
      return this.audioContext;
    } catch {
      return null;
    }
  }

  private readStored(): boolean {
    try {
      return localStorage.getItem(MUTE_KEY) === '1';
    } catch {
      return false;
    }
  }
}
