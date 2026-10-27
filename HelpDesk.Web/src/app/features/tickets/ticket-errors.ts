import { HttpErrorResponse } from '@angular/common/http';
import { TimeoutError } from 'rxjs';
import { TicketCreationResponseError } from './ticket.service';

export function ticketError(error: unknown, action: 'create' | 'detail'): string {
  const uncertain =
    ' No reenvíes la solicitud sin revisar antes el listado: el ticket podría haberse creado.';
  if (error instanceof TicketCreationResponseError)
    return 'No pudimos interpretar la confirmación del servidor. Revisa el listado antes de crear otro ticket.';
  if (error instanceof TimeoutError)
    return (
      'El servidor tarda en responder.' + (action === 'create' ? uncertain : ' Inténtalo de nuevo.')
    );
  if (error instanceof HttpErrorResponse) {
    switch (error.status) {
      case 0:
        return (
          'No se pudo conectar con el servidor.' +
          (action === 'create' ? uncertain : ' Comprueba la conexión y vuelve a intentarlo.')
        );
      case 400:
        return action === 'create'
          ? 'El servidor no acepta estos datos. Revisa el título, la descripción, la prioridad y la categoría.'
          : 'No se pudo consultar esta incidencia. Revisa su dirección.';
      case 401:
        return 'Tu sesión ya no es válida. Inicia sesión de nuevo.';
      case 403:
        return action === 'create'
          ? 'No tienes permiso para crear incidencias.'
          : 'No tienes permiso para consultar esta incidencia.';
      case 404:
        return 'No se encontró la incidencia o no está disponible para tu cuenta.';
      case 409:
        return 'La solicitud entra en conflicto con el estado actual. Revisa tu sesión antes de intentarlo de nuevo.';
    }
  }
  return (
    'No se pudo completar la operación.' +
    (action === 'create' ? uncertain : ' Inténtalo de nuevo más tarde.')
  );
}
