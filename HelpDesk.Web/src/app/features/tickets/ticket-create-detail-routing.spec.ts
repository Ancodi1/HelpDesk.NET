import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { NavigationEnd, provideRouter, Router } from '@angular/router';
import { filter, firstValueFrom } from 'rxjs';
import { App } from '../../app';
import { routes } from '../../app.routes';
import { AuthService } from '../../core/auth/auth.service';
import { authInterceptor } from '../../core/auth/auth.interceptor';
import { signIn } from '../../core/auth/auth.testing';
import { UserRole } from '../../core/auth/auth.models';
import { TicketCreate } from './create/ticket-create';
import { pageFixture, ticketFixture } from './ticket.testing';

describe('Create and detail protected routing with HTTP-isolated API', () => {
  let f: ComponentFixture<App>;
  let router: Router;
  let http: HttpTestingController;
  let auth: AuthService;
  const visit = async (url: string) => {
    await router.navigateByUrl(url);
    f.detectChanges();
  };
  const actionNavigation = async (action: () => void) => {
    const ended = firstValueFrom(
      router.events.pipe(filter((event) => event instanceof NavigationEnd)),
    );
    action();
    await ended;
    f.detectChanges();
  };
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    router = TestBed.inject(Router);
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    f = TestBed.createComponent(App);
  });
  afterEach(() => {
    f.destroy();
    auth.logout();
    http.verify();
  });
  it.each(['/tickets/new', '/tickets/41'])(
    'blocks an anonymous visit before querying the API: %s',
    async (url) => {
      await visit(url);
      expect(router.url).toBe('/login');
      http.expectNone((r) => r.url.startsWith('/api/tickets'));
    },
  );
  it.each<UserRole>(['User', 'Technician', 'Admin'])(
    'offers creation to %s and matches new before the ID route',
    async (role) => {
      signIn(auth, http, role);
      await visit('/tickets');
      http.expectOne((r) => r.url === '/api/tickets').flush(pageFixture());
      f.detectChanges();
      const button = [...f.nativeElement.querySelectorAll('button')].find((b: HTMLButtonElement) =>
        b.textContent?.includes('Nuevo ticket'),
      ) as HTMLButtonElement;
      expect(button).toBeDefined();
      await actionNavigation(() => button.click());
      expect(router.url).toBe('/tickets/new');
      expect(f.nativeElement.querySelector('h1').textContent).toContain('Cuéntanos qué ocurre');
      http.expectNone('/api/tickets/new');
      http.expectNone('/api/tickets');
    },
  );
  it('creates through the real contract, redirects to the returned ID and confirms it in detail', async () => {
    signIn(auth, http, 'User');
    await visit('/tickets/new?status=Open&page=2');
    const page = f.debugElement.query(By.directive(TicketCreate)).componentInstance as TicketCreate;
    page.form.setValue({
      title: 'Test flow ticket',
      description: 'Test flow description',
      priority: 'High',
      category: 'Network',
    });
    page.submit();
    const post = http.expectOne('/api/tickets');
    expect(post.request.method).toBe('POST');
    expect(post.request.headers.has('Authorization')).toBe(true);
    await actionNavigation(() =>
      post.flush(
        ticketFixture({
          id: 101,
          title: 'Test flow ticket',
          createdByUserId: 3,
          priority: 'High',
          category: 'Network',
        }),
        {
          status: 201,
          statusText: 'Created',
          headers: { Location: 'https://example.invalid/not-a-navigation-target' },
        },
      ),
    );
    expect(router.url).toContain('/tickets/101');
    const detail = http.expectOne('/api/tickets/101');
    expect(detail.request.headers.has('Authorization')).toBe(true);
    detail.flush(
      ticketFixture({
        id: 101,
        title: 'Test flow ticket',
        createdByUserId: 3,
        priority: 'High',
        category: 'Network',
      }),
    );
    f.detectChanges();
    expect(f.nativeElement.querySelector('.feedback.success').textContent).toContain(
      '#101 creado correctamente',
    );
    expect(f.nativeElement.querySelector('h1').textContent).toContain('Test flow ticket');
    expect(f.nativeElement.querySelector('a.back-link').getAttribute('href')).toContain(
      'status=Open',
    );
    expect(f.nativeElement.querySelector('a.back-link').getAttribute('href')).toContain('page=2');
    http.expectNone('https://example.invalid/not-a-navigation-target');
  });
  it('opens the selected list ticket and restores the filtered list on return', async () => {
    signIn(auth, http);
    await visit('/tickets?status=Open&page=2');
    http
      .expectOne((r) => r.url === '/api/tickets')
      .flush(pageFixture({ page: 2, totalCount: 11, totalPages: 2 }));
    f.detectChanges();
    await actionNavigation(() => f.nativeElement.querySelector('button.ticket-title').click());
    expect(router.url).toContain('/tickets/41');
    http.expectOne('/api/tickets/41').flush(ticketFixture());
    f.detectChanges();
    expect(f.nativeElement.querySelector('.feedback.success')).toBeNull();
    await actionNavigation(() => f.nativeElement.querySelector('a.back-link').click());
    expect(router.url).toContain('status=Open');
    expect(router.url).toContain('page=2');
    const list = http.expectOne((r) => r.url === '/api/tickets');
    expect(list.request.params.get('page')).toBe('2');
    list.flush(pageFixture({ page: 2, totalCount: 11, totalPages: 2 }));
  });
  it('delegates a protected detail 401 to the unchanged auth interceptor', async () => {
    signIn(auth, http);
    await visit('/tickets/41');
    const request = http.expectOne('/api/tickets/41');
    await actionNavigation(() => request.flush({}, { status: 401, statusText: 'Unauthorized' }));
    expect(auth.user()).toBeNull();
    expect(router.url).toBe('/login');
    expect(f.nativeElement.querySelector('article')).toBeNull();
  });
});
