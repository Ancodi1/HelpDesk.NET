import { DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { HomeIntro } from './home-intro';
import { HealthService } from '../../core/api/health.service';

type ConnectionState = 'loading' | 'success' | 'error';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [DatePipe, HomeIntro],
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class Home implements OnInit {
  private readonly health = inject(HealthService);
  private readonly destroyRef = inject(DestroyRef);
  private inFlight = false;
  readonly connection = signal<ConnectionState>('loading');
  readonly lastChecked = signal<Date | null>(null);

  ngOnInit(): void {
    this.checkConnection();
  }

  checkConnection(): void {
    if (this.inFlight) return;
    this.inFlight = true;
    this.connection.set('loading');
    this.health
      .check()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          this.inFlight = false;
        }),
      )
      .subscribe({
        next: () => {
          this.connection.set('success');
          this.lastChecked.set(new Date());
        },
        error: () => {
          this.connection.set('error');
          this.lastChecked.set(new Date());
        },
      });
  }
}
