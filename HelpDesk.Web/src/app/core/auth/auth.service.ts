import { HttpClient } from '@angular/common/http';
import { computed, DestroyRef, inject, Injectable, Injector, signal } from '@angular/core';
import { Router } from '@angular/router';
import { defer, map, Observable, tap, timeout } from 'rxjs';
import { LoginRequest, PublicUser, RegisterRequest, SessionUser } from './auth.models';

interface Session {
  token: string;
  user: SessionUser;
  expiresAt: number;
  version: number;
}
export interface Authorization {
  token: string;
  version: number;
}
type SessionNotice = 'expired' | 'unauthorized' | null;

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly injector = inject(Injector);
  // Observing the in-memory session does not need to instantiate an HTTP client.
  private get http(): HttpClient {
    return this.injector.get(HttpClient);
  }
  private readonly router = inject(Router);
  private readonly current = signal<Session | null>(null);
  private version = 0;
  private timer: ReturnType<typeof setTimeout> | undefined;
  readonly user = computed(() => this.current()?.user ?? null);
  readonly expiresAt = computed(() => this.current()?.expiresAt ?? null);
  readonly notice = signal<SessionNotice>(null);

  constructor() {
    inject(DestroyRef).onDestroy(() => this.clear());
  }

  login(input: LoginRequest): Observable<void> {
    return defer(() => {
      this.logout();
      const version = this.version;
      return this.http
        .post<unknown>(
          '/api/auth/login',
          {
            email: input.email.trim().toLowerCase(),
            password: input.password,
          },
          { redirect: 'error' },
        )
        .pipe(
          timeout(15_000),
          map((response) => this.parseSession(response, version)),
          tap((session) => {
            // A late login must not restore a session after logout or a newer attempt.
            if (version !== this.version) throw new Error('Login superseded');
            this.current.set(session);
            this.scheduleExpiry();
          }),
          map(() => undefined),
        );
    });
  }

  register(input: RegisterRequest): Observable<PublicUser> {
    return this.http
      .post<PublicUser>(
        '/api/auth/register',
        {
          name: input.name,
          email: input.email.trim().toLowerCase(),
          password: input.password,
        },
        { redirect: 'error' },
      )
      .pipe(timeout(15_000));
  }

  authorization(): Authorization | null {
    const session = this.current();
    if (!session) return null;
    if (Date.now() >= session.expiresAt) {
      this.endSession('expired');
      return null;
    }
    return { token: session.token, version: session.version };
  }

  isAuthenticated(): boolean {
    return this.authorization() !== null;
  }

  logout(): void {
    this.version++;
    this.clear();
    this.notice.set(null);
  }

  rejectSession(version: number): void {
    // Ignore a 401 from a request sent using an earlier session.
    if (this.current()?.version === version) this.endSession('unauthorized');
  }

  private endSession(notice: Exclude<SessionNotice, null>): void {
    this.logout();
    this.notice.set(notice);
    void this.router.navigateByUrl('/login');
  }

  private clear(): void {
    clearTimeout(this.timer);
    this.timer = undefined;
    this.current.set(null);
  }

  private scheduleExpiry(): void {
    clearTimeout(this.timer);
    const session = this.current();
    if (!session) return;
    this.timer = setTimeout(
      () => {
        if (!this.isAuthenticated()) return;
        this.scheduleExpiry();
      },
      Math.min(Math.max(0, session.expiresAt - Date.now()), 2_147_483_647),
    );
  }

  private parseSession(response: unknown, version: number): Session {
    if (
      !isObject(response) ||
      response['tokenType'] !== 'Bearer' ||
      typeof response['accessToken'] !== 'string' ||
      typeof response['expiresIn'] !== 'number' ||
      !Number.isSafeInteger(response['expiresIn']) ||
      response['expiresIn'] <= 0
    )
      throw new Error('Invalid login response');
    const token = response['accessToken'];
    const parts = token.split('.');
    if (parts.length !== 3 || parts.some((part) => !/^[A-Za-z0-9_-]+$/.test(part)))
      throw new Error('Invalid login response');
    let claims: unknown;
    try {
      const payload = parts[1].replace(/-/g, '+').replace(/_/g, '/');
      const bytes = Uint8Array.from(
        atob(payload + '='.repeat((4 - (payload.length % 4)) % 4)),
        (c) => c.charCodeAt(0),
      );
      claims = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(bytes));
    } catch {
      throw new Error('Invalid login response');
    }
    // Reading claims is for navigation only. The API validates signatures and roles.
    if (
      !isObject(claims) ||
      typeof claims['sub'] !== 'string' ||
      !/^[1-9]\d*$/.test(claims['sub']) ||
      !Number.isSafeInteger(Number(claims['sub'])) ||
      typeof claims['email'] !== 'string' ||
      !claims['email'].trim() ||
      typeof claims['role'] !== 'string' ||
      !['User', 'Technician', 'Admin'].includes(String(claims['role'])) ||
      typeof claims['exp'] !== 'number' ||
      !Number.isSafeInteger(claims['exp']) ||
      (claims['nbf'] !== undefined &&
        (typeof claims['nbf'] !== 'number' ||
          !Number.isSafeInteger(claims['nbf']) ||
          claims['nbf'] * 1000 > Date.now() + 30_000))
    )
      throw new Error('Invalid login response');
    const expiresAt = Math.min(claims['exp'] * 1000, Date.now() + response['expiresIn'] * 1000);
    if (!Number.isFinite(expiresAt) || expiresAt <= Date.now())
      throw new Error('Invalid login response');
    return {
      token,
      version,
      expiresAt,
      user: {
        id: Number(claims['sub']),
        email: claims['email'],
        role: claims['role'] as SessionUser['role'],
      },
    };
  }
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
