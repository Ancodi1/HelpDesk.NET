import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Admin } from './admin';
import { userFixture } from './user.testing';
import { pageFixture } from '../tickets/ticket.testing';
import { ticketStatuses } from '../tickets/ticket.models';
import { AuthService } from '../../core/auth/auth.service';
import { signIn } from '../../core/auth/auth.testing';

describe('Admin directory and overview', () => {
  let http: HttpTestingController;
  let f: ComponentFixture<Admin>;
  let c: Admin;
  const users = [
    userFixture({ name: '<b>Fixture technician</b>' }),
    userFixture({ id: 8, name: 'Other user', role: 'User' }),
  ];
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    signIn(TestBed.inject(AuthService), http);
    f = TestBed.createComponent(Admin);
    c = f.componentInstance;
    f.detectChanges();
  });
  afterEach(() => {
    f.destroy();
    TestBed.inject(AuthService).logout();
    http.verify();
  });
  function flushSummary() {
    for (const [index, status] of ticketStatuses.entries()) {
      const request = http.expectOne(
        (r) => r.url === '/api/tickets' && r.params.get('status') === status,
      );
      expect(request.request.params.get('pageSize')).toBe('1');
      expect(request.request.params.has('assignedToMe')).toBe(false);
      request.flush(pageFixture({ pageSize: 1, totalCount: 200 + index, totalPages: 200 + index }));
    }
  }
  function ready() {
    flushSummary();
    http.expectOne('/api/users').flush(users);
    f.detectChanges();
  }
  it('shows server totals beyond one page and links to filtered existing tickets', () => {
    expect(f.nativeElement.textContent).toContain('Cargando usuarios');
    ready();
    expect(f.nativeElement.querySelector('.stats').textContent).toContain('200');
    expect(f.nativeElement.querySelector('.stats a').getAttribute('href')).toBe(
      '/tickets?status=Open',
    );
    expect(f.nativeElement.querySelector('.identity b')).toBeNull();
    expect(f.nativeElement.querySelectorAll('.users th[scope=col]')).toHaveLength(4);
    expect(f.nativeElement.querySelectorAll('.users tbody tr')).toHaveLength(users.length);
    expect(f.nativeElement.textContent).toContain('<b>Fixture technician</b>');
    c.search.setValue('OTHER');
    c.roleFilter.setValue('User');
    f.detectChanges();
    expect(c.filtered().map((u) => u.id)).toEqual([8]);
    c.search.setValue('not found');
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('No hay coincidencias');
    expect(f.nativeElement.querySelector('[type=password]')).toBeNull();
  });
  it('shows an empty directory without losing overview', () => {
    flushSummary();
    http.expectOne('/api/users').flush([]);
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('No hay usuarios disponibles');
    expect(f.nativeElement.querySelectorAll('.stats a')).toHaveLength(4);
  });
  it('retries directory and overview independently after read errors', () => {
    const requests = http.match((r) => r.url === '/api/tickets');
    for (const r of requests)
      if (!r.cancelled) r.flush({}, { status: 403, statusText: 'Forbidden' });
    http
      .expectOne('/api/users')
      .flush({ message: 'provider secret' }, { status: 403, statusText: 'Forbidden' });
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('No tienes permiso');
    expect(f.nativeElement.textContent).not.toContain('provider secret');
    c.reloadUsers();
    http.expectOne('/api/users').flush(users);
    http.expectNone((r) => r.url === '/api/tickets');
    c.reloadSummary();
    flushSummary();
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(true);
  });
  it('validates Unicode whitespace, UTF-16 length, email and roles before writing', () => {
    ready();
    c.edit(users[0]);
    for (const name of ['', '\u0085', '😀'.repeat(51)]) {
      c.form.controls.name.setValue(name);
      c.save();
      expect(c.form.controls.name.invalid).toBe(true);
    }
    c.form.controls.name.setValue('Valid');
    for (const email of [
      'invalid',
      'name@localhost',
      'name@bad-.test',
      'a'.repeat(255) + '@example.test',
    ]) {
      c.form.controls.email.setValue(email);
      c.save();
      expect(c.form.controls.email.invalid).toBe(true);
    }
    c.form.controls.email.setValue('valid@example.test');
    c.form.controls.role.setValue('Other' as never);
    c.save();
    expect(c.form.invalid).toBe(true);
    http.expectNone((r) => r.method === 'PUT');
  });
  it('confirms changed role, resets confirmation when editing, and blocks duplicate writes', () => {
    ready();
    c.edit(users[0]);
    c.form.controls.role.setValue('Admin');
    c.save();
    expect(c.confirming()).toBe(true);
    http.expectNone((r) => r.method === 'PUT');
    c.form.controls.name.setValue('Changed');
    expect(c.confirming()).toBe(false);
    c.save();
    c.save(true);
    c.save(true);
    f.detectChanges();
    expect(f.nativeElement.querySelector('fieldset').disabled).toBe(true);
    const request = http.expectOne('/api/users/7');
    expect(request.request.body).toEqual({ name: 'Changed', email: users[0].email, role: 'Admin' });
    request.flush(userFixture({ role: 'Admin', name: 'Confirmed server name' }));
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Confirmed server name');
    expect(f.nativeElement.textContent).toContain('Usuario actualizado correctamente');
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(true);
  });
  it('allows cancelling role confirmation without a write', () => {
    ready();
    c.edit(users[0]);
    c.form.controls.role.setValue('User');
    c.save();
    c.cancel();
    expect(c.selected()).toBeNull();
    expect(c.confirming()).toBe(false);
    http.expectNone((r) => r.method === 'PUT');
  });
  it.each([400, 401, 403, 404, 409, 500, 0])(
    'requires reload after write error %s and never leaks bodies',
    (status) => {
      ready();
      c.edit(users[0]);
      c.save();
      const request = http.expectOne('/api/users/7');
      if (status === 0) request.error(new ProgressEvent('error'));
      else request.flush({ message: 'provider secret' }, { status, statusText: 'Error' });
      f.detectChanges();
      expect(c.reviewRequired()).toBe(true);
      expect(f.nativeElement.textContent).not.toContain('provider secret');
      c.save();
      c.edit(users[1]);
      http.expectNone((r) => r.method === 'PUT');
      expect(c.selected()?.id).toBe(7);
      c.reloadUsers();
      http.expectOne('/api/users').flush(users);
      expect(c.reviewRequired()).toBe(false);
      expect(c.selected()).toBeNull();
    },
  );
  it('treats malformed success as uncertain and cancels pending reads on destruction', () => {
    ready();
    c.edit(users[0]);
    c.save();
    http.expectOne('/api/users/7').flush(userFixture({ id: 999 }));
    expect(c.reviewRequired()).toBe(true);
    c.reloadUsers();
    const read = http.expectOne('/api/users');
    f.destroy();
    expect(read.cancelled).toBe(true);
  });
});
