import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { authError } from '../auth.errors';
import { emailAddress, nonBlank } from '../auth.validation';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './login.html',
  styleUrl: '../auth-form.scss',
})
export class Login {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly builder = inject(NonNullableFormBuilder);
  readonly registered = inject(ActivatedRoute).snapshot.queryParamMap.get('registered') === '1';
  readonly pending = signal(false);
  readonly error = signal<string | null>(null);
  readonly form = this.builder.group({ email: ['', emailAddress], password: ['', nonBlank] });

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
      .login(this.form.getRawValue())
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          this.pending.set(false);
          this.form.controls.password.reset('');
        }),
      )
      .subscribe({
        next: () => {
          void this.router.navigateByUrl('/session');
        },
        error: (error: unknown) => this.error.set(authError(error, 'login')),
      });
  }
}
