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
        ><small>{{ labels[user.role] }} · Sesión iniciada</small></a
      >
      <button type="button" (click)="logout()">Cerrar sesión</button>
    } @else {
      <a routerLink="/login">Iniciar sesión</a
      ><a class="register-link" routerLink="/register">Crear cuenta</a>
    }
  </nav>`,
  styles: `
    .session-menu {
      display: flex;
      align-items: center;
      justify-content: flex-end;
      flex-wrap: wrap;
      gap: 10px 16px;
    }
    a,
    button {
      color: var(--accent);
      font-size: 12px;
      font-weight: 600;
      text-decoration: none;
    }
    a:hover {
      text-decoration: underline;
    }
    button,
    .register-link {
      border: 1px solid var(--border);
      border-radius: 7px;
      padding: 10px 12px;
      background: white;
      cursor: pointer;
      min-height: 42px;
    }
    button:hover,
    .register-link:hover {
      border-color: var(--accent);
    }
    .identity {
      color: var(--ink);
      min-width: 0;
    }
    .identity span {
      display: block;
      max-width: 210px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    small {
      display: block;
      color: var(--muted);
      font-size: 10px;
      margin-top: 4px;
      font-weight: 400;
    }
    @media (max-width: 480px) {
      .session-menu {
        justify-content: flex-start;
      }
      .identity span {
        max-width: 185px;
      }
    }
  `,
})
export class SessionMenu {
  readonly auth = inject(AuthService);
  readonly labels = roleLabels;
  private readonly router = inject(Router);
  logout(): void {
    this.auth.logout();
    void this.router.navigateByUrl('/login');
  }
}
