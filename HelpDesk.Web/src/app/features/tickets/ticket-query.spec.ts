import { convertToParamMap } from '@angular/router';
import {
  defaultTicketQuery,
  isCanonicalTicketQuery,
  readTicketQuery,
  ticketQueryParams,
} from './ticket-query';

describe('Ticket query URL contract', () => {
  it('uses API defaults with a clean URL', () => {
    expect(readTicketQuery(convertToParamMap({}))).toEqual(defaultTicketQuery);
    expect(ticketQueryParams({ ...defaultTicketQuery })).toEqual({});
  });
  it('preserves literal search, known filters, page and any valid page size', () => {
    const params = convertToParamMap({
      search: '  %_\\ prueba  ',
      status: 'InProgress',
      priority: 'Critical',
      category: 'Access',
      page: '2',
      pageSize: '7',
    });
    const query = readTicketQuery(params);
    expect(query.search).toBe('  %_\\ prueba  ');
    expect(query.pageSize).toBe(7);
    expect(isCanonicalTicketQuery(params, query)).toBe(true);
    expect(query.status).toBe('InProgress');
  });
  it.each(['0', '-1', '1.5', 'abc', '2147483648', '9007199254740993'])(
    'normalizes an invalid page: %s',
    (page) => expect(readTicketQuery(convertToParamMap({ page })).page).toBe(1),
  );
  it.each(['0', '101', '-1', '5.1', 'abc'])('normalizes an invalid page size: %s', (pageSize) =>
    expect(readTicketQuery(convertToParamMap({ pageSize })).pageSize).toBe(10),
  );
  it('rejects an offset larger than the API integer limit', () =>
    expect(readTicketQuery(convertToParamMap({ page: '2147483647', pageSize: '100' })).page).toBe(
      1,
    ));
  it('drops unrecognized enums and repeated parameters', () => {
    const q = readTicketQuery(
      convertToParamMap({
        status: ['Open', 'Closed'],
        priority: '1',
        category: 'network',
        search: ['a', 'b'],
        page: ['1', '2'],
      }),
    );
    expect(q).toEqual(defaultTicketQuery);
  });
  it('canonicalizes defaults, unsupported sorting and leading zeros', () => {
    const raw = convertToParamMap({ page: '01', pageSize: '10', sort: 'title', status: '' });
    const q = readTicketQuery(raw);
    expect(isCanonicalTicketQuery(raw, q)).toBe(false);
    expect(ticketQueryParams(q)).toEqual({});
  });
});
