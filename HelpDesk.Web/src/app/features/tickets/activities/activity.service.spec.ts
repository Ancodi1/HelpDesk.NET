import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { ActivityService } from './activity.service';
import { activityFixture as item, activityPageFixture as page } from './activity.testing';

describe('Activity API contracts', () => {
  let http: HttpTestingController;
  let service: ActivityService;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(ActivityService);
  });
  afterEach(() => http.verify());
  it('uses a read-only relative endpoint with the requested cursor and no cache', () => {
    const next = vi.fn();
    service.list(41, 101, 1).subscribe(next);
    const read = http.expectOne('/api/tickets/41/activities?pageSize=1&beforeId=101');
    expect(read.request.method).toBe('GET');
    expect(read.request.cache).toBe('no-store');
    read.flush(page({ pageSize: 1 }));
    expect(next).toHaveBeenCalledWith(page({ pageSize: 1 }));
  });
  it.each([
    {},
    page({ items: [item({ ticketId: 42 })] }),
    page({ items: [item({ id: 0 })] }),
    page({ items: [item({ actorUserId: -1 })] }),
    page({ items: [item({ createdAt: 'invalid' })] }),
    page({ items: [item(), item()] }),
    page({ items: [item({ id: 99 }), item()] }),
    page({ hasMore: true, nextBeforeId: 100 }),
    page({ nextBeforeId: 100 }),
    page({ pageSize: 100 }),
    page({ items: [item({ oldValue: 'High' })] }),
    page({ items: [item({ newValue: '<b>High</b>' })] }),
    page({ items: [item({ field: 'AssignedToUserId', oldValue: null, newValue: '12' })] }),
    page({ items: [item({ oldAssigneeName: 'Unexpected' })] }),
  ])('rejects an invalid page without inventing data: %j', (body) => {
    const error = vi.fn();
    service.list(41).subscribe({ error });
    http.expectOne('/api/tickets/41/activities?pageSize=20').flush(body);
    expect(error).toHaveBeenCalledOnce();
  });
  it('rejects IDs outside the requested cursor', () => {
    const error = vi.fn();
    service.list(41, 100).subscribe({ error });
    http.expectOne('/api/tickets/41/activities?pageSize=20&beforeId=100').flush(page());
    expect(error).toHaveBeenCalledOnce();
  });
  it('cancels a stalled read after fifteen seconds without retrying', () => {
    vi.useFakeTimers();
    try {
      const error = vi.fn();
      service.list(41).subscribe({ error });
      const read = http.expectOne('/api/tickets/41/activities?pageSize=20');
      vi.advanceTimersByTime(15000);
      expect(read.cancelled).toBe(true);
      expect(error).toHaveBeenCalledOnce();
      http.expectNone((r) => r.url.includes('/activities'));
    } finally {
      vi.useRealTimers();
    }
  });
});
