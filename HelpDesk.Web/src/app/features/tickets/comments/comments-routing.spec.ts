import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { App } from '../../../app';
import { routes } from '../../../app.routes';
import { AuthService } from '../../../core/auth/auth.service';
import { authInterceptor } from '../../../core/auth/auth.interceptor';
import { signIn } from '../../../core/auth/auth.testing';
import { ticketFixture } from '../ticket.testing';
import { commentFixture, commentsFixture } from './comment.testing';

describe('Comments inside the protected ticket detail', () => {
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
    'allows %s to comment on a visible closed ticket without losing navigation context',
    async (role) => {
      signIn(auth, http, role);
      const f = TestBed.createComponent(App);
      try {
        await router.navigateByUrl('/tickets/41?priority=High&page=2');
        f.detectChanges();
        await f.whenStable();
        http
          .expectOne('/api/tickets/41')
          .flush(ticketFixture({ status: 'Closed', createdByUserId: 3 }));
        f.detectChanges();
        (f.nativeElement.querySelector('app-ticket-comments button') as HTMLButtonElement).click();
        f.detectChanges();
        const read = http.expectOne('/api/tickets/41/comments?pageSize=20');
        expect(read.request.headers.has('Authorization')).toBe(true);
        read.flush(commentsFixture());
        f.detectChanges();
        const textarea = f.nativeElement.querySelector('#comment-content') as HTMLTextAreaElement;
        textarea.value = 'Allowed comment';
        textarea.dispatchEvent(new Event('input'));
        const form = f.nativeElement.querySelector('app-ticket-comments form') as HTMLFormElement;
        form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
        const request = http.expectOne('/api/tickets/41/comments');
        expect(request.request.headers.has('Authorization')).toBe(true);
        f.detectChanges();
        const refreshDetail = [...f.nativeElement.querySelectorAll('button')].find(
          (b: HTMLButtonElement) => b.textContent?.includes('Actualizar detalle'),
        ) as HTMLButtonElement;
        expect(refreshDetail.disabled).toBe(true);
        request.flush(commentFixture({ id: 101 }), { status: 201, statusText: 'Created' });
        expect(auth.isAuthenticated()).toBe(true);
        expect(router.url).toContain('priority=High&page=2');
        expect(f.nativeElement.querySelector('.back-link').getAttribute('href')).toBe(
          '/tickets?priority=High&page=2',
        );
      } finally {
        f.destroy();
      }
    },
  );
  it.each([401, 403])(
    'handles a read denial %s without login loops or stale comments',
    async (status) => {
      signIn(auth, http, 'Technician');
      const f = TestBed.createComponent(App);
      try {
        await router.navigateByUrl('/tickets/41');
        f.detectChanges();
        await f.whenStable();
        http.expectOne('/api/tickets/41').flush(ticketFixture({ assignedToUserId: 3 }));
        f.detectChanges();
        (f.nativeElement.querySelector('app-ticket-comments button') as HTMLButtonElement).click();
        http
          .expectOne('/api/tickets/41/comments?pageSize=20')
          .flush({}, { status, statusText: 'Denied' });
        await f.whenStable();
        f.detectChanges();
        expect(auth.isAuthenticated()).toBe(status !== 401);
        expect(router.url).toBe(status === 401 ? '/login' : '/tickets/41');
        http.expectNone((r) => r.url.endsWith('/comments'));
      } finally {
        f.destroy();
      }
    },
  );
});
