import { HttpErrorResponse } from '@angular/common/http';
import { TimeoutError } from 'rxjs';

export function authError(error: unknown, action: 'login' | 'register'): string {
  if (error instanceof TimeoutError) return 'El servidor tarda en responder. Inténtalo de nuevo.';
  if (error instanceof HttpErrorResponse) {
    switch (error.status) {
      case 0:
        return 'No se pudo conectar con el servidor. Comprueba la conexión e inténtalo de nuevo.';
      case 400:
        return 'Revisa los campos. Los datos no cumplen los requisitos del servidor.';
      case 401:
        return action === 'login'
          ? 'Correo o contraseña incorrectos.'
          : 'No se pudo autorizar la solicitud.';
      case 403:
        return 'No tienes permiso para realizar esta operación.';
      case 409:
        return action === 'register'
          ? 'Ya existe una cuenta con ese correo. Puedes iniciar sesión.'
          : 'La solicitud entra en conflicto con el estado actual. Inténtalo de nuevo.';
    }
  }
  // Never echo the provider response, request, credentials or token.
  return 'No se pudo completar la operación. Inténtalo de nuevo más tarde.';
}
