import { PageHeader } from '../../../shared/page-header';
import { RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { AuthService } from '../../../core/auth/auth.service';
import { roleLabels, roleNavigation } from '../../../core/auth/auth.models';

@Component({
  selector: 'app-session',
  standalone: true,
  imports: [PageHeader, RouterLink, DatePipe],
  template: `<section class="session-page" aria-labelledby="session-title">
    @if (auth.user(); as user) {
      <app-page-header
        headingId="session-title"
        [title]="navigation[user.role]"
        context="Cuenta / Mi sesión"
        description="Identidad y acceso al espacio de soporte."
      />
      <div class="session-workspace">
        <section class="account-panel" aria-labelledby="account-title">
          <div class="account-heading">
            <span class="account-avatar" aria-hidden="true">{{
              user.email.slice(0, 1).toUpperCase()
            }}</span>
            <div>
              <h2 id="account-title">Mi cuenta</h2>
              <span>{{ labels[user.role] }}</span>
            </div>
          </div>
          <dl>
            <div>
              <dt>Correo electrónico</dt>
              <dd>{{ user.email }}</dd>
            </div>
            <div>
              <dt>Rol</dt>
              <dd>{{ labels[user.role] }}</dd>
            </div>
            <div>
              <dt>Caducidad de la sesión</dt>
              <dd>{{ auth.expiresAt() | date: 'HH:mm:ss' }}</dd>
            </div>
          </dl>
          <p class="note">
            La sesión se conserva en esta página. Al recargar tendrás que iniciar sesión de nuevo.
            Puedes cerrar sesión desde la barra superior.
          </p>
        </section>
        <section class="account-panel" aria-labelledby="session-tools">
          <h2 id="session-tools">Herramientas disponibles</h2>
          <nav aria-label="Accesos de mi sesión">
            <a routerLink="/tickets">Consultar incidencias <span aria-hidden="true">→</span></a
            ><a routerLink="/tickets/new">Nueva incidencia <span aria-hidden="true">→</span></a>
            @if (user.role === 'Technician') {
              <a routerLink="/my-tickets">Mis incidencias <span aria-hidden="true">→</span></a>
            }
            @if (user.role === 'Admin') {
              <a routerLink="/admin">Administración <span aria-hidden="true">→</span></a>
            }
          </nav>
          <p class="note">El servidor comprueba los permisos en cada operación.</p>
        </section>
      </div>
    }
  </section>`,
  styles: `
    .session-page {
      max-width: 1200px;
      margin: auto;
    }
    .session-workspace {
      display: grid;
      grid-template-columns: 1.2fr 1fr;
      gap: 24px;
    }
    .account-panel {
      background: var(--surface);
      border: 1px solid var(--border);
      padding: 24px;
      min-width: 0;
    }
    .account-heading {
      display: flex;
      gap: 12px;
      align-items: center;
      padding-bottom: 20px;
      border-bottom: 1px solid var(--border);
    }
    .account-avatar {
      display: grid;
      place-items: center;
      width: 44px;
      height: 44px;
      background: var(--accent-soft);
      color: var(--accent);
      font-size: 20px;
      font-weight: 600;
    }
    .account-heading span:not(.account-avatar) {
      color: var(--muted);
      font-size: 13px;
    }
    h2 {
      font-size: 16px;
      margin: 0 0 8px;
    }
    dl {
      margin: 8px 0 20px;
    }
    dl > div {
      display: grid;
      grid-template-columns: 160px minmax(0, 1fr);
      gap: 16px;
      padding: 14px 0;
      border-bottom: 1px solid var(--border);
      font-size: 13px;
    }
    dt,
    .note {
      color: var(--muted);
    }
    dd {
      margin: 0;
      overflow-wrap: anywhere;
    }
    .note {
      font-size: 12px;
      line-height: 1.7;
    }
    nav {
      display: grid;
      margin-top: 20px;
    }
    a {
      display: flex;
      justify-content: space-between;
      gap: 12px;
      padding: 14px 0;
      border-bottom: 1px solid var(--border);
      color: var(--accent);
      text-decoration: none;
      font-size: 14px;
    }
    a:hover {
      text-decoration: underline;
    }
    @media (max-width: 767px) {
      .session-workspace {
        grid-template-columns: 1fr;
      }
      .account-panel {
        padding: 16px;
      }
      dl > div {
        grid-template-columns: 1fr;
        gap: 4px;
      }
    }
  `,
})
export class SessionPage {
  readonly auth = inject(AuthService);
  readonly labels = roleLabels;
  readonly navigation = roleNavigation;
}
