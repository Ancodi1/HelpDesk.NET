import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, OnInit, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import {
  catchError,
  EMPTY,
  from,
  map,
  merge,
  Observable,
  of,
  startWith,
  Subject,
  switchMap,
  timer,
  TimeoutError,
} from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { TicketService } from './ticket.service';
import {
  categoryLabels,
  priorityLabels,
  statusLabels,
  TicketPage,
  TicketQuery,
  ticketCategories,
  ticketPriorities,
  ticketStatuses,
} from './ticket.models';
import {
  defaultTicketQuery,
  isCanonicalTicketQuery,
  readTicketQuery,
  ticketQueryParams,
} from './ticket-query';
import { TicketTable } from './ticket-table';

type ListState =
  { kind: 'loading' } | { kind: 'ready'; data: TicketPage } | { kind: 'error'; message: string };
type Action =
  | { type: 'route'; params: ParamMap }
  | { type: 'search'; search: string }
  | { type: 'filters' }
  | { type: 'page'; page: number }
  | { type: 'retry' }
  | { type: 'clear' };

@Component({
  selector: 'app-tickets',
  standalone: true,
  imports: [ReactiveFormsModule, TicketTable],
  templateUrl: './tickets.html',
  styleUrl: './tickets.scss',
})
export class Tickets implements OnInit {
  private readonly service = inject(TicketService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly builder = inject(NonNullableFormBuilder);
  private readonly actions = new Subject<Action>();
  readonly auth = inject(AuthService);
  readonly canCreate = computed(() => this.auth.user() !== null);
  readonly query = signal<TicketQuery>({ ...defaultTicketQuery });
  readonly state = signal<ListState>({ kind: 'loading' });
  readonly statuses = ticketStatuses;
  readonly priorities = ticketPriorities;
  readonly categories = ticketCategories;
  readonly statusNames = statusLabels;
  readonly priorityNames = priorityLabels;
  readonly categoryNames = categoryLabels;
  readonly pageSizes = [5, 10, 25, 50, 100];
  readonly form = this.builder.group({
    search: '',
    status: this.builder.control<TicketQuery['status']>(''),
    priority: this.builder.control<TicketQuery['priority']>(''),
    category: this.builder.control<TicketQuery['category']>(''),
    pageSize: 10,
  });
  readonly hasFilters = computed(() => {
    const q = this.query();
    return !!(q.search || q.status || q.priority || q.category);
  });
  readonly visibility = computed(() =>
    this.auth.user()?.role === 'Admin'
      ? 'Todas las incidencias disponibles para tu cuenta de administrador.'
      : this.auth.user()?.role === 'Technician'
        ? 'Incidencias creadas por ti o asignadas a ti.'
        : 'Incidencias creadas por ti.',
  );

  ngOnInit(): void {
    const filters = merge(
      this.form.controls.status.valueChanges,
      this.form.controls.priority.valueChanges,
      this.form.controls.category.valueChanges,
      this.form.controls.pageSize.valueChanges,
    ).pipe(map((): Action => ({ type: 'filters' })));
    // A single switchMap cancels both stale HTTP requests and pending search debounce,
    // including when browser history or a select changes while the user is typing.
    merge(
      this.route.queryParamMap.pipe(map((params): Action => ({ type: 'route', params }))),
      this.form.controls.search.valueChanges.pipe(
        map((search): Action => ({ type: 'search', search })),
      ),
      filters,
      this.actions,
    )
      .pipe(
        switchMap((action) => {
          this.state.set({ kind: 'loading' });
          return this.handle(action);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((state) => this.state.set(state));
  }

  newTicket(): void {
    void this.router.navigate(['/tickets/new'], { queryParams: ticketQueryParams(this.query()) });
  }
  openTicket(id: number): void {
    void this.router.navigate(['/tickets', id], { queryParams: ticketQueryParams(this.query()) });
  }

  retry(): void {
    this.actions.next({ type: 'retry' });
  }
  clearFilters(): void {
    this.actions.next({ type: 'clear' });
  }
  goToPage(page: number): void {
    this.actions.next({ type: 'page', page });
  }

  private handle(action: Action): Observable<ListState> {
    const current = this.query();
    if (action.type === 'route') {
      const query = readTicketQuery(action.params);
      this.query.set(query);
      this.form.setValue(
        {
          search: query.search,
          status: query.status,
          priority: query.priority,
          category: query.category,
          pageSize: query.pageSize,
        },
        { emitEvent: false },
      );
      return isCanonicalTicketQuery(action.params, query)
        ? this.load(query)
        : this.navigate(query, true);
    }
    if (action.type === 'retry') return this.load(current);
    if (action.type === 'search')
      return timer(350).pipe(
        switchMap(() => this.commit({ ...current, search: action.search, page: 1 }, true)),
      );
    if (action.type === 'filters') return this.commit({ ...this.form.getRawValue(), page: 1 });
    if (action.type === 'clear')
      return this.commit({ ...defaultTicketQuery, pageSize: current.pageSize });
    return this.commit({ ...current, page: Math.max(1, action.page) });
  }

  private commit(query: TicketQuery, replaceUrl = false): Observable<ListState> {
    const same = (Object.keys(defaultTicketQuery) as (keyof TicketQuery)[]).every(
      (key) => query[key] === this.query()[key],
    );
    if (!same) return this.navigate(query, replaceUrl);
    this.form.setValue(
      {
        search: query.search,
        status: query.status,
        priority: query.priority,
        category: query.category,
        pageSize: query.pageSize,
      },
      { emitEvent: false },
    );
    return this.load(query);
  }
  private navigate(query: TicketQuery, replaceUrl: boolean): Observable<ListState> {
    return from(
      this.router.navigate([], {
        relativeTo: this.route,
        queryParams: ticketQueryParams(query),
        replaceUrl,
      }),
    ).pipe(
      switchMap((ok) =>
        ok
          ? EMPTY
          : of<ListState>({
              kind: 'error',
              message: 'No se pudo actualizar la consulta. Inténtalo de nuevo.',
            }),
      ),
      catchError(() =>
        of<ListState>({
          kind: 'error',
          message: 'No se pudo actualizar la consulta. Inténtalo de nuevo.',
        }),
      ),
    );
  }
  private load(query: TicketQuery): Observable<ListState> {
    return this.service.list(query).pipe(
      map((data): ListState => ({ kind: 'ready', data })),
      catchError((error: unknown) => of<ListState>({ kind: 'error', message: listError(error) })),
      startWith<ListState>({ kind: 'loading' }),
    );
  }
}
function listError(error: unknown): string {
  if (error instanceof TimeoutError)
    return 'El servidor tarda en responder. Puedes volver a intentarlo.';
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0)
      return 'No se pudo conectar con el servidor. Comprueba la conexión y vuelve a intentarlo.';
    if (error.status === 401) return 'Tu sesión ya no es válida. Inicia sesión de nuevo.';
    if (error.status === 403) return 'No tienes permiso para consultar estas incidencias.';
    if (error.status === 400)
      return 'El servidor no acepta esta consulta. Revisa los filtros o límpialos.';
  }
  return 'No se pudieron cargar las incidencias. Inténtalo de nuevo más tarde.';
}
