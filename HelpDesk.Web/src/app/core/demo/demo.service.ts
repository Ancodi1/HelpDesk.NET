import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom, timeout } from 'rxjs';

export interface DemoAccount {
  role: 'User' | 'Technician';
  email: string;
  password: string;
}

@Injectable({ providedIn: 'root' })
export class DemoService {
  private readonly http = inject(HttpClient);
  readonly enabled = signal(false);
  readonly unavailable = signal(false);
  readonly accounts = signal<DemoAccount[]>([]);

  async load(): Promise<void> {
    try {
      const value: unknown = await firstValueFrom(
        this.http.get('/api/demo', { redirect: 'error' }).pipe(timeout(5000)),
      );
      if (!value || typeof value !== 'object' || !('enabled' in value)) throw new Error();
      if (value.enabled === false) {
        this.enabled.set(false);
        this.accounts.set([]);
      } else {
        if (value.enabled !== true || !('accounts' in value) || !Array.isArray(value.accounts) ||
            value.accounts.length !== 2) throw new Error();
        const accounts: DemoAccount[] = [];
        for (const role of ['User', 'Technician'] as const) {
          const account = value.accounts.find(a => a?.role === role);
          if (!account || typeof account.email !== 'string' || typeof account.password !== 'string' ||
              account.password.length < 15 || account.password.length > 128 || account.password.includes('\0'))
            throw new Error();
          // Proyección explícita: ningún campo administrativo o token se incorpora al estado.
          accounts.push({ role, email: account.email, password: account.password });
        }
        this.accounts.set(accounts);
        this.enabled.set(true);
      }
      this.unavailable.set(false);
    } catch {
      this.accounts.set([]);
      this.enabled.set(false);
      this.unavailable.set(true);
    }
  }
}
