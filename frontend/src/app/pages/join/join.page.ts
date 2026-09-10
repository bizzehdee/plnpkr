import { Component, Injector, inject, signal, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import type { RoomClientBase } from '../../core/room.client';
import { IdentityService } from '../../core/identity.service';
import { SessionMembershipService } from '../../core/session-membership.service';
import { I18nService } from '../../core/i18n.service';
import { TranslatePipe } from '../../core/translate.pipe';
import { resolveApiBase } from '../../core/app-config';
import { ParticipantRole, RoomTool, SessionLanding } from '../../core/models';

/**
 * The one invite-link shape both tools share (#19): `/join/<code>` resolves the short code to a
 * room, gates on name and password, seats the joiner, and sends them to that tool's page.
 *
 * **The join goes over the room's own hub (#32).** A short code belongs to exactly one tool, and
 * each tool has its own hub — so this page reads the tool from the landing response and drives that
 * tool's client. It used to send every join to the *poker* hub regardless: for a retro room the
 * server seated the participant and then failed projecting a poker snapshot, so the joiner saw
 * "could not reach the server" while their seat had in fact been taken.
 *
 * The client is imported on demand, once the tool is known. That keeps the realtime transport out
 * of the initial bundle (#31) on a page whose first act is an HTTP read, and it means this page
 * holds no compile-time knowledge of either tool beyond the route it navigates to.
 */
@Component({
  selector: 'app-join',
  imports: [FormsModule, TranslatePipe],
  templateUrl: './join.page.html',
})
export class JoinPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);
  private readonly injector = inject(Injector);
  private readonly identity = inject(IdentityService);
  private readonly membership = inject(SessionMembershipService);
  private readonly i18n = inject(I18nService);

  protected shortCode = '';
  protected displayName = this.identity.displayName;
  protected role: ParticipantRole = 'Voter';
  protected password = '';

  protected readonly loading = signal(true);
  protected readonly sessionName = signal<string | null>(null);
  protected readonly requiresPassword = signal(false);
  protected readonly tool = signal<RoomTool>('Poker');
  protected readonly notFound = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    this.shortCode = this.route.snapshot.paramMap.get('shortCode') ?? '';
    try {
      const landing = await firstValueFrom(
        this.http.get<SessionLanding>(`${resolveApiBase()}/api/sessions/${this.shortCode}`),
      );
      this.sessionName.set(landing.name);
      this.requiresPassword.set(landing.requiresPassword);
      // A short code belongs to one tool (#19); remember which so a successful join lands on that
      // tool's page rather than assuming poker.
      this.tool.set(landing.tool);
    } catch {
      this.notFound.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * The hub client for this room's tool, imported when it is needed rather than at startup (#32).
   * `Injector.get` still resolves the same root-provided singleton the tool's own page injects, so
   * the seat this takes is the seat that page finds on arrival.
   */
  private async clientFor(tool: RoomTool): Promise<RoomClientBase> {
    if (tool === 'Retro') {
      const { SignalrRetroClient } = await import('../../core/retro.client');
      return this.injector.get(SignalrRetroClient);
    }
    const { SignalrRealtimeClient } = await import('../../core/poker.client');
    return this.injector.get(SignalrRealtimeClient);
  }

  protected async join(): Promise<void> {
    this.error.set(null);
    if (!this.displayName.trim()) {
      this.error.set(this.i18n.t('join.errorNameRequired'));
      return;
    }

    this.busy.set(true);
    try {
      const client = await this.clientFor(this.tool());
      await client.connect();
      this.identity.displayName = this.displayName.trim();

      const result = await client.joinRoom(
        this.shortCode,
        this.identity.userId,
        this.displayName.trim(),
        this.role,
        this.password.trim() || null,
      );

      switch (result.status) {
        case 'Ok':
          this.membership.remember(this.shortCode, this.role);
          await this.router.navigate([this.tool() === 'Retro' ? '/retro' : '/poker', this.shortCode]);
          break;
        case 'NameTaken':
          this.error.set(this.i18n.t('err.join.nameTaken'));
          break;
        case 'PasswordRequired':
          this.requiresPassword.set(true);
          this.error.set(this.i18n.t('err.join.passwordRequired'));
          break;
        case 'WrongPassword':
          this.requiresPassword.set(true);
          this.error.set(this.i18n.t('err.join.wrongPassword'));
          break;
        case 'SessionNotFound':
          this.notFound.set(true);
          break;
        case 'SessionClosed':
          this.error.set(this.i18n.t('err.join.sessionClosed'));
          break;
        case 'SessionFull':
          this.error.set(this.i18n.t('err.join.sessionFull'));
          break;
        case 'RateLimited':
          this.error.set(this.i18n.t('err.rateLimited'));
          break;
        case 'WrongTool':
          // The server's backstop for a join routed to the wrong hub (#32). Reachable only if the
          // landing read and the join disagree — a room cannot change tool, so in practice this
          // means the code was re-used. Re-read the landing rather than guessing.
          this.error.set(this.i18n.t('err.join.wrongTool'));
          break;
        default:
          this.error.set(result.error ?? this.i18n.t('join.errorGeneric'));
      }
    } catch {
      this.error.set(this.i18n.t('common.errorUnreachable'));
    } finally {
      this.busy.set(false);
    }
  }
}
