import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TicketActivities } from './ticket-activities';
import { activityFixture as item, activityPageFixture as page } from './activity.testing';

describe('Ticket activity history', () => {
  let http: HttpTestingController;
  let f: ComponentFixture<TicketActivities>;
  let c: TicketActivities;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    f = TestBed.createComponent(TicketActivities);
    c = f.componentInstance;
    f.componentRef.setInput('ticketId', 41);
    f.detectChanges();
  });
  afterEach(() => {
    f.destroy();
    http.verify();
  });
  const read = () => http.expectOne('/api/tickets/41/activities?pageSize=20');
  it('loads on demand and renders safe historical names, dates and assignment values', () => {
    http.expectNone((r) => r.url.includes('/activities'));
    (f.nativeElement.querySelector('button') as HTMLButtonElement).click();
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Cargando historial');
    read().flush(
      page({
        items: [
          item({
            actorName: '<b>Actor</b>',
            field: 'AssignedToUserId',
            oldValue: null,
            newValue: '12',
            newAssigneeName: '<script>Historical name</script>',
          }),
        ],
      }),
    );
    f.detectChanges();
    expect(f.nativeElement.querySelector('script, b')).toBeNull();
    expect(f.nativeElement.textContent).toContain('<b>Actor</b>');
    expect(f.nativeElement.textContent).toContain(
      'Responsable: Sin asignar → <script>Historical name</script>',
    );
    expect(f.nativeElement.querySelector('time').getAttribute('datetime')).toBe(
      '2026-11-04T09:00:00Z',
    );
    c.load();
    read().flush(page({ items: [] }));
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('No hay actividad registrada');
  });
  it('appends older pages once and refreshes from the beginning on a modification', () => {
    c.open();
    read().flush(
      page({
        items: Array.from({ length: 20 }, (_, i) => item({ id: 100 - i })),
        hasMore: true,
        nextBeforeId: 81,
      }),
    );
    c.load(true);
    c.load(true);
    http
      .expectOne('/api/tickets/41/activities?pageSize=20&beforeId=81')
      .flush(page({ items: [item({ id: 80 })] }));
    expect(c.items()).toHaveLength(21);
    expect(c.hasMore()).toBe(false);
    f.componentRef.setInput('revision', 1);
    f.detectChanges();
    read().flush(page({ items: [item({ id: 101 })] }));
    expect(c.items().map((a) => a.id)).toEqual([101]);
  });
  it.each([401, 403, 404, 500, 0])(
    'clears stale activity after error %s and offers a safe retry',
    (status) => {
      c.open();
      read().flush(page());
      c.load();
      const request = read();
      if (status === 0) request.error(new ProgressEvent('error'));
      else request.flush({ error: 'provider secret' }, { status, statusText: 'Error' });
      f.detectChanges();
      expect(c.items()).toEqual([]);
      expect(f.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
      expect(f.nativeElement.textContent).not.toContain('provider secret');
      c.load();
      read().flush(page());
      expect(c.loaded()).toBe(true);
    },
  );
  it('cancels obsolete reads when changing tickets, refreshing and destroying', () => {
    c.open();
    const old = read();
    f.componentRef.setInput('ticketId', 42);
    f.detectChanges();
    expect(old.cancelled).toBe(true);
    expect(c.items()).toEqual([]);
    expect(c.opened()).toBe(false);
    c.open();
    const first = http.expectOne('/api/tickets/42/activities?pageSize=20');
    f.componentRef.setInput('revision', 1);
    f.detectChanges();
    expect(first.cancelled).toBe(true);
    const second = http.expectOne('/api/tickets/42/activities?pageSize=20');
    f.destroy();
    expect(second.cancelled).toBe(true);
  });
});
