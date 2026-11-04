import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, input, signal, SimpleChanges } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, takeUntil } from 'rxjs';
import { ActivityService, TicketActivity } from './activity.service';
import { statusLabels, priorityLabels, categoryLabels } from '../ticket.models';

@Component({
  selector: 'app-ticket-activities',
  imports: [DatePipe],
  templateUrl: './ticket-activities.html',
  styleUrls: ['../ticket-page.scss', '../comments/ticket-comments.scss'],
})
export class TicketActivities {
  readonly ticketId = input.required<number>();
  readonly revision = input(0);
  private readonly service = inject(ActivityService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly changed = new Subject<void>();
  readonly opened = signal(false);
  readonly items = signal<TicketActivity[]>([]);
  readonly reading = signal(false);
  readonly loaded = signal(false);
  readonly hasMore = signal(false);
  readonly nextBeforeId = signal<number | null>(null);
  readonly error = signal('');
  readonly fields = {
    Status: 'Estado',
    Priority: 'Prioridad',
    Category: 'Categoría',
    AssignedToUserId: 'Responsable',
  };

  ngOnChanges(changes: SimpleChanges) {
    if (changes['ticketId']) {
      this.changed.next();
      this.opened.set(false);
      this.items.set([]);
      this.reading.set(false);
      this.loaded.set(false);
      this.hasMore.set(false);
      this.nextBeforeId.set(null);
      this.error.set('');
    } else if (this.opened()) this.load();
  }
  open() {
    this.opened.set(true);
    this.load();
  }
  load(older = false) {
    if (older && (this.reading() || !this.hasMore())) return;
    // Refresh cancels even an in-flight older page after a successful modification.
    this.changed.next();
    this.reading.set(true);
    this.error.set('');
    if (!older) {
      this.items.set([]);
      this.loaded.set(false);
      this.hasMore.set(false);
      this.nextBeforeId.set(null);
    }
    this.service
      .list(this.ticketId(), older ? this.nextBeforeId() : null)
      .pipe(takeUntil(this.changed), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.items.update((items) => (older ? [...items, ...page.items] : page.items));
          this.hasMore.set(page.hasMore);
          this.nextBeforeId.set(page.nextBeforeId);
          this.loaded.set(true);
          this.reading.set(false);
        },
        error: (error: unknown) => {
          this.reading.set(false);
          this.items.set([]);
          this.loaded.set(false);
          this.hasMore.set(false);
          this.nextBeforeId.set(null);
          const status = error instanceof HttpErrorResponse ? error.status : 0;
          this.error.set(
            status === 404
              ? 'La incidencia no existe o ya no está disponible para tu cuenta.'
              : status === 401
                ? 'Tu sesión ya no es válida. Inicia sesión de nuevo.'
                : status === 403
                  ? 'No tienes permiso para consultar este historial.'
                  : 'No se pudo cargar el historial. Puedes volver a intentarlo.',
          );
        },
      });
  }
  value(activity: TicketActivity, old: boolean): string {
    const value = old ? activity.oldValue : activity.newValue;
    if (activity.field === 'AssignedToUserId')
      return (old ? activity.oldAssigneeName : activity.newAssigneeName) ?? 'Sin asignar';
    const labels: Record<string, Record<string, string>> = {
      Status: statusLabels,
      Priority: priorityLabels,
      Category: categoryLabels,
    };
    return value === null ? 'Sin valor' : labels[activity.field][value];
  }
}
