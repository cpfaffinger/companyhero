import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { sessionInterceptor } from '../libs/data-access/zugang/session.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    // Icons einheitlich als selbst ausgelieferte Material Symbols Rounded über die Schriftklasse (Marke 2.6, K09).
    // Sitzung als HttpOnly-Cookie, CSRF-Token aus dem lesbaren Cookie `ch_csrf` als Header `X-CSRF-Token` bei jedem
    // zustandsändernden Request (A-007); 401 behandelt der Client (K11).
    provideHttpClient(withFetch(), withXsrfConfiguration({ cookieName: 'ch_csrf', headerName: 'X-CSRF-Token' }), withInterceptors([sessionInterceptor])),
  ],
};
