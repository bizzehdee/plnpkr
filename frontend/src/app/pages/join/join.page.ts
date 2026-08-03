import { Component, inject, signal, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SignalrRealtimeClient } from '../../core/realtime.client';
import { IdentityService } from '../../core/identity.service';
import { SessionMembershipService } from '../../core/session-membership.service';
import { I18nService } from '../../core/i18n.service';
import { TranslatePipe } from '../../core/translate.pipe';
import { resolveApiBase } from '../../core/app-config';
import { ParticipantRole, SessionLanding } from '../../core/models';

@Component({
  selector: 'app-join',
  imports: [FormsModule, TranslatePipe],
  templateUrl: './join.page.html',
})
export class JoinPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);
  private readonly realtime = inject(SignalrRealtimeClient);
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
    } catch {
      this.notFound.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  protected async join(): Promise<void> {
    this.error.set(null);
    if (!this.displayName.trim()) {
      this.error.set(this.i18n.t('join.errorNameRequired'));
      return;
    }

    this.busy.set(true);
    try {
      await this.realtime.connect();
      this.identity.displayName = this.displayName.trim();

      const result = await this.realtime.joinSession(
        this.shortCode,
        this.identity.userId,
        this.displayName.trim(),
        this.role,
        this.password.trim() || null,
      );

      switch (result.status) {
        case 'Ok':
          this.membership.remember(this.shortCode, this.role);
          await this.router.navigate(['/session', this.shortCode]);
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
