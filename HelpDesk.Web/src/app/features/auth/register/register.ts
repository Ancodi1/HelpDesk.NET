import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { authError } from '../auth.errors';
import { emailAddress, nonBlank, registrationPassword } from '../auth.validation';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './register.html',
  styleUrl: '../auth-form.scss',
})
export class Register {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly builder = inject(NonNullableFormBuilder);
  readonly pending = signal(false);
  readonly error = signal<string | null>(null);
  readonly form = this.builder.group({
    name: ['', [nonBlank, Validators.maxLength(100)]],
    email: ['', emailAddress],
    password: ['', registrationPassword],
  });

  constructor() {
    this.destroyRef.onDestroy(() => this.form.controls.password.reset(''));
  }

  submit(): void {
    if (this.pending()) return;
    this.form.markAllAsTouched();
    this.error.set(null);
    if (this.form.invalid) return;
    this.pending.set(true);
    this.auth
      .register(this.form.getRawValue())
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          this.pending.set(false);
          this.form.controls.password.reset('');
        }),
      )
      .subscribe({
        next: () => {
          void this.router.navigate(['/login'], { queryParams: { registered: '1' } });
        },
        error: (error: unknown) => this.error.set(authError(error, 'register')),
      });
  }
}
