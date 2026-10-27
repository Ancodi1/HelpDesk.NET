import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable, timeout } from 'rxjs';

export interface HealthResponse {
  status: 'ok';
}

@Injectable({ providedIn: 'root' })
export class HealthService {
  private readonly http = inject(HttpClient);

  check(): Observable<HealthResponse> {
    return this.http.get<unknown>('/api/health').pipe(
      timeout(8_000),
      map((response) => {
        // HttpClient's generic types do not validate the actual JSON response.
        if (
          typeof response !== 'object' ||
          response === null ||
          !('status' in response) ||
          response.status !== 'ok'
        ) {
          throw new Error('Unexpected health response');
        }
        return { status: 'ok' };
      }),
    );
  }
}
