import { ParamMap, Params } from '@angular/router';
import { TicketQuery, ticketStatuses, ticketPriorities, ticketCategories } from './ticket.models';

export const defaultTicketQuery: Readonly<TicketQuery> = {
  search: '',
  status: '',
  priority: '',
  category: '',
  page: 1,
  pageSize: 10,
};

export function readTicketQuery(params: ParamMap): TicketQuery {
  const single = (name: string) => (params.getAll(name).length === 1 ? params.get(name)! : '');
  const integer = (name: string, max: number, fallback: number) => {
    const raw = single(name);
    const value = /^\d+$/.test(raw) ? Number(raw) : NaN;
    return Number.isSafeInteger(value) && value >= 1 && value <= max ? value : fallback;
  };
  const pageSize = integer('pageSize', 100, 10);
  const page = integer('page', 2_147_483_647, 1);
  const status = single('status');
  const priority = single('priority');
  const category = single('category');
  return {
    search: single('search'),
    status: ticketStatuses.find((v) => v === status) ?? '',
    priority: ticketPriorities.find((v) => v === priority) ?? '',
    category: ticketCategories.find((v) => v === category) ?? '',
    page: (page - 1) * pageSize <= 2_147_483_647 ? page : 1,
    pageSize,
  };
}
export function ticketQueryParams(query: TicketQuery): Params {
  const params: Params = {};
  if (query.search) params['search'] = query.search;
  if (query.status) params['status'] = query.status;
  if (query.priority) params['priority'] = query.priority;
  if (query.category) params['category'] = query.category;
  if (query.page !== 1) params['page'] = query.page;
  if (query.pageSize !== 10) params['pageSize'] = query.pageSize;
  return params;
}
export function isCanonicalTicketQuery(params: ParamMap, query: TicketQuery): boolean {
  const canonical = ticketQueryParams(query);
  return (
    params.keys.length === Object.keys(canonical).length &&
    params.keys.every(
      (key) => params.getAll(key).length === 1 && params.get(key) === String(canonical[key]),
    )
  );
}
