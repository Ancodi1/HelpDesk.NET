import { HttpErrorResponse } from '@angular/common/http';

export function demoError(error: unknown): string | null {
  if (!(error instanceof HttpErrorResponse)) return null;
  const code: unknown = error.error?.code;
  if (error.status === 409 && code === 'demo_capacity_reached')
    return 'La demo ha alcanzado su capacidad. Puedes seguir consultándola; necesita una restauración.';
  if (error.status === 409 && code === 'demo_busy')
    return 'La demo se está actualizando. Vuelve a intentarlo más tarde.';
  if (error.status === 403 && code === 'demo_registration_disabled')
    return 'El registro está cerrado en la demo. Utiliza una cuenta compartida.';
  return null;
}
