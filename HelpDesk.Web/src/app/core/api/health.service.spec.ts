import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { HealthService } from './health.service';

describe('HealthService', () => {
  let http: HttpTestingController;
  let service: HealthService;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(HealthService);
  });
  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });
  it('uses GET on the relative API endpoint', () => {
    const next = vi.fn();
    service.check().subscribe(next);
    const request = http.expectOne('/api/health');
    expect(request.request.method).toBe('GET');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({ status: 'ok' });
    expect(next).toHaveBeenCalledWith({ status: 'ok' });
  });
  it.each([null, {}, { status: 'down' }, 'ok'])('rejects an unexpected response: %j', (body) => {
    const error = vi.fn();
    service.check().subscribe({ error });
    http.expectOne('/api/health').flush(body);
    expect(error).toHaveBeenCalledOnce();
  });
  it('propagates an HTTP error', () => {
    const error = vi.fn();
    service.check().subscribe({ error });
    http.expectOne('/api/health').flush({}, { status: 503, statusText: 'Service Unavailable' });
    expect(error).toHaveBeenCalledOnce();
  });
  it('times out and cancels a request that does not respond', () => {
    vi.useFakeTimers();
    const error = vi.fn();
    service.check().subscribe({ error });
    const request = http.expectOne('/api/health');
    vi.advanceTimersByTime(8_001);
    expect(error).toHaveBeenCalledOnce();
    expect(request.cancelled).toBe(true);
  });
});
