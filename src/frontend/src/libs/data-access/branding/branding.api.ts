// Datenzugriff der Domäne Marke und Theme (K11): ein gemeinsamer Serverdatenzugriff je Bereich, RxJS für die Abläufe.
// Lädt den Tokensatz des Tenants und der Plattform über den generierten Vertrag und übergibt ihn dem Theme-Service.
import { inject, Injectable } from '@angular/core';
import { Observable, shareReplay, tap } from 'rxjs';
import { ApiClient } from '../../api-client/api-client';
import type { components } from '../../api-client/generated/api';
import { ThemeService } from '../../theme/theme.service';

type ThemeDocumentRequest = components['schemas']['ThemeDocumentRequest'];
type ThemePreviewResponse = components['schemas']['ThemePreviewResponse'];
type ThemeResponse = components['schemas']['ThemeResponse'];
type PlatformThemeResponse = components['schemas']['PlatformThemeResponse'];

@Injectable({ providedIn: 'root' })
export class BrandingApi {
  private readonly api = inject(ApiClient);
  private readonly theme = inject(ThemeService);
  private tenant$?: Observable<ThemeResponse>;
  private platform$?: Observable<PlatformThemeResponse>;

  /** Gültiges Theme des Tenants; ein Request je App-Start, alle Verbraucher teilen ihn. */
  tenantTheme(): Observable<ThemeResponse> {
    this.tenant$ ??= this.api.get('/api/branding/theme').pipe(
      tap((theme) => this.theme.tenantTheme.set(theme)),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    return this.tenant$;
  }

  /** Plattformmarke für die Plattform-Schale und die Anmeldeseite vor Zuordnung; ohne Sitzung abrufbar. */
  platformTheme(): Observable<PlatformThemeResponse> {
    this.platform$ ??= this.api.get('/api/branding/platform').pipe(
      tap((theme) => this.theme.platformTheme.set(theme)),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    return this.platform$;
  }

  /** Verwaltung: Ableitung ohne Persistierung mit Kontrastbericht (A-013). */
  preview(document: ThemeDocumentRequest): Observable<ThemePreviewResponse> {
    return this.api.post('/api/branding/theme/preview', document);
  }

  /** Verwaltung: Veröffentlichen als neue Version; der Tokensatz wechselt zur Laufzeit ohne Neuladen (Marke 3.4). */
  publish(document: ThemeDocumentRequest): Observable<ThemeResponse> {
    return this.api.put('/api/branding/theme', document).pipe(
      tap((theme) => {
        this.theme.tenantTheme.set(theme);
        this.tenant$ = undefined;
      }),
    );
  }
}
