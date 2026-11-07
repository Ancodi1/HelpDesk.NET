import { PageHeader } from '../../shared/page-header';
import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { catchError, forkJoin, map, of, startWith, Subject, switchMap } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { PublicUser, roleLabels, UserRole } from '../../core/auth/auth.models';
import { emailAddress } from '../auth/auth.validation';
import { enumSelection, requiredTicketText } from '../tickets/ticket-form.validation';
import { defaultTicketQuery } from '../tickets/ticket-query';
import { statusLabels, ticketStatuses } from '../tickets/ticket.models';
import { TicketService } from '../tickets/ticket.service';
import { UserService, userError, userRoles } from './user.service';

type Directory =
  { kind: 'loading' } | { kind: 'ready'; users: PublicUser[] } | { kind: 'error'; message: string };
type Summary = { kind: 'loading' } | { kind: 'ready'; counts: number[] } | { kind: 'error' };

@Component({
  selector: 'app-admin',
  imports: [PageHeader, ReactiveFormsModule, RouterLink, DatePipe],
  templateUrl: './admin.html',
  styleUrls: [
    '../tickets/ticket-page.scss',
    '../tickets/create/ticket-create.scss',
    './admin.scss',
  ],
})
export class Admin {
  private readonly users = inject(UserService);
  private readonly tickets = inject(TicketService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly directoryRefresh = new Subject<void>();
  private readonly summaryRefresh = new Subject<void>();
  readonly roles = userRoles;
  readonly roleNames = roleLabels;
  readonly statuses = ticketStatuses;
  readonly statusNames = statusLabels;
  readonly directory = signal<Directory>({ kind: 'loading' });
  readonly summary = signal<Summary>({ kind: 'loading' });
  readonly search = new FormControl('', { nonNullable: true });
  readonly roleFilter = new FormControl('', { nonNullable: true });
  private readonly term = toSignal(this.search.valueChanges, { initialValue: '' });
  private readonly filter = toSignal(this.roleFilter.valueChanges, { initialValue: '' });
  readonly filtered = computed(() => {
    const state = this.directory();
    if (state.kind !== 'ready') return [];
    const term = this.term().trim().toLocaleLowerCase();
    return state.users.filter(
      (u) =>
        (!this.filter() || u.role === this.filter()) &&
        `${u.name} ${u.email} ${u.id}`.toLocaleLowerCase().includes(term),
    );
  });
  readonly selected = signal<PublicUser | null>(null);
  readonly pending = signal(false);
  readonly reviewRequired = signal(false);
  readonly message = signal('');
  readonly success = signal(false);
  readonly confirming = signal(false);
  readonly selfChange = computed(() => this.selected()?.id === this.auth.user()?.id);
  readonly form = new FormGroup({
    name: new FormControl('', {
      nonNullable: true,
      validators: [requiredTicketText, Validators.maxLength(100)],
    }),
    email: new FormControl('', { nonNullable: true, validators: [emailAddress] }),
    role: new FormControl<UserRole>('User', {
      nonNullable: true,
      validators: [enumSelection(userRoles)],
    }),
  });

  ngOnInit() {
    this.directoryRefresh
      .pipe(
        startWith(undefined),
        switchMap(() => {
          this.directory.set({ kind: 'loading' });
          return this.users.list().pipe(
            map((users) => ({ kind: 'ready' as const, users })),
            catchError((error) => of({ kind: 'error' as const, message: userError(error) })),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((state) => this.directory.set(state));
    this.summaryRefresh
      .pipe(
        startWith(undefined),
        switchMap(() => {
          this.summary.set({ kind: 'loading' });
          return forkJoin(
            ticketStatuses.map((status) =>
              this.tickets
                .list({ ...defaultTicketQuery, status, pageSize: 1 })
                .pipe(map((page) => page.totalCount)),
            ),
          ).pipe(
            map((counts) => ({ kind: 'ready' as const, counts })),
            catchError(() => of({ kind: 'error' as const })),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((state) => this.summary.set(state));
    this.form.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.confirming.set(false));
  }
  reloadUsers() {
    if (this.pending()) return;
    this.selected.set(null);
    this.confirming.set(false);
    this.reviewRequired.set(false);
    this.message.set('');
    this.directoryRefresh.next();
  }
  reloadSummary() {
    this.summaryRefresh.next();
  }
  edit(user: PublicUser) {
    if (this.pending() || this.reviewRequired()) return;
    this.selected.set(user);
    this.form.reset({ name: user.name, email: user.email, role: user.role });
    this.message.set('');
    this.success.set(false);
    this.confirming.set(false);
  }
  cancel() {
    if (!this.pending()) {
      this.selected.set(null);
      this.confirming.set(false);
    }
  }
  save(confirmed = false) {
    const selected = this.selected();
    if (!selected || this.pending() || this.reviewRequired()) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      this.message.set('Revisa nombre (máximo 100 caracteres), correo y rol.');
      this.success.set(false);
      return;
    }
    const body = this.form.getRawValue();
    const roleChanged = body.role !== selected.role;
    if (roleChanged && (!confirmed || !this.confirming())) {
      this.confirming.set(true);
      return;
    }
    this.pending.set(true);
    this.message.set('');
    this.success.set(false);
    this.users
      .update(selected.id, body)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (user) => {
          this.pending.set(false);
          this.confirming.set(false);
          this.directory.update((state) =>
            state.kind === 'ready'
              ? { kind: 'ready', users: state.users.map((u) => (u.id === user.id ? user : u)) }
              : state,
          );
          this.selected.set(null);
          this.success.set(true);
          this.message.set(
            'Usuario actualizado correctamente. Si cambió su rol, debe volver a iniciar sesión.',
          );
          if (user.id === this.auth.user()?.id && user.role !== this.auth.user()?.role) {
            // A role change invalidates this JWT on the server; do not keep a stale Admin session.
            this.auth.logout();
            void this.router.navigateByUrl('/login');
          }
        },
        error: (error) => {
          this.pending.set(false);
          this.confirming.set(false);
          this.reviewRequired.set(true);
          this.message.set(userError(error, true));
        },
      });
  }
}
