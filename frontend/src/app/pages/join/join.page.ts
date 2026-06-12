import { Component, inject, signal, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SignalrRealtimeClient } from '../../core/realtime.client';
import { IdentityService } from '../../core/identity.service';
import { resolveApiBase } from '../../core/app-config';
import { ParticipantRole, SessionLanding } from '../../core/models';

@Component({
  selector: 'app-join',
  imports: [FormsModule],
  templateUrl: './join.page.html',
})
export class JoinPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);
  private readonly realtime = inject(SignalrRealtimeClient);
  private readonly identity = inject(IdentityService);

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
      this.error.set('Please enter your name.');
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
          await this.router.navigate(['/session', this.shortCode]);
          break;
        case 'NameTaken':
          this.error.set(result.error ?? 'That name is already taken — please pick another.');
          break;
        case 'PasswordRequired':
          this.requiresPassword.set(true);
          this.error.set('This session requires a password.');
          break;
        case 'WrongPassword':
          this.requiresPassword.set(true);
          this.error.set(result.error ?? 'Incorrect password — please try again.');
          break;
        case 'SessionNotFound':
          this.notFound.set(true);
          break;
        default:
          this.error.set(result.error ?? 'Could not join the session.');
      }
    } catch {
      this.error.set('Could not reach the server. Is the API running?');
    } finally {
      this.busy.set(false);
    }
  }
}
