import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, timeout } from 'rxjs';

export interface TicketComment {
  id: number;
  ticketId: number;
  authorUserId: number;
  authorName: string;
  content: string;
  createdAt: string;
}
export interface CommentPage {
  items: TicketComment[];
  hasMore: boolean;
  nextBeforeId: number | null;
  pageSize: number;
}
@Injectable({ providedIn: 'root' })
export class CommentService {
  private readonly http = inject(HttpClient);
  list(ticketId: number, beforeId: number | null = null, pageSize = 20) {
    let params = new HttpParams().set('pageSize', pageSize);
    if (beforeId !== null) params = params.set('beforeId', beforeId);
    return this.http
      .get<unknown>(`/api/tickets/${ticketId}/comments`, { params, cache: 'no-store' })
      .pipe(
        timeout(15_000),
        map((value) => {
          const page = value as CommentPage | null;
          if (
            !page ||
            !Array.isArray(page.items) ||
            page.pageSize !== pageSize ||
            typeof page.hasMore !== 'boolean' ||
            page.items.length > pageSize ||
            (page.hasMore && page.items.length !== pageSize)
          )
            throw new Error('Unexpected comment page');
          const items = page.items.map((item) => readComment(item, ticketId));
          if (
            items.some(
              (item, index) =>
                (beforeId !== null && item.id >= beforeId) ||
                (index > 0 && item.id >= items[index - 1].id),
            ) ||
            page.nextBeforeId !== (page.hasMore ? items.at(-1)?.id : null)
          )
            throw new Error('Unexpected comment cursor');
          return { items, pageSize, hasMore: page.hasMore, nextBeforeId: page.nextBeforeId };
        }),
      );
  }
  create(ticketId: number, content: string) {
    return this.http
      .post<unknown>(`/api/tickets/${ticketId}/comments`, { content }, { observe: 'response' })
      .pipe(
        timeout(15_000),
        map((response) => {
          if (response.status !== 201) throw new Error('Unexpected comment confirmation');
          return readComment(response.body, ticketId);
        }),
      );
  }
}
function readComment(value: unknown, ticketId: number): TicketComment {
  const c = value as TicketComment | null;
  const id = (value: unknown) =>
    typeof value === 'number' && Number.isInteger(value) && value > 0 && value <= 2147483647;
  if (
    !c ||
    !id(c.id) ||
    c.ticketId !== ticketId ||
    !id(c.authorUserId) ||
    typeof c.authorName !== 'string' ||
    typeof c.content !== 'string' ||
    typeof c.createdAt !== 'string' ||
    !Number.isFinite(Date.parse(c.createdAt))
  )
    throw new Error('Unexpected comment response');
  return {
    id: c.id,
    ticketId,
    authorUserId: c.authorUserId,
    authorName: c.authorName,
    content: c.content,
    createdAt: c.createdAt,
  };
}
export function commentError(error: unknown, writing = false): string {
  const code = error instanceof HttpErrorResponse ? error.status : 0;
  const messages: Record<number, string> = {
    400: 'Revisa el comentario (texto obligatorio de hasta 4.000 unidades UTF-16) o la paginación.',
    401: 'Tu sesión ya no es válida. Inicia sesión de nuevo.',
    403: 'No tienes permiso para acceder a estos comentarios.',
    404: 'La incidencia no existe o ya no está disponible para tu cuenta.',
    409: 'No se pudo publicar por un conflicto. Actualiza los comentarios y revisa tu sesión.',
  };
  return (
    messages[code] ??
    (writing
      ? 'No se pudo confirmar la publicación. El comentario podría haberse guardado.'
      : 'No se pudieron cargar los comentarios. Puedes volver a intentarlo.')
  );
}
