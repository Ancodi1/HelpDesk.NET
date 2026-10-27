import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable, timeout } from 'rxjs';
import {
  Ticket,
  CreateTicketRequest,
  TicketPage,
  TicketQuery,
  ticketStatuses,
  ticketPriorities,
  ticketCategories,
} from './ticket.models';

@Injectable({ providedIn: 'root' })
export class TicketService {
  private readonly http = inject(HttpClient);
  get(id: number): Observable<Ticket> {
    return this.http.get<unknown>(`/api/tickets/${id}`, { cache: 'no-store' }).pipe(
      timeout(15_000),
      map((response) => {
        if (!isTicket(response) || response.id !== id)
          throw new Error('Unexpected ticket response');
        return response;
      }),
    );
  }

  create(input: CreateTicketRequest): Observable<Ticket> {
    // Do not forward incidental fields such as creator, status or assignment.
    const body = {
      title: input.title,
      description: input.description,
      priority: input.priority,
      category: input.category,
    };
    return this.http
      .post<unknown>('/api/tickets', body, { observe: 'response', cache: 'no-store' })
      .pipe(
        timeout(15_000),
        map((response) => {
          if (response.status !== 201 || !isTicket(response.body))
            throw new TicketCreationResponseError();
          return response.body;
        }),
      );
  }

  list(query: TicketQuery): Observable<TicketPage> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['search', 'status', 'priority', 'category'] as const) {
      if (query[key]) params = params.set(key, query[key]);
    }
    return this.http.get<unknown>('/api/tickets', { params, cache: 'no-store' }).pipe(
      timeout(15_000),
      map((response) => {
        if (
          !isPage(response) ||
          response.page !== query.page ||
          response.pageSize !== query.pageSize
        )
          throw new Error('Unexpected ticket response');
        return response;
      }),
    );
  }
}
function object(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function integer(value: unknown, min: number, max = 2_147_483_647): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= min && value <= max;
}
function isTicket(value: unknown): value is Ticket {
  return (
    object(value) &&
    integer(value['id'], 1) &&
    typeof value['title'] === 'string' &&
    typeof value['description'] === 'string' &&
    typeof value['createdAt'] === 'string' &&
    Number.isFinite(Date.parse(value['createdAt'])) &&
    ticketStatuses.some((v) => v === value['status']) &&
    ticketPriorities.some((v) => v === value['priority']) &&
    ticketCategories.some((v) => v === value['category']) &&
    (value['createdByUserId'] === null || integer(value['createdByUserId'], 1)) &&
    (value['assignedToUserId'] === null || integer(value['assignedToUserId'], 1))
  );
}
function isPage(value: unknown): value is TicketPage {
  return (
    object(value) &&
    integer(value['totalCount'], 0) &&
    integer(value['page'], 1) &&
    integer(value['pageSize'], 1, 100) &&
    integer(value['totalPages'], 0) &&
    value['totalPages'] === Math.ceil(value['totalCount'] / value['pageSize']) &&
    Array.isArray(value['items']) &&
    value['items'].length <= value['pageSize'] &&
    value['items'].every(isTicket)
  );
}

// A successful transport response can mean the write happened even if its body is unreadable.
export class TicketCreationResponseError extends Error {
  constructor() {
    super('Unexpected creation response');
  }
}
