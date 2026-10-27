import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, OnInit, signal, computed } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  catchError,
  distinctUntilChanged,
  map,
  merge,
  of,
  startWith,
  Subject,
  switchMap,
} from 'rxjs';
import { TicketService } from '../ticket.service';
import { categoryLabels, priorityLabels, statusLabels, Ticket } from '../ticket.models';
import { readTicketQuery, ticketQueryParams } from '../ticket-query';
import { ticketError } from '../ticket-errors';

type DetailState =
  | { kind: 'loading' }
  | { kind: 'ready'; ticket: Ticket }
  | { kind: 'not-found' }
  | { kind: 'error'; message: string };

@Component({
  selector: 'app-ticket-detail',
  standalone: true,
  imports: [RouterLink, DatePipe],
  templateUrl: './ticket-detail.html',
  styleUrls: ['../ticket-page.scss', './ticket-detail.scss'],
})
export class TicketDetail implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly service = inject(TicketService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly retryAction = new Subject<void>();
  private readonly createdTicketId: unknown =
    this.router.currentNavigation()?.extras.state?.['createdTicketId'];
  readonly id = signal<number | null>(null);
  readonly created = computed(() => this.id() !== null && this.id() === this.createdTicketId);
  readonly backQuery = toSignal(
    this.route.queryParamMap.pipe(map((p) => ticketQueryParams(readTicketQuery(p)))),
    { initialValue: {} },
  );
  readonly state = signal<DetailState>({ kind: 'loading' });
  readonly statusNames = statusLabels;
  readonly priorityNames = priorityLabels;
  readonly categoryNames = categoryLabels;

  ngOnInit(): void {
    const routeIds = this.route.paramMap.pipe(
      map((params) => parseTicketId(params.get('id'))),
      distinctUntilChanged(),
    );
    merge(routeIds, this.retryAction.pipe(map(() => this.id())))
      .pipe(
        switchMap((id) => {
          this.id.set(id);
          if (id === null) return of<DetailState>({ kind: 'not-found' });
          return this.service.get(id).pipe(
            map((ticket): DetailState => ({ kind: 'ready', ticket })),
            catchError((error: unknown) =>
              of<DetailState>(
                error instanceof HttpErrorResponse && error.status === 404
                  ? { kind: 'not-found' }
                  : { kind: 'error', message: ticketError(error, 'detail') },
              ),
            ),
            startWith<DetailState>({ kind: 'loading' }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((state) => this.state.set(state));
  }
  retry(): void {
    this.retryAction.next();
  }
}
export function parseTicketId(raw: string | null): number | null {
  if (raw === null || !/^\d+$/.test(raw)) return null;
  const id = Number(raw);
  return Number.isInteger(id) && id >= 1 && id <= 2_147_483_647 ? id : null;
}
