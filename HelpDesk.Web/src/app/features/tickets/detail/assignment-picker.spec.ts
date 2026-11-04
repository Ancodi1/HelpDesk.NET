import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { vi } from 'vitest';
import { AuthService } from '../../../core/auth/auth.service';
import { SessionUser } from '../../../core/auth/auth.models';
import { TicketManagement } from './ticket-management';
import { ticketFixture } from '../ticket.testing';
import { userFixture } from '../../admin/user.testing';

describe('Assignment selector using the administrative directory', () => {
  const user = signal<SessionUser | null>({ id: 3, email: 'admin@example.invalid', role: 'Admin' });
  let http: HttpTestingController;
  let f: ComponentFixture<TicketManagement>;
  let c: TicketManagement;
  const candidates = [
    userFixture(),
    userFixture({ id: 3, name: 'Fixture admin', role: 'Admin' }),
    userFixture({ id: 9, role: 'User' }),
  ];
  beforeEach(() => {
    user.set({ id: 3, email: 'admin@example.invalid', role: 'Admin' });
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { user } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    f = TestBed.createComponent(TicketManagement);
    c = f.componentInstance;
    f.componentRef.setInput('ticket', ticketFixture({ assignedToUserId: 7 }));
    f.detectChanges();
  });
  afterEach(() => {
    f.destroy();
    http.verify();
  });
  function open() {
    c.operation.setValue('assignment');
    c.prepare();
    f.detectChanges();
  }
  function select(text: string) {
    const element = f.nativeElement.querySelector('#edit-assignment') as HTMLSelectElement;
    element.value = Array.from(element.options).find((o) => o.textContent?.includes(text))!.value;
    element.dispatchEvent(new Event('change'));
    f.detectChanges();
  }
  it('loads only on assignment, keeps selection and offers Technician/Admin by name and email', () => {
    http.expectNone('/api/users');
    open();
    c.save();
    http.expectNone('/api/tickets/41/assignment');
    http.expectOne('/api/users').flush(candidates);
    f.detectChanges();
    const element = f.nativeElement.querySelector('#edit-assignment') as HTMLSelectElement;
    expect(element.tagName).toBe('SELECT');
    expect(element.options).toHaveLength(3);
    expect(element.selectedOptions[0].textContent).toContain('Fixture technician');
    expect(element.textContent).toContain('technician@example.invalid');
    expect(c.assignment.value).toBe(7);
    c.assignment.setValue(9);
    c.save();
    http.expectNone('/api/tickets/41/assignment');
  });
  it.each([
    ['Fixture admin', 3],
    ['Sin asignar', null],
  ] as const)('writes %s with the real ID/null exactly once', (label, id) => {
    open();
    http.expectOne('/api/users').flush(candidates);
    f.detectChanges();
    select(label);
    const updated = vi.fn();
    c.updated.subscribe(updated);
    c.save();
    c.save();
    const request = http.expectOne('/api/tickets/41/assignment');
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ assignedToUserId: id });
    request.flush(ticketFixture({ assignedToUserId: id }));
    expect(updated).toHaveBeenCalledWith(ticketFixture({ assignedToUserId: id }));
  });
  it('keeps an unavailable assignee visible without silently unassigning and allows explicit unassignment', () => {
    open();
    http.expectOne('/api/users').flush([]);
    f.detectChanges();
    expect(c.assignment.value).toBe(7);
    expect(f.nativeElement.textContent).toContain('Asignación actual no elegible');
    c.save();
    http.expectNone('/api/tickets/41/assignment');
    select('Sin asignar');
    c.save();
    http.expectOne('/api/tickets/41/assignment').flush(ticketFixture());
  });
  it.each([401, 403, 500])(
    'blocks assignment after directory error %s and offers a read retry',
    (status) => {
      open();
      http
        .expectOne('/api/users')
        .flush({ details: 'provider secret' }, { status, statusText: 'Error' });
      f.detectChanges();
      c.save();
      http.expectNone('/api/tickets/41/assignment');
      expect(f.nativeElement.textContent).not.toContain('provider secret');
      c.assignmentPicker()!.reload();
      http.expectOne('/api/users').flush(candidates);
      f.detectChanges();
      expect(c.assignmentPicker()!.selectionAvailable()).toBe(true);
    },
  );
  it.each([400, 401, 403, 404, 409])(
    'honors server denial %s after stale eligibility without retrying a write',
    (status) => {
      open();
      http.expectOne('/api/users').flush(candidates);
      f.detectChanges();
      c.save();
      http.expectOne('/api/tickets/41/assignment').flush({}, { status, statusText: 'Error' });
      expect(c.reviewRequired()).toBe(true);
      c.save();
      http.expectNone('/api/tickets/41/assignment');
    },
  );
  it.each(['User', 'Technician'] as const)('does not request the directory for %s', (role) => {
    user.set({ id: 7, email: 'fixture@example.invalid', role });
    open();
    http.expectNone('/api/users');
    // prepare() legitimately selects status for Technician; explicitly tamper with
    // the operation to verify that assignment still cannot be submitted.
    c.operation.setValue('assignment');
    c.save();
    http.expectNone('/api/tickets/41/assignment');
  });
  it('cancels the directory request when the operation is changed', () => {
    open();
    const read = http.expectOne('/api/users');
    c.operation.setValue('text');
    f.detectChanges();
    expect(read.cancelled).toBe(true);
  });
});
