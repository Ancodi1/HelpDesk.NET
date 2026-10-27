import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { vi } from 'vitest';
import { TicketCreate } from './ticket-create';
import { ticketFixture } from '../ticket.testing';

const valid = {
  title: 'Test fixture title',
  description: '  First line\nSecond line  ',
  priority: 'Medium' as const,
  category: 'Other' as const,
};
describe('Ticket creation form', () => {
  let f: ComponentFixture<TicketCreate>;
  let http: HttpTestingController;
  let router: Router;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TicketCreate],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    f = TestBed.createComponent(TicketCreate);
    f.detectChanges();
  });
  afterEach(() => {
    f.destroy();
    http.verify();
    vi.useRealTimers();
  });
  it('defaults to Medium and Other and exposes no server-owned input', () => {
    expect(f.componentInstance.form.getRawValue()).toEqual({
      title: '',
      description: '',
      priority: 'Medium',
      category: 'Other',
    });
    expect(Object.keys(f.componentInstance.form.controls).sort()).toEqual([
      'category',
      'description',
      'priority',
      'title',
    ]);
    expect(f.nativeElement.querySelector('label[for="ticket-title"]')).not.toBeNull();
  });
  it('shows required errors without sending an invalid request', () => {
    f.componentInstance.submit();
    f.detectChanges();
    expect(f.nativeElement.querySelectorAll('.field-error').length).toBe(2);
    http.expectNone('/api/tickets');
  });
  it.each([' ', '\t\n', '\u0085', '\u2003'])(
    'rejects .NET whitespace-only title and description',
    (value) => {
      f.componentInstance.form.setValue({ ...valid, title: value, description: value });
      expect(f.componentInstance.form.invalid).toBe(true);
      f.componentInstance.submit();
      http.expectNone('/api/tickets');
    },
  );
  it('enforces title length in UTF-16 without limiting description or trimming text', () => {
    const form = f.componentInstance.form;
    form.setValue({ ...valid, title: '😀'.repeat(50), description: 'd'.repeat(5000) });
    expect(form.valid).toBe(true);
    form.controls.title.setValue('😀'.repeat(51));
    expect(form.controls.title.invalid).toBe(true);
    form.controls.title.setValue('t'.repeat(100));
    expect(form.valid).toBe(true);
    form.controls.title.setValue('t'.repeat(101));
    expect(form.invalid).toBe(true);
  });
  it('rejects invalid enum values before submission', () => {
    const form = f.componentInstance.form;
    form.setValue({ ...valid, priority: 'Unknown' as never });
    expect(form.invalid).toBe(true);
    form.setValue({ ...valid, category: 'Unknown' as never });
    expect(form.invalid).toBe(true);
    f.componentInstance.submit();
    http.expectNone('/api/tickets');
  });
  it('submits once, confirms creation and uses the response ID for navigation', () => {
    const page = f.componentInstance;
    page.form.setValue(valid);
    page.submit();
    page.submit();
    f.detectChanges();
    expect(f.nativeElement.querySelector('button[type="submit"]').disabled).toBe(true);
    expect(f.nativeElement.querySelector('fieldset').disabled).toBe(true);
    const request = http.expectOne('/api/tickets');
    expect(request.request.body).toEqual(valid);
    request.flush(ticketFixture(), { status: 201, statusText: 'Created' });
    f.detectChanges();
    expect(router.navigate).toHaveBeenCalledWith(['/tickets', 41], {
      queryParams: {},
      state: { createdTicketId: 41 },
    });
    expect(f.nativeElement.querySelector('[role="status"]').textContent).toContain(
      'creado correctamente',
    );
    expect(f.nativeElement.querySelector('a[href="/tickets/41"]')).not.toBeNull();
    page.submit();
    http.expectNone('/api/tickets');
  });
  it('keeps a safe detail link if navigation fails after successful creation', async () => {
    vi.mocked(router.navigate).mockRejectedValueOnce(new Error('navigation failed'));
    const page = f.componentInstance;
    page.form.setValue(valid);
    page.submit();
    http.expectOne('/api/tickets').flush(ticketFixture(), { status: 201, statusText: 'Created' });
    await Promise.resolve();
    f.detectChanges();
    expect(f.nativeElement.querySelector('a[href="/tickets/41"]')).not.toBeNull();
    expect(f.nativeElement.querySelector('button').disabled).toBe(true);
  });
  it.each([400, 401, 403, 404, 409, 500])(
    'handles create status %s without echoing server data and preserves the draft',
    (status) => {
      const page = f.componentInstance;
      page.form.setValue(valid);
      page.submit();
      http
        .expectOne('/api/tickets')
        .flush({ error: 'do-not-echo-provider' }, { status, statusText: 'Error' });
      f.detectChanges();
      expect(f.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
      expect(f.nativeElement.textContent).not.toContain('do-not-echo-provider');
      expect(page.pending()).toBe(false);
      expect(page.form.getRawValue()).toEqual(valid);
      expect(router.navigate).not.toHaveBeenCalled();
    },
  );
  it('explains that a network failure does not guarantee the ticket was not created', () => {
    f.componentInstance.form.setValue(valid);
    f.componentInstance.submit();
    http.expectOne('/api/tickets').error(new ProgressEvent('error'));
    f.detectChanges();
    expect(f.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'podría haberse creado',
    );
    http.expectNone('/api/tickets');
  });
  it('explains an unreadable creation response without retrying', () => {
    f.componentInstance.form.setValue(valid);
    f.componentInstance.submit();
    http.expectOne('/api/tickets').flush({}, { status: 201, statusText: 'Created' });
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Revisa el listado');
    http.expectNone('/api/tickets');
  });
  it('cancels a pending request on leaving the page', () => {
    f.componentInstance.form.setValue(valid);
    f.componentInstance.submit();
    const request = http.expectOne('/api/tickets');
    f.destroy();
    expect(request.cancelled).toBe(true);
    expect(router.navigate).not.toHaveBeenCalled();
  });
  it('warns about an uncertain timeout instead of automatically retrying', () => {
    vi.useFakeTimers();
    f.componentInstance.form.setValue(valid);
    f.componentInstance.submit();
    const request = http.expectOne('/api/tickets');
    vi.advanceTimersByTime(15001);
    f.detectChanges();
    expect(request.cancelled).toBe(true);
    expect(f.nativeElement.textContent).toContain('podría haberse creado');
    http.expectNone('/api/tickets');
  });
});
