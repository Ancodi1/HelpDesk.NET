import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { App } from '../../app';
import { routes } from '../../app.routes';
import { AuthService } from '../../core/auth/auth.service';
import { UserRole } from '../../core/auth/auth.models';
import { signIn } from '../../core/auth/auth.testing';
import { TicketService } from './ticket.service';
import { TicketQuery } from './ticket.models';
import { pageFixture, ticketFixture } from './ticket.testing';

describe('Real ticket routing and permissions', () => {
  let auth: AuthService;
  let http: HttpTestingController;
  const list = vi.fn((q: TicketQuery) =>
    of(
      pageFixture({
        page: q.page,
        pageSize: q.pageSize,
        totalCount: 11,
        totalPages: Math.ceil(11 / q.pageSize),
        items: [ticketFixture({ createdByUserId: 3 })],
      }),
    ),
  );
  beforeEach(() => {
    list.mockClear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: TicketService, useValue: { list } },
      ],
    });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    auth.logout();
    http.verify();
  });
  it('redirects anonymous visitors before requesting any ticket data', async () => {
    const f = TestBed.createComponent(App);
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/tickets');
    f.detectChanges();
    await f.whenStable();
    expect(f.nativeElement.querySelector('a[href="/tickets"]')).toBeNull();
    expect(router.url).toBe('/login');
    expect(list).not.toHaveBeenCalled();
  });
  it.each<UserRole>(['User', 'Technician', 'Admin'])(
    'allows %s to see exactly the API result through the protected route',
    async (role) => {
      signIn(auth, http, role);
      const f = TestBed.createComponent(App);
      const router = TestBed.inject(Router);
      await router.navigateByUrl('/tickets?priority=High&page=2');
      f.detectChanges();
      await f.whenStable();
      expect(router.url).toBe('/tickets?priority=High&page=2');
      expect(list).toHaveBeenCalledWith(expect.objectContaining({ page: 2, priority: 'High' }));
      expect(f.nativeElement.querySelector('h1').textContent).toContain('Incidencias');
      expect(f.nativeElement.querySelectorAll('tbody tr').length).toBe(1);
    },
  );
  it('opens tickets from the sidebar and keeps its active state with query parameters', async () => {
    signIn(auth, http);
    const f = TestBed.createComponent(App);
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/session');
    f.detectChanges();
    await f.whenStable();
    const link = f.nativeElement.querySelector('aside a[href="/tickets"]') as HTMLAnchorElement;
    expect(link.textContent).toContain('Tickets');
    expect(link.classList.contains('active')).toBe(false);
    link.click();
    await f.whenStable();
    f.detectChanges();
    expect(router.url).toBe('/tickets');
    expect(link.classList.contains('active')).toBe(true);
    expect(link.getAttribute('aria-current')).toBe('page');
    await router.navigateByUrl('/tickets?status=Open&page=2');
    f.detectChanges();
    await f.whenStable();
    expect(link.classList.contains('active')).toBe(true);
    expect(link.getAttribute('aria-current')).toBe('page');
    await router.navigateByUrl('/session');
    f.detectChanges();
    await f.whenStable();
    expect(link.classList.contains('active')).toBe(false);
  });
  it('updates the actual URL when paging and restores it on navigation', async () => {
    signIn(auth, http);
    const f = TestBed.createComponent(App);
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/tickets?status=Open');
    f.detectChanges();
    await f.whenStable();
    f.nativeElement.querySelectorAll('.pagination button')[1].click();
    await f.whenStable();
    f.detectChanges();
    expect(router.url).toContain('page=2');
    expect(router.url).toContain('status=Open');
    expect(f.nativeElement.textContent).toContain('Página 2 de 2');
    await router.navigateByUrl('/tickets?status=Closed&pageSize=5');
    f.detectChanges();
    await f.whenStable();
    expect(list).toHaveBeenLastCalledWith(
      expect.objectContaining({ status: 'Closed', page: 1, pageSize: 5 }),
    );
  });
});
