import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Params, Router } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { vi } from 'vitest';
import { Tickets } from './tickets';
import { pageFixture, ticketFixture } from './ticket.testing';
import { AuthService } from '../../core/auth/auth.service';
import { signIn } from '../../core/auth/auth.testing';

describe('Ticket listing interactions', () => {
  let fixture: ComponentFixture<Tickets>;
  let http: HttpTestingController;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let navigate: ReturnType<typeof vi.fn>;
  const request = () => http.expectOne((r) => r.url === '/api/tickets');
  const settle = async () => {
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();
  };
  beforeEach(() => {
    vi.useFakeTimers();
    params = new BehaviorSubject(convertToParamMap({}));
    navigate = vi.fn((_commands: unknown, options: { queryParams: Params }) => {
      queueMicrotask(() =>
        params.next(
          convertToParamMap(
            Object.fromEntries(
              Object.entries(options.queryParams).map(([key, value]) => [key, String(value)]),
            ),
          ),
        ),
      );
      return Promise.resolve(true);
    });
    TestBed.configureTestingModule({
      imports: [Tickets],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { queryParamMap: params.asObservable() } },
        { provide: Router, useValue: { navigate, navigateByUrl: vi.fn().mockResolvedValue(true) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    signIn(TestBed.inject(AuthService), http, 'User');
    fixture = TestBed.createComponent(Tickets);
    fixture.detectChanges();
  });
  afterEach(() => {
    fixture.destroy();
    TestBed.inject(AuthService).logout();
    http.verify();
    vi.useRealTimers();
  });
  it('announces loading and renders only returned tickets and totals', () => {
    expect(fixture.nativeElement.textContent).toContain('Cargando incidencias');
    request().flush(pageFixture());
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Test fixture incident');
    expect(fixture.nativeElement.textContent).toContain('1 incidencia encontrada');
    expect(fixture.nativeElement.textContent).toContain('Sin asignar');
    expect(fixture.nativeElement.textContent).toContain('Incidencias creadas por ti.');
    expect(fixture.nativeElement.querySelectorAll('tbody tr').length).toBe(1);
  });
  it('cancels immediately on typing, debounces search and preserves literal text in the URL', async () => {
    const old = request();
    fixture.componentInstance.form.controls.search.setValue('first');
    expect(old.cancelled).toBe(true);
    vi.advanceTimersByTime(200);
    fixture.componentInstance.form.controls.search.setValue('  %_final  ');
    vi.advanceTimersByTime(349);
    http.expectNone((r) => r.url === '/api/tickets');
    vi.advanceTimersByTime(1);
    await vi.runAllTicks();
    await settle();
    const next = request();
    expect(next.request.params.get('search')).toBe('  %_final  ');
    expect(next.request.params.get('page')).toBe('1');
    expect(params.value.get('search')).toBe('  %_final  ');
    next.flush(pageFixture());
  });
  it('applies selects immediately, cancels pending debounce and resets pagination', async () => {
    request().flush(pageFixture());
    params.next(convertToParamMap({ page: '3' }));
    request().flush(pageFixture({ page: 3, totalCount: 25, totalPages: 3 }));
    const form = fixture.componentInstance.form;
    form.controls.search.setValue('pending');
    form.controls.status.setValue('InProgress');
    await vi.runAllTicks();
    await settle();
    const next = request();
    expect(next.request.params.get('status')).toBe('InProgress');
    expect(next.request.params.get('search')).toBe('pending');
    expect(next.request.params.get('page')).toBe('1');
    next.flush(pageFixture());
    vi.advanceTimersByTime(400);
    http.expectNone((r) => r.url === '/api/tickets');
  });
  it('combines priority, category and page size using the real parameter names', async () => {
    request().flush(pageFixture());
    fixture.componentInstance.form.controls.priority.setValue('Critical');
    await vi.runAllTicks();
    await settle();
    request().flush(pageFixture());
    fixture.componentInstance.form.controls.category.setValue('Access');
    await vi.runAllTicks();
    await settle();
    request().flush(pageFixture());
    fixture.componentInstance.form.controls.pageSize.setValue(25);
    await vi.runAllTicks();
    await settle();
    const next = request();
    expect(next.request.params.get('priority')).toBe('Critical');
    expect(next.request.params.get('category')).toBe('Access');
    expect(next.request.params.get('pageSize')).toBe('25');
    next.flush(pageFixture({ pageSize: 25 }));
  });
  it('preserves filters while paging and shows API pagination metadata', async () => {
    request().flush(pageFixture());
    params.next(convertToParamMap({ status: 'Open' }));
    request().flush(pageFixture({ totalCount: 11, totalPages: 2 }));
    fixture.detectChanges();
    const buttons = [
      ...fixture.nativeElement.querySelectorAll('.pagination button'),
    ] as HTMLButtonElement[];
    expect(buttons[0].disabled).toBe(true);
    expect(buttons[1].disabled).toBe(false);
    buttons[1].click();
    await vi.runAllTicks();
    await settle();
    const next = request();
    expect(next.request.params.get('page')).toBe('2');
    expect(next.request.params.get('status')).toBe('Open');
    next.flush(pageFixture({ page: 2, totalCount: 11, totalPages: 2 }));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Página 2 de 2');
  });
  it('restores URL filters and cancels debounce when history changes', async () => {
    request().flush(pageFixture());
    fixture.componentInstance.form.controls.search.setValue('discarded');
    params.next(
      convertToParamMap({ search: 'history', priority: 'Low', page: '2', pageSize: '7' }),
    );
    const next = request();
    expect(next.request.params.get('search')).toBe('history');
    expect(fixture.componentInstance.form.controls.search.value).toBe('history');
    next.flush(pageFixture({ page: 2, pageSize: 7, totalCount: 8, totalPages: 2 }));
    vi.advanceTimersByTime(400);
    await settle();
    http.expectNone((r) => r.url === '/api/tickets');
    expect(fixture.nativeElement.querySelector('#ticket-page-size').textContent).toContain('7');
  });
  it('normalizes invalid URL input before sending a query', async () => {
    request().flush(pageFixture());
    params.next(convertToParamMap({ page: '-1', status: 'Unknown', sort: 'title' }));
    await vi.runAllTicks();
    await settle();
    const next = request();
    expect(next.request.params.keys().sort()).toEqual(['page', 'pageSize']);
    expect(params.value.keys).toEqual([]);
    next.flush(pageFixture());
  });
  it('retries a failed query without showing provider details or stale data', () => {
    request().flush({ error: 'do-not-display' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No se pudo cargar');
    expect(fixture.nativeElement.textContent).not.toContain('do-not-display');
    fixture.nativeElement.querySelector('.error button').click();
    request().flush(pageFixture());
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Test fixture incident');
  });
  it.each([0, 400, 401, 403])('shows a safe error for status %s', (status) => {
    const pending = request();
    if (status === 0) pending.error(new ProgressEvent('error'));
    else pending.flush({}, { status, statusText: 'Error' });
    fixture.detectChanges();
    const text = fixture.nativeElement.querySelector('[role="alert"]').textContent;
    expect(text).toContain(
      status === 0
        ? 'conectar'
        : status === 400
          ? 'consulta'
          : status === 401
            ? 'sesión'
            : 'permiso',
    );
  });
  it('shows an empty list without inventing metrics', () => {
    request().flush(pageFixture({ items: [], totalCount: 0, totalPages: 0 }));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No hay incidencias disponibles');
    expect(fixture.nativeElement.querySelector('table')).toBeNull();
    expect(fixture.nativeElement.querySelector('.pagination')).toBeNull();
  });
  it('shows no matches and clears filters', async () => {
    request().flush(pageFixture());
    params.next(convertToParamMap({ search: 'no matches' }));
    request().flush(pageFixture({ items: [], totalCount: 0, totalPages: 0 }));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No hay coincidencias');
    fixture.componentInstance.clearFilters();
    await vi.runAllTicks();
    await settle();
    const next = request();
    expect(next.request.params.has('search')).toBe(false);
    next.flush(pageFixture());
  });
  it('can clear a pending search even when the resulting URL is unchanged', () => {
    const old = request();
    fixture.componentInstance.form.controls.search.setValue('pending');
    expect(old.cancelled).toBe(true);
    fixture.componentInstance.clearFilters();
    const next = request();
    expect(fixture.componentInstance.form.controls.search.value).toBe('');
    next.flush(pageFixture());
  });
  it('offers a first-page recovery for an out-of-range page', async () => {
    request().flush(pageFixture());
    params.next(convertToParamMap({ page: '9' }));
    request().flush(pageFixture({ page: 9, items: [] }));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No hay incidencias en esta página');
    fixture.componentInstance.goToPage(1);
    await vi.runAllTicks();
    await settle();
    const next = request();
    expect(next.request.params.get('page')).toBe('1');
    next.flush(pageFixture());
  });
  it('cancels a superseded HTTP response and cancels on destruction', () => {
    const old = request();
    params.next(convertToParamMap({ status: 'Closed' }));
    expect(old.cancelled).toBe(true);
    const pending = request();
    fixture.destroy();
    expect(pending.cancelled).toBe(true);
  });
  it('cancels the pending search timer on destruction', async () => {
    request().flush(pageFixture());
    fixture.componentInstance.form.controls.search.setValue('discarded');
    fixture.destroy();
    vi.advanceTimersByTime(400);
    await settle();
    http.expectNone((r) => r.url === '/api/tickets');
  });
});
