import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { CommentService } from './comment.service';
import { commentFixture, commentsFixture } from './comment.testing';

describe('Comment API contracts', () => {
  let http: HttpTestingController;
  let service: CommentService;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(CommentService);
  });
  afterEach(() => http.verify());
  it('uses explicit relative endpoints, bounded pages and only the content in POST', () => {
    const next = vi.fn();
    service.list(41, 101, 1).subscribe(next);
    const read = http.expectOne('/api/tickets/41/comments?pageSize=1&beforeId=101');
    expect(read.request.cache).toBe('no-store');
    read.flush(commentsFixture({ pageSize: 1 }));
    expect(next).toHaveBeenCalledWith(commentsFixture({ pageSize: 1 }));
    service.create(41, '  Plain\ntext  ').subscribe();
    const post = http.expectOne('/api/tickets/41/comments');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ content: '  Plain\ntext  ' });
    post.flush(commentFixture(), { status: 201, statusText: 'Created' });
  });
  it.each([
    {},
    commentsFixture({ items: [commentFixture({ ticketId: 42 })] }),
    commentsFixture({ items: [commentFixture({ id: 0 })] }),
    commentsFixture({ items: [commentFixture({ createdAt: 'invalid' })] }),
    commentsFixture({ items: [commentFixture(), commentFixture()] }),
    commentsFixture({ items: [commentFixture({ id: 99 }), commentFixture()] }),
    commentsFixture({ hasMore: true, nextBeforeId: 100 }),
    commentsFixture({ nextBeforeId: 100 }),
    commentsFixture({ pageSize: 100 }),
  ])('rejects malformed page/cursor without inventing data: %j', (body) => {
    const error = vi.fn();
    service.list(41).subscribe({ error });
    http.expectOne('/api/tickets/41/comments?pageSize=20').flush(body);
    expect(error).toHaveBeenCalledOnce();
  });
  it('rejects a page that violates the requested cursor', () => {
    const error = vi.fn();
    service.list(41, 100).subscribe({ error });
    http.expectOne('/api/tickets/41/comments?pageSize=20&beforeId=100').flush(commentsFixture());
    expect(error).toHaveBeenCalledOnce();
  });
  it.each([200, 201])('rejects a malformed or wrong-status publication %s', (status) => {
    const error = vi.fn();
    service.create(41, 'Draft').subscribe({ error });
    http
      .expectOne('/api/tickets/41/comments')
      .flush(commentFixture({ ticketId: 42 }), { status, statusText: 'Result' });
    expect(error).toHaveBeenCalledOnce();
  });
  it.each([400, 401, 403, 404, 409, 500])('never retries a failed publication %s', (status) => {
    const error = vi.fn();
    service.create(41, 'Draft').subscribe({ error });
    http.expectOne('/api/tickets/41/comments').flush({}, { status, statusText: 'Error' });
    expect(error).toHaveBeenCalledOnce();
    http.expectNone('/api/tickets/41/comments');
  });
  it('bounds and cancels a stalled request', () => {
    vi.useFakeTimers();
    try {
      const error = vi.fn();
      service.create(41, 'Draft').subscribe({ error });
      const request = http.expectOne('/api/tickets/41/comments');
      vi.advanceTimersByTime(15000);
      expect(request.cancelled).toBe(true);
      expect(error).toHaveBeenCalledOnce();
    } finally {
      vi.useRealTimers();
    }
  });
});
