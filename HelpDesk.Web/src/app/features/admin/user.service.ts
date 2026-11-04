import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, timeout } from 'rxjs';
import { PublicUser, UserRole } from '../../core/auth/auth.models';

export const userRoles: readonly UserRole[] = ['User', 'Technician', 'Admin'];
export type UserUpdate = Pick<PublicUser, 'name' | 'email' | 'role'>;

@Injectable({ providedIn: 'root' })
export class UserService {
  private readonly http = inject(HttpClient);
  list() {
    return this.http.get<unknown>('/api/users', { cache: 'no-store' }).pipe(
      timeout(15_000),
      map((value) => {
        if (!Array.isArray(value)) throw new Error('Unexpected directory response');
        const users = value.map(readUser);
        if (new Set(users.map((u) => u.id)).size !== users.length)
          throw new Error('Duplicate user ID');
        return users;
      }),
    );
  }
  update(id: number, input: UserUpdate) {
    const body = { name: input.name, email: input.email.trim().toLowerCase(), role: input.role };
    return this.http.put<unknown>(`/api/users/${id}`, body).pipe(
      timeout(15_000),
      map((value) => {
        const user = readUser(value);
        if (user.id !== id) throw new Error('Unexpected user ID');
        return user;
      }),
    );
  }
}

function readUser(value: unknown): PublicUser {
  if (typeof value !== 'object' || value === null) throw new Error('Unexpected user response');
  const u = value as Record<string, unknown>;
  if (
    !Number.isInteger(u['id']) ||
    (u['id'] as number) < 1 ||
    (u['id'] as number) > 2147483647 ||
    typeof u['name'] !== 'string' ||
    typeof u['email'] !== 'string' ||
    !userRoles.includes(u['role'] as UserRole) ||
    typeof u['createdAt'] !== 'string' ||
    !Number.isFinite(Date.parse(u['createdAt']))
  )
    throw new Error('Unexpected user response');
  // Keep only the public DTO, never incidental provider fields.
  return {
    id: u['id'] as number,
    name: u['name'],
    email: u['email'],
    role: u['role'] as UserRole,
    createdAt: u['createdAt'],
  };
}

export function userError(error: unknown, writing = false): string {
  const status = error instanceof HttpErrorResponse ? error.status : 0;
  const messages: Record<number, string> = {
    400: 'El servidor no acepta los datos. Revisa nombre, correo y rol.',
    401: 'Tu sesión ya no es válida. Inicia sesión de nuevo.',
    403: 'No tienes permiso para administrar usuarios. Si tu rol cambió, inicia sesión de nuevo.',
    404: 'El usuario ya no está disponible. Actualiza el directorio.',
    409: 'Conflicto: el correo puede estar ocupado o el usuario tiene tickets asignados y no puede pasar a Usuario.',
  };
  return (
    messages[status] ??
    (writing
      ? 'No se pudo confirmar el cambio. Actualiza el directorio antes de reenviar: podría haberse guardado.'
      : 'No se pudo cargar el directorio. Puedes volver a intentarlo.')
  );
}
