import { DatePipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { AuthService } from '../../../core/auth/auth.service';
import { roleLabels, roleNavigation } from '../../../core/auth/auth.models';

@Component({
  selector: 'app-session',
  standalone: true,
  imports: [DatePipe],
  template: ` <section class="session-card" aria-labelledby="session-title">
    @if (auth.user(); as user) {
      <p class="eyebrow">SESIÓN INICIADA</p>
      <h1 id="session-title">{{ navigation[user.role] }}</h1>
      <p>Tu acceso a HelpDesk.NET está preparado.</p>
      <dl>
        <dt>Correo</dt>
        <dd>{{ user.email }}</dd>
        <dt>Rol</dt>
        <dd>{{ labels[user.role] }}</dd>
        <dt>La sesión caduca a las</dt>
        <dd>{{ auth.expiresAt() | date: 'HH:mm:ss' }}</dd>
      </dl>
      <p class="note">
        Puedes cerrar sesión desde la barra superior. El acceso a las funciones siempre será
        comprobado por el servidor.
      </p>
      <p class="note">
        La gestión de incidencias y el panel administrativo se incorporarán en próximas fases.
      </p>
    }
  </section>`,
  styles: `
    .session-card {
      max-width: 700px;
      padding: 32px;
      border: 1px solid var(--border);
      border-radius: 12px;
      background: white;
    }
    .eyebrow {
      color: var(--accent);
      font-size: 10px;
      font-weight: 700;
      letter-spacing: 1px;
    }
    h1 {
      font-size: 28px;
      letter-spacing: -0.8px;
    }
    p {
      color: var(--muted);
      line-height: 1.8;
    }
    dl {
      display: grid;
      grid-template-columns: auto minmax(0, 1fr);
      gap: 14px 24px;
      margin-block: 28px;
      font-size: 14px;
    }
    dt {
      color: var(--muted);
    }
    dd {
      margin: 0;
      overflow-wrap: anywhere;
    }
    .note {
      font-size: 12px;
    }
    @media (max-width: 480px) {
      .session-card {
        padding: 22px;
      }
      dl {
        grid-template-columns: 1fr;
        gap: 8px;
      }
      dd {
        margin-bottom: 12px;
      }
    }
  `,
})
export class SessionPage {
  readonly auth = inject(AuthService);
  readonly labels = roleLabels;
  readonly navigation = roleNavigation;
}
