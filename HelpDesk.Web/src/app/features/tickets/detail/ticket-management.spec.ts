import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { vi } from 'vitest';
import { AuthService } from '../../../core/auth/auth.service';
import { SessionUser } from '../../../core/auth/auth.models';
import { TicketManagement, nextStatuses } from './ticket-management';
import { ticketFixture } from '../ticket.testing';

describe('Ticket management', () => {
  const user = signal<SessionUser | null>(null);
  let http: HttpTestingController;
  beforeEach(() => {
    user.set({ id: 7, email: 'fixture@example.test', role: 'Admin' });
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { user } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function setup() {
    const f = TestBed.createComponent(TicketManagement);
    f.componentRef.setInput('ticket', ticketFixture({ assignedToUserId: 7 }));
    f.detectChanges();
    return f;
  }
  it.each(['User', 'Technician', 'Admin'] as const)('respects %s permissions', (role) => {
    user.update((u) => ({ ...u!, role }));
    const f = setup();
    const c = f.componentInstance;
    expect(c.admin()).toBe(role === 'Admin');
    expect(c.canStatus()).toBe(role !== 'User');
    if (role !== 'Admin') {
      c.operation.setValue('text');
      c.save();
      http.expectNone('/api/tickets/41');
    }
    f.componentRef.setInput('ticket', ticketFixture({ assignedToUserId: 8 }));
    f.detectChanges();
    expect(c.canStatus()).toBe(role === 'Admin');
    f.destroy();
  });
  it('validates text, IDs and impossible transitions without HTTP', () => {
    const f = setup();
    const c = f.componentInstance;
    for (const title of ['', '\u0085', 'x'.repeat(101)]) {
      c.text.controls.title.setValue(title);
      c.save();
      expect(c.text.invalid).toBe(true);
    }
    c.operation.setValue('assignment');
    c.assignment.setValue(1.5);
    c.save();
    expect(c.assignment.invalid).toBe(true);
    c.operation.setValue('status');
    c.status.setValue('Closed');
    c.save();
    http.expectNone('/api/tickets/41/status');
    expect(nextStatuses.Resolved).toEqual(['Open', 'InProgress', 'Closed']);
    f.destroy();
  });
  it('saves once, publishes the server ticket and confirms success', () => {
    const f = setup();
    const c = f.componentInstance;
    const updated = vi.fn();
    c.updated.subscribe(updated);
    c.text.setValue({ title: 'Changed', description: 'Description' });
    c.save();
    c.save();
    expect(c.pending()).toBe(true);
    f.detectChanges();
    expect(f.nativeElement.querySelector('fieldset').disabled).toBe(true);
    const r = http.expectOne('/api/tickets/41');
    expect(r.request.method).toBe('PUT');
    expect(r.request.body).toEqual({ title: 'Changed', description: 'Description' });
    r.flush(ticketFixture({ title: 'Changed' }));
    expect(updated).toHaveBeenCalledWith(ticketFixture({ title: 'Changed' }));
    expect(c.success()).toBe(true);
    expect(c.pending()).toBe(false);
    f.destroy();
  });
  it.each([400, 401, 403, 404, 409, 0])('handles HTTP %s without retry', (code) => {
    const f = setup();
    const c = f.componentInstance;
    c.operation.setValue('status');
    c.status.setValue('InProgress');
    c.save();
    const r = http.expectOne('/api/tickets/41/status');
    if (code === 0) r.error(new ProgressEvent('error'));
    else r.flush({}, { status: code, statusText: 'Error' });
    expect(c.pending()).toBe(false);
    expect(c.success()).toBe(false);
    expect(c.message()).not.toBe('');
    expect(c.reviewRequired()).toBe(true);
    c.save();
    http.expectNone('/api/tickets/41/status');
    f.destroy();
  });
});
