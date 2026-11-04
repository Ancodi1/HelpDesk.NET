import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TicketComments } from './ticket-comments';
import { commentFixture, commentsFixture } from './comment.testing';

describe('Ticket comment conversation', () => {
  let http: HttpTestingController;
  let f: ComponentFixture<TicketComments>;
  let c: TicketComments;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    f = TestBed.createComponent(TicketComments);
    c = f.componentInstance;
    f.componentRef.setInput('ticketId', 41);
    f.detectChanges();
  });
  afterEach(() => {
    f.destroy();
    http.verify();
  });
  const read = () => http.expectOne('/api/tickets/41/comments?pageSize=20');
  function ready() {
    c.open();
    read().flush(commentsFixture());
    f.detectChanges();
  }
  it('opens on demand, renders historical author/content as text and shows empty state', () => {
    http.expectNone((r) => r.url.includes('/comments'));
    (f.nativeElement.querySelector('button') as HTMLButtonElement).click();
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Cargando comentarios');
    read().flush(
      commentsFixture({
        items: [
          commentFixture({
            authorName: '<b>Author</b>',
            content: '<script>text</script>\nNext line',
          }),
        ],
      }),
    );
    f.detectChanges();
    expect(f.nativeElement.querySelector('script')).toBeNull();
    expect(f.nativeElement.querySelector('b')).toBeNull();
    expect(f.nativeElement.textContent).toContain('<b>Author</b>');
    expect(f.nativeElement.textContent).toContain('<script>text</script>\nNext line');
    expect(f.nativeElement.querySelector('time').getAttribute('datetime')).toBe(
      '2026-11-04T09:00:00Z',
    );
    c.load();
    read().flush(commentsFixture({ items: [] }));
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('No hay comentarios todavía');
  });
  it('appends older comments using the server cursor and keeps new arrivals out of old pages', () => {
    c.open();
    read().flush(
      commentsFixture({
        items: Array.from({ length: 20 }, (_, i) => commentFixture({ id: 100 - i })),
        hasMore: true,
        nextBeforeId: 81,
      }),
    );
    f.detectChanges();
    const button = [...f.nativeElement.querySelectorAll('button')].find((b: HTMLButtonElement) =>
      b.textContent?.includes('Cargar anteriores'),
    ) as HTMLButtonElement;
    button.click();
    c.load(true);
    http
      .expectOne('/api/tickets/41/comments?pageSize=20&beforeId=81')
      .flush(commentsFixture({ items: [commentFixture({ id: 80 })] }));
    f.detectChanges();
    expect(c.items()).toHaveLength(21);
    expect(c.items().at(-1)?.id).toBe(80);
    expect(c.hasMore()).toBe(false);
  });
  it('validates whitespace, UTF-16 limits and null characters before writing', () => {
    ready();
    for (const text of ['', '\u0085\u2003', 'x'.repeat(4001), '😀'.repeat(2001), 'text\0']) {
      c.content.setValue(text);
      c.publish();
      expect(c.content.invalid).toBe(true);
    }
    http.expectNone((r) => r.method === 'POST');
    c.content.setValue('😀'.repeat(2000));
    expect(c.content.valid).toBe(true);
  });
  it('submits once through a real reactive form without native navigation and clears confirmed text', () => {
    ready();
    const textarea = f.nativeElement.querySelector('textarea') as HTMLTextAreaElement;
    textarea.value = 'New\nplain comment';
    textarea.dispatchEvent(new Event('input'));
    const form = f.nativeElement.querySelector('form') as HTMLFormElement;
    const event = new Event('submit', { bubbles: true, cancelable: true });
    form.dispatchEvent(event);
    c.publish();
    expect(event.defaultPrevented).toBe(true);
    f.detectChanges();
    expect(f.nativeElement.querySelector('fieldset').disabled).toBe(true);
    const request = http.expectOne('/api/tickets/41/comments');
    expect(request.request.body).toEqual({ content: 'New\nplain comment' });
    request.flush(commentFixture({ id: 101, content: textarea.value }), {
      status: 201,
      statusText: 'Created',
    });
    f.detectChanges();
    expect(c.content.value).toBe('');
    expect(c.items()[0].id).toBe(101);
    expect(c.success()).toBe(true);
    expect(f.nativeElement.textContent).toContain('Comentario publicado correctamente');
  });
  it.each([400, 401, 403, 404, 409, 500, 0])(
    'preserves draft after write error %s, requires refresh and never exposes provider details',
    (status) => {
      ready();
      c.content.setValue('Draft');
      c.publish();
      const request = http.expectOne('/api/tickets/41/comments');
      if (status === 0) request.error(new ProgressEvent('error'));
      else request.flush({ error: 'provider secret' }, { status, statusText: 'Error' });
      f.detectChanges();
      expect(c.reviewRequired()).toBe(true);
      expect(c.content.value).toBe('Draft');
      expect(f.nativeElement.textContent).not.toContain('provider secret');
      c.publish();
      http.expectNone((r) => r.method === 'POST');
      if ([401, 403, 404].includes(status)) expect(c.items()).toEqual([]);
      c.load();
      read().flush(commentsFixture());
      expect(c.reviewRequired()).toBe(false);
      expect(c.content.value).toBe('Draft');
    },
  );
  it('treats an unreadable successful POST as uncertain rather than resending', () => {
    ready();
    c.content.setValue('Draft');
    c.publish();
    http
      .expectOne('/api/tickets/41/comments')
      .flush(commentFixture({ ticketId: 42 }), { status: 201, statusText: 'Created' });
    expect(c.reviewRequired()).toBe(true);
    expect(c.content.value).toBe('Draft');
    expect(c.items()).toHaveLength(1);
  });
  it('clears stale data and cancels reads and publications when ticket ID changes', () => {
    c.open();
    const old = read();
    f.componentRef.setInput('ticketId', 42);
    f.detectChanges();
    expect(old.cancelled).toBe(true);
    expect(c.items()).toEqual([]);
    expect(c.opened()).toBe(false);
    c.open();
    http
      .expectOne('/api/tickets/42/comments?pageSize=20')
      .flush(commentsFixture({ items: [commentFixture({ ticketId: 42 })] }));
    c.content.setValue('Only ticket 42');
    c.publish();
    const post = http.expectOne('/api/tickets/42/comments');
    f.componentRef.setInput('ticketId', 43);
    f.detectChanges();
    expect(post.cancelled).toBe(true);
    expect(c.content.value).toBe('');
    expect(c.pending()).toBe(false);
    expect(c.items()).toEqual([]);
  });
  it('offers safe read retry and cancels on destruction', () => {
    c.open();
    read().flush({ error: 'provider secret' }, { status: 503, statusText: 'Unavailable' });
    f.detectChanges();
    expect(c.loaded()).toBe(false);
    expect(f.nativeElement.textContent).not.toContain('provider secret');
    c.load();
    const pending = read();
    f.destroy();
    expect(pending.cancelled).toBe(true);
  });
});
