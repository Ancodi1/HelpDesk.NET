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
        path: 'tickets/new',
        title: 'Nuevo ticket | HelpDesk.NET',
        data: { label: 'Nuevo ticket' },
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/tickets/create/ticket-create').then((m) => m.TicketCreate),
      },
      {
        path: 'tickets/:id',
        title: 'Detalle | HelpDesk.NET',
        data: { label: 'Detalle del ticket' },
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/tickets/detail/ticket-detail').then((m) => m.TicketDetail),
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
