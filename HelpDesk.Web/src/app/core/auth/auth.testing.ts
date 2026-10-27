// Test fixtures only. Never imported by runtime code or sent to a real API.
import { HttpTestingController } from '@angular/common/http/testing';
import { AuthService } from './auth.service';
import { UserRole } from './auth.models';

export function loginResponse(
  role: UserRole = 'Admin',
  overrides: Record<string, unknown> = {},
  expiresIn = 1800,
) {
  const now = Math.floor(Date.now() / 1000);
  const encode = (value: unknown) =>
    btoa(JSON.stringify(value)).replace(/=/g, '').replace(/\+/g, '-').replace(/\//g, '_');
  return {
    accessToken: `${encode({ alg: 'HS256', typ: 'JWT' })}.${encode({ sub: '3', email: 'person@example.invalid', role, exp: now + 1800, nbf: now, ...overrides })}.${btoa('test-signature').replace(/=/g, '')}`,
    tokenType: 'Bearer',
    expiresIn,
  };
}
export function signIn(
  auth: AuthService,
  http: HttpTestingController,
  role: UserRole = 'Admin',
  overrides: Record<string, unknown> = {},
  expiresIn = 1800,
) {
  const response = loginResponse(role, overrides, expiresIn);
  auth.login({ email: 'person@example.invalid', password: 'unit-test-only-value' }).subscribe();
  http.expectOne('/api/auth/login').flush(response);
  return response;
}
