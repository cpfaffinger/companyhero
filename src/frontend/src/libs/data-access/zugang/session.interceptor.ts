// Interceptor der Sitzung (K11, A-007): 401 einer Anfrage der Mitglieder-App markiert die Sitzung als beendet, damit die Schale
// zum Zugang führt; Anfragen der Anmeldewege selbst (Zugang, Beitritt, Kiosk) behandeln 401 fachlich und bleiben unberührt.
// Jede erfolgreiche Antwort aus einer Mitgliedssitzung verlängert die Offline-Freigabe (Zugang 7).
import { HttpErrorResponse, type HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { tap } from 'rxjs';
import { OfflineFreigabe } from './offline-freigabe';
import { SessionStore } from './session.store';

const ownHandling = ['/api/auth/', '/api/join/', '/api/kiosk/'];

export const sessionInterceptor: HttpInterceptorFn = (request, next) => {
  const store = inject(SessionStore);
  const freigabe = inject(OfflineFreigabe);
  const path = request.url.replace(/^https?:\/\/[^/]+/, '');
  const handlesItself = ownHandling.some((prefix) => path.startsWith(prefix));
  return next(request).pipe(
    tap({
      next: (event) => {
        if (event instanceof HttpResponse && event.ok && path.startsWith('/api/')) {
          const session = store.session();
          if (session && (session.kind === 'member' || session.kind === 'privileged') && session.personId) {
            freigabe.verlaengern(session.tenantId, session.personId);
          }
        }
      },
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 401 && !handlesItself && path.startsWith('/api/')) {
          store.markEnded();
        }
      },
    }),
  );
};
