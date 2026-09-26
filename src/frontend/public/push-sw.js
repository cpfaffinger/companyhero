// Service Worker für Web Push (Benachrichtigungen 3.1, 3.3; A-059): die Nutzlast enthält nur { id, kategorie, ziel }. Titel und
// Text lädt der Worker über die API mit der Sitzung (Cookie); ohne gültige Sitzung erscheint eine neutrale Nachricht mit dem
// Produktnamen, und das Tippen öffnet die Anmeldung. Bei pushsubscriptionchange erneuert der Worker das Abonnement ohne
// Nutzeraktion und meldet es erneut (3.2 Nr. 4). Der Worker cacht nichts und liest keine Personendaten Dritter.
const DEFAULT_TITLE = 'CompanyHero';
let vapidPublicKey = null;

self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()));

self.addEventListener('message', (event) => {
  if (event.data && event.data.type === 'ch:vapid') {
    vapidPublicKey = event.data.publicKey;
  }
});

self.addEventListener('push', (event) => {
  let payload = null;
  try {
    payload = event.data ? event.data.json() : null;
  } catch {
    payload = null;
  }
  event.waitUntil(show(payload));
});

async function show(payload) {
  const target = payload && typeof payload.ziel === 'string' ? payload.ziel : '/';
  const id = payload && typeof payload.id === 'string' ? payload.id : null;
  let title = DEFAULT_TITLE;
  let body = '';
  try {
    const theme = await fetch('/api/branding/theme', { credentials: 'include' });
    if (theme.ok) {
      const t = await theme.json();
      title = t.produktname || title;
      if (id) {
        const list = await fetch('/api/notifications?unreadOnly=true', { credentials: 'include' });
        if (list.ok) {
          const entry = (await list.json()).find((e) => e.id === id);
          if (entry) {
            body = resolve(t.texte[entry.textKey] || entry.textKey, entry.params);
          }
        }
      }
    }
  } catch {
    // Ohne Sitzung oder ohne Netz: neutrale Anzeige mit Produktname (Benachrichtigungen 3.1).
  }
  const kategorie = payload && payload.kategorie ? String(payload.kategorie) : 'allgemein';
  return self.registration.showNotification(title, { body, tag: kategorie + ':' + target, data: { target, id }, icon: '/favicon.ico' });
}

function resolve(text, params) {
  return String(text).replace(/\{(\w+)\}/g, (match, name) => (params && params[name] !== undefined ? String(params[name]) : match));
}

self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const target = (event.notification.data && event.notification.data.target) || '/';
  const path = target.startsWith('/challenges') ? '/t/_/challenges' : target === '/ich' ? '/t/_/ich' : '/t/_/benachrichtigungen';
  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clients) => {
      const existing = clients.find((c) => 'focus' in c);
      if (existing) {
        existing.navigate(path);
        return existing.focus();
      }
      return self.clients.openWindow(path);
    }),
  );
});

self.addEventListener('pushsubscriptionchange', (event) => {
  const key = (event.oldSubscription && event.oldSubscription.options && event.oldSubscription.options.applicationServerKey) || vapidPublicKey;
  if (!key) {
    return;
  }
  event.waitUntil(
    self.registration.pushManager
      .subscribe({ userVisibleOnly: true, applicationServerKey: key })
      .then((subscription) => {
        const json = subscription.toJSON();
        return fetch('/api/me/notifications/subscriptions', {
          method: 'POST',
          credentials: 'include',
          headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': csrfToken() },
          body: JSON.stringify({ endpoint: subscription.endpoint, p256dh: json.keys && json.keys.p256dh, auth: json.keys && json.keys.auth, deviceLabel: null }),
        });
      })
      .catch(() => undefined),
  );
});

function csrfToken() {
  // Der Worker sieht keine Cookies; das Backend akzeptiert die Erneuerung nur mit gültiger Sitzung, der Token kommt vom nächsten App-Start.
  return '';
}
