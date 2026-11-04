import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { AssignmentPicker } from './assignment-picker';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { AuthService } from '../../../core/auth/auth.service';
import {
  Ticket,
  TicketStatus,
  ticketPriorities,
  ticketCategories,
  statusLabels,
  priorityLabels,
  categoryLabels,
} from '../ticket.models';
import { TicketOperation, TicketService } from '../ticket.service';
import { enumSelection, requiredTicketText } from '../ticket-form.validation';

export const nextStatuses: Record<TicketStatus, readonly TicketStatus[]> = {
  Open: ['InProgress'],
  InProgress: ['Open', 'Resolved'],
  Resolved: ['Open', 'InProgress', 'Closed'],
  Closed: ['Open'],
};
@Component({
  selector: 'app-ticket-management',
  standalone: true,
  imports: [ReactiveFormsModule, AssignmentPicker],
  templateUrl: './ticket-management.html',
  styleUrls: ['../ticket-page.scss', '../create/ticket-create.scss'],
})
export class TicketManagement {
  readonly assignmentPicker = viewChild(AssignmentPicker);
  readonly ticket = input.required<Ticket>();
  readonly updated = output<Ticket>();
  private readonly auth = inject(AuthService);
  private readonly service = inject(TicketService);
  private readonly destroyRef = inject(DestroyRef);
  readonly admin = computed(() => this.auth.user()?.role === 'Admin');
  readonly canStatus = computed(
    () =>
      this.admin() ||
      (this.auth.user()?.role === 'Technician' &&
        this.auth.user()?.id === this.ticket().assignedToUserId),
  );
  readonly statuses = computed(() => [this.ticket().status, ...nextStatuses[this.ticket().status]]);
  readonly priorities = ticketPriorities;
  readonly categories = ticketCategories;
  readonly statusNames = statusLabels;
  readonly priorityNames = priorityLabels;
  readonly categoryNames = categoryLabels;
  readonly pending = signal(false);
  readonly reviewRequired = signal(false);
  readonly message = signal('');
  readonly success = signal(false);
  readonly operation = new FormControl<TicketOperation>('text', { nonNullable: true });
  readonly text = new FormGroup({
    title: new FormControl('', {
      nonNullable: true,
      validators: [requiredTicketText, Validators.maxLength(100)],
    }),
    description: new FormControl('', { nonNullable: true, validators: [requiredTicketText] }),
  });
  readonly status = new FormControl<TicketStatus>('Open', { nonNullable: true });
  readonly priority = new FormControl<Ticket['priority']>('Medium', {
    nonNullable: true,
    validators: [enumSelection(ticketPriorities)],
  });
  readonly category = new FormControl<Ticket['category']>('Other', {
    nonNullable: true,
    validators: [enumSelection(ticketCategories)],
  });
  readonly assignment = new FormControl<number | null>(null, [
    (c) =>
      c.value === null || (Number.isInteger(c.value) && c.value > 0 && c.value <= 2147483647)
        ? null
        : { id: true },
  ]);
  prepare(): void {
    const t = this.ticket();
    this.text.reset({ title: t.title, description: t.description });
    this.status.setValue(t.status);
    this.priority.setValue(t.priority);
    this.category.setValue(t.category);
    this.assignment.setValue(t.assignedToUserId);
    if (!this.admin()) this.operation.setValue('status');
  }
  ngOnInit(): void {
    this.prepare();
  }
  save(): void {
    if (this.pending() || this.reviewRequired()) return;
    const op = this.operation.value;
    if (op === 'status' ? !this.canStatus() : !this.admin()) return;
    const control =
      op === 'text'
        ? this.text
        : op === 'status'
          ? this.status
          : op === 'priority'
            ? this.priority
            : op === 'category'
              ? this.category
              : this.assignment;
    control.markAllAsTouched();
    if (control.invalid || (op === 'status' && !this.statuses().includes(this.status.value))) {
      this.success.set(false);
      this.message.set(
        'Revisa los datos: título obligatorio de hasta 100 caracteres, descripción obligatoria y selección o ID válidos.',
      );
      return;
    }
    if (op === 'assignment' && !this.assignmentPicker()?.selectionAvailable()) {
      this.success.set(false);
      this.message.set('Carga los usuarios y selecciona un responsable elegible o Sin asignar.');
      return;
    }
    this.pending.set(true);
    this.message.set('');
    this.success.set(false);
    const id = this.ticket().id;
    this.service
      .update(id, op, {
        ...this.text.getRawValue(),
        status: this.status.value,
        priority: this.priority.value,
        category: this.category.value,
        assignedToUserId: this.assignment.value,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (ticket) => {
          this.pending.set(false);
          this.updated.emit(ticket);
          this.success.set(true);
          this.message.set('Cambios guardados correctamente.');
        },
        error: (error: unknown) => {
          this.pending.set(false);
          const code = error instanceof HttpErrorResponse ? error.status : 0;
          const messages: Record<number, string> = {
            400: 'El servidor no acepta estos datos. Revisa los campos y el ID: debe corresponder a Technician o Admin.',
            401: 'Tu sesión ya no es válida. Inicia sesión de nuevo.',
            403: 'No tienes permiso para esta operación. Actualiza el detalle para revisar la asignación.',
            404: 'El ticket no existe o ya no está disponible para tu cuenta.',
            409: 'Conflicto con el estado o la asignación actual. Actualiza el detalle antes de continuar.',
          };
          this.message.set(
            messages[code] ??
              'No se pudo confirmar el cambio. Actualiza el detalle antes de reenviar: la operación podría haberse guardado.',
          );
          this.reviewRequired.set(true);
        },
      });
  }
}
