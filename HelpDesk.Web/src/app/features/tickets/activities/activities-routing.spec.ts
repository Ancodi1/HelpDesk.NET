import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter, Router } from '@angular/router';
import { App } from '../../../app';
import { routes } from '../../../app.routes';
import { AuthService } from '../../../core/auth/auth.service';
import { authInterceptor } from '../../../core/auth/auth.interceptor';
import { signIn } from '../../../core/auth/auth.testing';
import { ticketFixture } from '../ticket.testing';
import { TicketManagement } from '../detail/ticket-management';
import { activityPageFixture as page } from './activity.testing';

describe('Activity inside the protected detail', () => {
  let http: HttpTestingController;
  let auth: AuthService;
  let router: Router;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
  });
  afterEach(() => {
    auth.logout();
    http.verify();
  });
  it.each(['User', 'Technician', 'Admin'] as const)(
    'reads visible history as %s independently',
    async (role) => {
      signIn(auth, http, role);
      const f = TestBed.createComponent(App);
      try {
        await router.navigateByUrl('/tickets/41?priority=High&page=2');
        f.detectChanges();
        http.expectOne('/api/tickets/41').flush(ticketFixture({ assignedToUserId: 3 }));
        f.detectChanges();
        (
          f.nativeElement.querySelector('app-ticket-activities button') as HTMLButtonElement
        ).click();
        const read = http.expectOne('/api/tickets/41/activities?pageSize=20');
        expect(read.request.headers.has('Authorization')).toBe(true);
        read.flush(page());
        f.detectChanges();
        expect(f.nativeElement.textContent).toContain('Historical admin');
        expect(router.url).toContain('priority=High&page=2');
        http.expectNone((r) => r.url.includes('/users') || r.url.includes('/comments'));
      } finally {
        f.destroy();
      }
    },
  );
  it('refreshes open history after a confirmed write and keeps the detail usable after history failure', async () => {
    signIn(auth, http, 'Admin');
    const f = TestBed.createComponent(App);
    try {
      await router.navigateByUrl('/tickets/41');
      f.detectChanges();
      http.expectOne('/api/tickets/41').flush(ticketFixture());
      f.detectChanges();
      (f.nativeElement.querySelector('app-ticket-activities button') as HTMLButtonElement).click();
      http.expectOne('/api/tickets/41/activities?pageSize=20').flush(page({ items: [] }));
      const management = f.debugElement.query(By.directive(TicketManagement))
        .componentInstance as TicketManagement;
      management.text.controls.title.setValue('Changed title');
      management.save();
      http.expectOne('/api/tickets/41').flush(ticketFixture({ title: 'Changed title' }));
      f.detectChanges();
      http
        .expectOne('/api/tickets/41/activities?pageSize=20')
        .flush({}, { status: 500, statusText: 'Error' });
      f.detectChanges();
      expect(f.nativeElement.querySelector('h1').textContent).toBe('Changed title');
      expect(f.nativeElement.textContent).toContain('No se pudo cargar el historial');
      expect(auth.isAuthenticated()).toBe(true);
    } finally {
      f.destroy();
    }
  });
});
