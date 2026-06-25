import { RevealCueService } from './reveal-cue.service';

describe('RevealCueService', () => {
  afterEach(() => localStorage.removeItem('pp.sound-muted'));

  it('defaults to unmuted', () => {
    const svc = new RevealCueService();
    expect(svc.muted()).toBe(false);
  });

  it('toggles mute and persists the preference', () => {
    const svc = new RevealCueService();
    svc.toggleMute();
    expect(svc.muted()).toBe(true);
    expect(localStorage.getItem('pp.sound-muted')).toBe('1');

    // A fresh instance reads the stored preference back.
    expect(new RevealCueService().muted()).toBe(true);
  });

  it('does not throw when playing a reveal cue without Web Audio available', () => {
    const svc = new RevealCueService();
    // jsdom has no AudioContext — ensureContext() returns null and playReveal() no-ops.
    expect(() => svc.playReveal()).not.toThrow();
  });

  it('stays silent (no-op) when muted', () => {
    const svc = new RevealCueService();
    svc.setMuted(true);
    expect(() => svc.playReveal()).not.toThrow();
  });
});
