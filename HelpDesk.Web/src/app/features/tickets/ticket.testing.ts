// Fixtures for HTTP-isolated unit tests only; never imported by application code.
import { Ticket, TicketPage } from './ticket.models';
export function ticketFixture(overrides: Partial<Ticket> = {}): Ticket {
  return {
    id: 41,
    title: 'Test fixture incident',
    description: 'Test fixture description',
    createdAt: '2026-10-27T10:00:00Z',
    status: 'Open',
    priority: 'Medium',
    category: 'Software',
    createdByUserId: null,
    assignedToUserId: null,
    ...overrides,
  };
}
export function pageFixture(overrides: Partial<TicketPage> = {}): TicketPage {
  return {
    items: [ticketFixture()],
    totalCount: 1,
    page: 1,
    pageSize: 10,
    totalPages: 1,
    ...overrides,
  };
}
