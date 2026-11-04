// Fixtures for simulated HTTP only; never imported by application code.
import { CommentPage, TicketComment } from './comment.service';
export function commentFixture(overrides: Partial<TicketComment> = {}): TicketComment {
  return {
    id: 100,
    ticketId: 41,
    authorUserId: 3,
    authorName: 'Historical author',
    content: 'Plain comment',
    createdAt: '2026-11-04T09:00:00Z',
    ...overrides,
  };
}
export function commentsFixture(overrides: Partial<CommentPage> = {}): CommentPage {
  return {
    items: [commentFixture()],
    hasMore: false,
    nextBeforeId: null,
    pageSize: 20,
    ...overrides,
  };
}
