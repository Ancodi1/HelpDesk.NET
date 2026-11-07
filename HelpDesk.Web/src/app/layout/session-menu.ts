import { DemoService } from '../core/demo/demo.service';
import { Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { roleLabels } from '../core/auth/auth.models';

@Component({
  selector: 'app-session-menu',
  standalone: true,
  imports: [RouterLink],
  template: ` <nav class="session-menu" aria-label="Cuenta">
    @if (auth.user(); as user) {
      <a class="identity" routerLink="/session"
        ><span>{{ user.email }}</span
        ><small
          >{{ labels[user.role] }}<span class="session-state"> · Sesión iniciada</span></small
        ></a
      >
      <button type="button" (click)="logout()">Cerrar sesión</button>
    } @else {
      <a routerLink="/login">Iniciar sesión</a>
      @if (!demo.enabled()) {
        <a class="register-link" routerLink="/register">Crear cuenta</a>
      }
    }
  </nav>`,
  styles: `
    .session-menu {
      display: flex;
      align-items: center;
      justify-content: flex-end;
      flex-wrap: wrap;
      gap: var(--space-2) var(--space-3);
    }
    a,
    button {
      color: var(--accent);
      font-size: var(--text-small);
      font-weight: 600;
      text-decoration: none;
    }
    a:hover {
      text-decoration: underline;
    }
    button,
    .register-link {
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      padding: var(--space-2) var(--space-3);
      background: var(--surface);
      cursor: pointer;
      min-height: 36px;
    }
    button:hover,
    .register-link:hover {
      border-color: var(--accent);
    }
    .identity {
      color: var(--ink);
      min-width: 0;
    }
    .identity > span {
      display: block;
      max-width: 210px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    small {
      display: block;
      color: var(--muted);
      font-size: var(--text-small);
      margin-top: var(--space-1);
      font-weight: 400;
    }
    @media (max-width: 959px) {
      a,
      .register-link,
      button {
        min-height: 44px;
        display: inline-flex;
        align-items: center;
      }
      .identity {
        display: block;
      }
    }
    @media (max-width: 639px) {
      .session-menu {
        justify-content: space-between;
        gap: 8px;
      }
      .session-state {
        display: none;
      }
      .identity > span {
        max-width: 160px;
      }
    }
  `,
})
export class SessionMenu {
  readonly demo = inject(DemoService);
  readonly auth = inject(AuthService);
  readonly labels = roleLabels;
  private readonly router = inject(Router);
  logout(): void {
    this.auth.logout();
    void this.router.navigateByUrl('/login');
  }
}
