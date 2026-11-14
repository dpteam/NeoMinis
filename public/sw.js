/* =========================================================================
   Service worker: the only thing that runs when the tab is closed.

   The server sends an empty "tickle" push, because encrypting a payload would
   need AES-GCM, which this server's framework does not have. So the worker wakes
   up, asks the site what is waiting (the request carries the session cookie like
   any other), and shows what it finds.
   ========================================================================= */

self.addEventListener('install', function () {
  self.skipWaiting();
});

self.addEventListener('activate', function (event) {
  event.waitUntil(self.clients.claim());
});

self.addEventListener('push', function (event) {
  event.waitUntil(deliver());
});

function deliver() {
  return fetch('/api/push/pending', {
    credentials: 'same-origin',
    headers: { 'Accept': 'application/json' }
  }).then(function (res) {
    if (!res.ok) return null;
    return res.json();
  }).then(function (data) {
    if (!data || !data.items || !data.items.length) {
      // nothing new: still show something, otherwise the browser may punish the
      // subscription for a push that produced no notification
      return showOne({
        id: 0,
        text: 'You have a new notification',
        link: '#/notifications',
        actor: null
      });
    }
    var chain = Promise.resolve();
    data.items.forEach(function (item) {
      chain = chain.then(function () { return showOne(item); });
    });
    return chain;
  }).catch(function () { });
}

function showOne(item) {
  var tag = 'notif-' + (item.id || 'x');
  // The same notification must not stack up: a repeated tickle finds its tag and
  // does nothing.
  return self.registration.getNotifications({ tag: tag }).then(function (existing) {
    if (existing && existing.length) return;
    var who = item.actor && (item.actor.displayName || item.actor.username);
    var title = who ? who : 'Notification';
    var body = item.text || '';
    var link = item.link || '#/notifications';
    return self.registration.showNotification(title, {
      body: body,
      tag: tag,
      renotify: false,
      icon: '/favicon.ico',
      badge: '/favicon.ico',
      data: { link: link }
    });
  });
}

self.addEventListener('notificationclick', function (event) {
  event.notification.close();
  var link = (event.notification.data && event.notification.data.link) || '#/notifications';
  var url = new URL(link, self.location.origin).href;

  event.waitUntil(self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(function (list) {
    for (var i = 0; i < list.length; i++) {
      var client = list[i];
      if (client.url.indexOf(self.location.origin) !== 0) continue;
      // a tab is already open: focus it and move it to the right place
      if ('navigate' in client) {
        return client.navigate(url).then(function () { return client.focus(); }).catch(function () {
          return client.focus();
        });
      }
      return client.focus();
    }
    return self.clients.openWindow(url);
  }));
});
