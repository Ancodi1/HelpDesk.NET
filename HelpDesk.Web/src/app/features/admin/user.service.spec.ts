import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { UserService } from './user.service';
import { userFixture } from './user.testing';

describe('Administrative user contracts', () => {
  let http: HttpTestingController;
  let service: UserService;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(UserService);
  });
  afterEach(() => http.verify());
  it('reads the whole public directory without pagination and discards incidental fields', () => {
    const next = vi.fn();
    service.list().subscribe(next);
    const request = http.expectOne('/api/users');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.keys()).toEqual([]);
    expect(request.request.cache).toBe('no-store');
    request.flush([{ ...userFixture(), extraProviderField: 'not-for-the-ui' }]);
    expect(next).toHaveBeenCalledWith([userFixture()]);
  });
  it.each([
    {},
    [userFixture({ id: 0 })],
    [userFixture({ role: 'Other' as never })],
    [userFixture({ createdAt: 'invalid' })],
    [userFixture(), userFixture()],
  ])('rejects malformed directory %j', (value) => {
    const error = vi.fn();
    service.list().subscribe({ error });
    http.expectOne('/api/users').flush(value);
    expect(error).toHaveBeenCalledOnce();
  });
  it('writes only the update DTO, normalizing email without changing name', () => {
    const next = vi.fn();
    service
      .update(7, { ...userFixture(), name: '  Fixture  ', email: ' TECH@Example.Invalid ' })
      .subscribe(next);
    const request = http.expectOne('/api/users/7');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      name: '  Fixture  ',
      email: 'tech@example.invalid',
      role: 'Technician',
    });
    request.flush(userFixture({ name: 'Server name' }));
    expect(next).toHaveBeenCalledWith(userFixture({ name: 'Server name' }));
  });
  it('rejects an update response belonging to another user', () => {
    const error = vi.fn();
    service.update(7, userFixture()).subscribe({ error });
    http.expectOne('/api/users/7').flush(userFixture({ id: 8 }));
    expect(error).toHaveBeenCalledOnce();
  });
  it.each([400, 401, 403, 404, 409, 500])('does not retry a failed write %s', (status) => {
    const error = vi.fn();
    service.update(7, userFixture()).subscribe({ error });
    http.expectOne('/api/users/7').flush({}, { status, statusText: 'Test error' });
    expect(error).toHaveBeenCalledOnce();
    http.expectNone('/api/users/7');
  });
  it('bounds a stalled read and cancels it', () => {
    vi.useFakeTimers();
    try {
      const error = vi.fn();
      service.list().subscribe({ error });
      const request = http.expectOne('/api/users');
      vi.advanceTimersByTime(15000);
      expect(error).toHaveBeenCalledOnce();
      expect(request.cancelled).toBe(true);
    } finally {
      vi.useRealTimers();
    }
  });
});
