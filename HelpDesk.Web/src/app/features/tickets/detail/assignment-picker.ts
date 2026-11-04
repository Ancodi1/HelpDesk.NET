import { Component, DestroyRef, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { catchError, map, of, startWith, Subject, switchMap } from 'rxjs';
import { PublicUser } from '../../../core/auth/auth.models';
import { UserService, userError } from '../../admin/user.service';

type Assignees =
  { kind: 'loading' } | { kind: 'ready'; users: PublicUser[] } | { kind: 'error'; message: string };

@Component({
  selector: 'app-assignment-picker',
  imports: [ReactiveFormsModule],
  styleUrl: '../create/ticket-create.scss',
  template: `
    @let state = directory();
    @if (state.kind === 'loading') {
      <p role="status">Cargando usuarios elegibles…</p>
    } @else if (state.kind === 'error') {
      <p class="field-error" role="alert">{{ state.message }}</p>
      <button type="button" (click)="reload()">Reintentar usuarios</button>
    } @else {
      <div class="field">
        <label for="edit-assignment">Responsable (técnico o administrador)</label>
        <select id="edit-assignment" [formControl]="control()">
          <option [ngValue]="null">Sin asignar</option>
          @if (!selectionAvailable()) {
            <option [ngValue]="control().value" disabled>
              Asignación actual no elegible o no disponible
            </option>
          }
          @for (user of state.users; track user.id) {
            <option [ngValue]="user.id">{{ user.name }} — {{ user.email }}</option>
          }
        </select>
        @if (state.users.length === 0) {
          <p class="hint">No hay usuarios elegibles. Puedes desasignar.</p>
        }
        @if (!selectionAvailable()) {
          <p class="field-error">Elige un responsable disponible o Sin asignar.</p>
        }
        <p class="hint">
          La API comprueba el rol actual al guardar. No informa de activación de cuentas.
        </p>
        <button type="button" (click)="reload()">Actualizar usuarios</button>
      </div>
    }
  `,
})
export class AssignmentPicker {
  readonly control = input.required<FormControl<number | null>>();
  readonly directory = signal<Assignees>({ kind: 'loading' });
  private readonly users = inject(UserService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly refresh = new Subject<void>();
  ngOnInit() {
    this.refresh
      .pipe(
        startWith(undefined),
        switchMap(() => {
          this.directory.set({ kind: 'loading' });
          return this.users.list().pipe(
            map((users) => ({
              kind: 'ready' as const,
              users: users.filter((u) => u.role === 'Technician' || u.role === 'Admin'),
            })),
            catchError((error) => of({ kind: 'error' as const, message: userError(error) })),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((state) => this.directory.set(state));
  }
  reload() {
    this.refresh.next();
  }
  selectionAvailable() {
    const state = this.directory();
    return (
      state.kind === 'ready' &&
      (this.control().value === null || state.users.some((u) => u.id === this.control().value))
    );
  }
}
