import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize, map } from 'rxjs';
import { TicketService } from '../ticket.service';
import {
  categoryLabels,
  priorityLabels,
  Ticket,
  TicketPriority,
  TicketCategory,
  ticketPriorities,
  ticketCategories,
} from '../ticket.models';
import { readTicketQuery, ticketQueryParams } from '../ticket-query';
import { enumSelection, requiredTicketText } from '../ticket-form.validation';
import { ticketError } from '../ticket-errors';

@Component({
  selector: 'app-ticket-create',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './ticket-create.html',
  styleUrls: ['../ticket-page.scss', './ticket-create.scss'],
})
export class TicketCreate {
  private readonly service = inject(TicketService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly builder = inject(NonNullableFormBuilder);
  readonly backQuery = toSignal(
    this.route.queryParamMap.pipe(map((p) => ticketQueryParams(readTicketQuery(p)))),
    { initialValue: {} },
  );
  readonly priorities = ticketPriorities;
  readonly categories = ticketCategories;
  readonly priorityNames = priorityLabels;
  readonly categoryNames = categoryLabels;
  readonly pending = signal(false);
  readonly created = signal<Ticket | null>(null);
  readonly error = signal<string | null>(null);
  readonly form = this.builder.group({
    title: ['', [requiredTicketText, Validators.maxLength(100)]],
    description: ['', requiredTicketText],
    priority: this.builder.control<TicketPriority>('Medium', enumSelection(ticketPriorities)),
    category: this.builder.control<TicketCategory>('Other', enumSelection(ticketCategories)),
  });

  submit(): void {
    if (this.pending() || this.created()) return;
    this.error.set(null);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.pending.set(true);
    this.service
      .create(this.form.getRawValue())
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.pending.set(false)),
      )
      .subscribe({
        next: (ticket) => {
          this.created.set(ticket);
          // Only a validated response ID is used. Never redirect through a Location URL.
          void this.router
            .navigate(['/tickets', ticket.id], {
              queryParams: this.backQuery(),
              state: { createdTicketId: ticket.id },
            })
            .catch(() => undefined); // The confirmation includes a safe detail link if navigation fails.
        },
        error: (error: unknown) => this.error.set(ticketError(error, 'create')),
      });
  }
}
