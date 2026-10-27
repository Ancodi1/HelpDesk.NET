import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter, Router } from '@angular/router';
import { App } from '../../../app';
import { TicketDetail } from './ticket-detail';
import { ticketFixture } from '../ticket.testing';

describe('Ticket detail read-only view', () => {
  let f: ComponentFixture<App>;
  let http: HttpTestingController;
  let router: Router;
  const page = () =>
    f.debugElement.query(By.directive(TicketDetail)).componentInstance as TicketDetail;
  const visit = async (url: string) => {
    await router.navigateByUrl(url);
    f.detectChanges();
  };
  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'tickets/:id', component: TicketDetail }]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    f = TestBed.createComponent(App);
    await visit('/tickets/41');
  });
  afterEach(() => {
    f.destroy();
    http.verify();
  });
  it('announces loading and displays only the real detail fields', () => {
    expect(f.nativeElement.textContent).toContain('Cargando incidencia');
    http
      .expectOne('/api/tickets/41')
      .flush(
        ticketFixture({
          description: 'First line\nSecond line',
          createdByUserId: 3,
          assignedToUserId: 7,
        }),
      );
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Test fixture incident');
    expect(f.nativeElement.textContent).toContain('First line\nSecond line');
    expect(f.nativeElement.textContent).toContain('ID 3');
    expect(f.nativeElement.textContent).toContain('ID 7');
    expect(f.nativeElement.textContent).toContain('Abierta');
    expect(f.nativeElement.querySelector('time').getAttribute('datetime')).toBe(
      '2026-10-27T10:00:00Z',
    );
    expect(f.nativeElement.querySelector('[role="status"].success')).toBeNull();
  });
  it('handles nullable relations without inventing user names', () => {
    http.expectOne('/api/tickets/41').flush(ticketFixture());
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Sin creador registrado');
    expect(f.nativeElement.textContent).toContain('Sin asignar');
  });
  it('renders untrusted title and description as text', () => {
    http
      .expectOne('/api/tickets/41')
      .flush(ticketFixture({ title: '<img src=x>', description: '<script>test</script>' }));
    f.detectChanges();
    expect(f.nativeElement.querySelector('img')).toBeNull();
    expect(f.nativeElement.querySelector('script')).toBeNull();
    expect(f.nativeElement.textContent).toContain('<img src=x>');
  });
  it('uses the same safe message for a nonexistent or invisible ticket', () => {
    http.expectOne('/api/tickets/41').flush(null, { status: 404, statusText: 'Not Found' });
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('no existe o no está disponible para tu cuenta');
    expect(f.nativeElement.querySelector('article')).toBeNull();
  });
  it.each([400, 401, 403, 500])(
    'handles detail status %s without leaking response data',
    (status) => {
      http
        .expectOne('/api/tickets/41')
        .flush({ error: 'do-not-echo-provider' }, { status, statusText: 'Error' });
      f.detectChanges();
      expect(f.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
      expect(f.nativeElement.textContent).not.toContain('do-not-echo-provider');
      expect(f.nativeElement.querySelector('article')).toBeNull();
    },
  );
  it('can retry a connection error', () => {
    http.expectOne('/api/tickets/41').error(new ProgressEvent('error'));
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('conectar');
    f.nativeElement.querySelector('button').click();
    http.expectOne('/api/tickets/41').flush(ticketFixture());
    f.detectChanges();
    expect(f.nativeElement.querySelector('article')).not.toBeNull();
  });
  it('does not show a mismatched response ticket', () => {
    http.expectOne('/api/tickets/41').flush(ticketFixture({ id: 42 }));
    f.detectChanges();
    expect(f.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
    expect(f.nativeElement.querySelector('article')).toBeNull();
  });
  it('cancels an obsolete ID request and then cancels on destruction', async () => {
    const old = http.expectOne('/api/tickets/41');
    await visit('/tickets/42');
    expect(old.cancelled).toBe(true);
    const request = http.expectOne('/api/tickets/42');
    f.destroy();
    expect(request.cancelled).toBe(true);
  });
  it.each(['abc', '0', '-1', '1.5', '2147483648'])(
    'rejects invalid route IDs without querying the API: %s',
    async (id) => {
      http.expectOne('/api/tickets/41').flush(ticketFixture());
      await visit('/tickets/' + id);
      expect(f.nativeElement.textContent).toContain('No encontramos este ticket');
      http.expectNone((r) => r.url.startsWith('/api/tickets/'));
    },
  );
  it('preserves list query parameters in the return link', async () => {
    http.expectOne('/api/tickets/41').flush(ticketFixture());
    await visit('/tickets/41?search=printer&status=Open&page=2');
    expect(f.nativeElement.querySelector('a.back-link').getAttribute('href')).toContain(
      'search=printer',
    );
    expect(f.nativeElement.querySelector('a.back-link').getAttribute('href')).toContain('page=2');
    http.expectNone('/api/tickets/41');
  });
  it('removes previous detail data while a new ticket is loading or denied', async () => {
    http.expectOne('/api/tickets/41').flush(ticketFixture());
    f.detectChanges();
    await visit('/tickets/42');
    expect(f.nativeElement.textContent).not.toContain('Test fixture incident');
    http.expectOne('/api/tickets/42').flush({}, { status: 403, statusText: 'Forbidden' });
    f.detectChanges();
    expect(f.nativeElement.textContent).not.toContain('Test fixture incident');
  });
});
