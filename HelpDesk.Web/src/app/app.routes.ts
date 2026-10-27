import { Routes } from '@angular/router';
import { AppLayout } from './layout/app-layout';
import { authGuard } from './core/auth/auth.guards';

export const routes: Routes = [
  {
    path: '',
    component: AppLayout,
    children: [
      {
        path: '',
        pathMatch: 'full',
        title: 'Inicio | HelpDesk.NET',
        data: { label: 'Inicio' },
        loadComponent: () => import('./features/home/home').then((m) => m.Home),
      },
      {
        path: 'login',
        title: 'Acceso | HelpDesk.NET',
        data: { label: 'Acceso' },
        loadComponent: () => import('./features/auth/login/login').then((m) => m.Login),
      },
      {
        path: 'register',
        title: 'Registro | HelpDesk.NET',
        data: { label: 'Registro' },
        loadComponent: () => import('./features/auth/register/register').then((m) => m.Register),
      },
      {
        path: 'tickets',
        title: 'Incidencias | HelpDesk.NET',
        data: { label: 'Incidencias' },
        canActivate: [authGuard],
        loadComponent: () => import('./features/tickets/tickets').then((m) => m.Tickets),
      },
      {
        path: 'session',
        title: 'Mi sesión | HelpDesk.NET',
        data: { label: 'Mi sesión' },
        canActivate: [authGuard],
        loadComponent: () => import('./features/auth/session/session').then((m) => m.SessionPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
