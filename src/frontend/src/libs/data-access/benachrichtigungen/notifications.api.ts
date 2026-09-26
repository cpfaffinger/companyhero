// Datenzugriff der Benachrichtigungen (Benachrichtigungen 3, 6.1, 7.2; K11): Zentrum, persönliche Einstellungen, Geräte,
// Web-Push-Abonnement über den eigenen Service Worker (push-sw.js) mit dem VAPID-Schlüssel der Plattform. Der Browser-Prompt
// erscheint erst nach dem Tap auf „Benachrichtigungen einschalten“ (Benachrichtigungen 3.2).
import { inject, Injectable, signal } from '@angular/core';
import { from, map, Observable, switchMap, tap } from 'rxjs';
import { ApiClient } from '../../api-client/api-client';
import type { components } from '../../api-client/generated/api';

export type Notification = components['schemas']['NotificationResponse'];
export type PersonNotificationSettings = components['schemas']['PersonNotificationSettingsResponse'];
export type PersonNotificationSettingsRequest = components['schemas']['PersonNotificationSettingsRequest'];
export type PushSubscriptionInfo = components['schemas']['PushSubscriptionResponse'];

export type PushZustand = 'nicht_verfuegbar' | 'aus' | 'verweigert' | 'an';

@Injectable({ providedIn: 'root' })
export class NotificationsApi {
  private readonly api = inject(ApiClient);

  readonly entries = signal<Notification[] | null>(null);
  readonly settings = signal<PersonNotificationSettings | null>(null);
  readonly subscriptions = signal<PushSubscriptionInfo[] | null>(null);

  list(unreadOnly = false): Observable<Notification[]> {
    return this.api.get('/api/notifications', unreadOnly ? { query: { unreadOnly: 'true' } } : undefined).pipe(tap((e) => this.entries.set(e)));
  }

  markRead(entryId: string): Observable<void> {
    return this.api.post('/api/notifications/{entryId}/read', null, { path: { entryId } }).pipe(map(() => undefined));
  }

  markAllRead(): Observable<void> {
    return this.api.post('/api/notifications/read-all', null).pipe(map(() => undefined));
  }

  loadSettings(): Observable<PersonNotificationSettings> {
    return this.api.get('/api/me/notifications').pipe(tap((s) => this.settings.set(s)));
  }

  saveSettings(request: PersonNotificationSettingsRequest): Observable<PersonNotificationSettings> {
    return this.api.put('/api/me/notifications', request).pipe(tap((s) => this.settings.set(s)));
  }

  loadSubscriptions(): Observable<PushSubscriptionInfo[]> {
    return this.api.get('/api/me/notifications/subscriptions').pipe(tap((s) => this.subscriptions.set(s)));
  }

  removeSubscription(subscriptionId: string): Observable<void> {
    return this.api.delete('/api/me/notifications/subscriptions/{subscriptionId}', { path: { subscriptionId } }).pipe(map(() => undefined));
  }

  /** Zustand des Push auf diesem Gerät (Benachrichtigungen 3.2 Nr. 5): Verfügbarkeit, Berechtigung. */
  pushZustand(): PushZustand {
    if (typeof Notification === 'undefined' || !('serviceWorker' in navigator) || !('PushManager' in window)) {
      return 'nicht_verfuegbar';
    }
    return Notification.permission === 'denied' ? 'verweigert' : Notification.permission === 'granted' ? 'an' : 'aus';
  }

  /** Einschalten: VAPID-Schlüssel laden, Service Worker registrieren, Berechtigung nach dem Tap, Abonnement an das Backend melden. */
  pushEinschalten(deviceLabel: string): Observable<PushSubscriptionInfo> {
    return this.api.get('/api/notifications/vapid').pipe(
      switchMap((vapid) => {
        if (!vapid.available || !vapid.publicKey) {
          throw new Error('push_not_available');
        }
        return from(subscribeInBrowser(vapid.publicKey));
      }),
      switchMap((subscription) => this.api.post('/api/me/notifications/subscriptions', { ...subscription, deviceLabel })),
      tap(() => this.loadSubscriptions().subscribe()),
    );
  }
}

async function subscribeInBrowser(publicKey: string): Promise<{ endpoint: string; p256dh: string; auth: string }> {
  const registration = await navigator.serviceWorker.register('/push-sw.js', { scope: '/' });
  await navigator.serviceWorker.ready;
  const permission = await Notification.requestPermission();
  if (permission !== 'granted') {
    throw new Error('push_denied');
  }
  const existing = await registration.pushManager.getSubscription();
  const subscription = existing ?? (await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: base64UrlToBytes(publicKey) }));
  const json = subscription.toJSON();
  registration.active?.postMessage({ type: 'ch:vapid', publicKey });
  return { endpoint: subscription.endpoint, p256dh: json.keys?.['p256dh'] ?? '', auth: json.keys?.['auth'] ?? '' };
}

export function base64UrlToBytes(value: string): Uint8Array<ArrayBuffer> {
  const padded = value.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - (value.length % 4)) % 4);
  const raw = atob(padded);
  const bytes = new Uint8Array(new ArrayBuffer(raw.length));
  for (let i = 0; i < raw.length; i++) {
    bytes[i] = raw.charCodeAt(i);
  }
  return bytes;
}
