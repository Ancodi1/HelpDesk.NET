import { ticketStatuses, ticketPriorities, ticketCategories } from '../ticket.models';
import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, timeout } from 'rxjs';

export interface TicketActivity {
  id: number;
  ticketId: number;
  actorUserId: number;
  actorName: string;
  field: 'Status' | 'Priority' | 'Category' | 'AssignedToUserId';
  oldValue: string | null;
  newValue: string | null;
  oldAssigneeName: string | null;
  newAssigneeName: string | null;
  createdAt: string;
}
export interface ActivityPage {
  items: TicketActivity[];
  hasMore: boolean;
  nextBeforeId: number | null;
  pageSize: number;
}
@Injectable({ providedIn: 'root' })
export class ActivityService {
  private readonly http = inject(HttpClient);
  list(ticketId: number, beforeId: number | null = null, pageSize = 20) {
    let params = new HttpParams().set('pageSize', pageSize);
    if (beforeId !== null) params = params.set('beforeId', beforeId);
    return this.http
      .get<unknown>(`/api/tickets/${ticketId}/activities`, { params, cache: 'no-store' })
      .pipe(
        timeout(15_000),
        map((value) => {
          const page = value as ActivityPage | null;
          if (
            !page ||
            !Array.isArray(page.items) ||
            page.pageSize !== pageSize ||
            typeof page.hasMore !== 'boolean' ||
            page.items.length > pageSize ||
            (page.hasMore && page.items.length !== pageSize)
          )
            throw new Error('Unexpected activity page');
          const items = page.items.map((item) => readActivity(item, ticketId));
          if (
            items.some(
              (item, index) =>
                (beforeId !== null && item.id >= beforeId) ||
                (index > 0 && item.id >= items[index - 1].id),
            ) ||
            page.nextBeforeId !== (page.hasMore ? items.at(-1)?.id : null)
          )
            throw new Error('Unexpected activity cursor');
          return { items, pageSize, hasMore: page.hasMore, nextBeforeId: page.nextBeforeId };
        }),
      );
  }
}
function readActivity(value: unknown, ticketId: number): TicketActivity {
  const a = value as TicketActivity | null;
  const id = (v: unknown) =>
    typeof v === 'number' && Number.isInteger(v) && v > 0 && v <= 2147483647;
  if (
    !a ||
    !id(a.id) ||
    a.ticketId !== ticketId ||
    !id(a.actorUserId) ||
    typeof a.actorName !== 'string' ||
    typeof a.createdAt !== 'string' ||
    !Number.isFinite(Date.parse(a.createdAt)) ||
    a.oldValue === a.newValue
  )
    throw new Error('Unexpected activity');
  const values: Record<string, readonly string[]> = {
    Status: ticketStatuses,
    Priority: ticketPriorities,
    Category: ticketCategories,
  };
  if (a.field === 'AssignedToUserId') {
    for (const [value, name] of [
      [a.oldValue, a.oldAssigneeName],
      [a.newValue, a.newAssigneeName],
    ]) {
      if (
        value === null
          ? name !== null
          : typeof value !== 'string' ||
            !/^[1-9]\d*$/.test(value) ||
            !id(Number(value)) ||
            typeof name !== 'string'
      )
        throw new Error('Unexpected assignee');
    }
  } else if (
    !Object.hasOwn(values, a.field) ||
    typeof a.oldValue !== 'string' ||
    typeof a.newValue !== 'string' ||
    !values[a.field].includes(a.oldValue) ||
    !values[a.field].includes(a.newValue) ||
    a.oldAssigneeName !== null ||
    a.newAssigneeName !== null
  ) {
    throw new Error('Unexpected activity values');
  }
  return {
    id: a.id,
    ticketId,
    actorUserId: a.actorUserId,
    actorName: a.actorName,
    field: a.field,
    oldValue: a.oldValue,
    newValue: a.newValue,
    oldAssigneeName: a.oldAssigneeName,
    newAssigneeName: a.newAssigneeName,
    createdAt: a.createdAt,
  };
}
