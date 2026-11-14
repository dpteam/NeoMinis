/* =========================================================================
   PushUI - turning the browser's push subscription on and off.

   The server sends an empty tickle (see public/sw.js), so all this has to do is
   register the worker, ask for permission and hand the subscription to the
   server. Everything degrades quietly: no service worker, no push support or a
   denied permission all end in a toast, never in a broken settings page.
   ========================================================================= */
var PushUI = (function () {
  'use strict';

  var registration = null;

  function T(k, a) { return I18N.t(k, a); }
  function byId(id) { return document.getElementById(id); }
  function site() { return App.state.site || {}; }

  /// <summary>True when this browser can do push at all, and the server can sign.</summary>
  function possible() {
    if (!('serviceWorker' in navigator)) return false;
    if (!('PushManager' in window)) return false;
    if (!('Notification' in window)) return false;
    return !!site().pushEnabled;
  }

  function permission() {
    if (!('Notification' in window)) return 'unsupported';
    return Notification.permission;   // default | granted | denied
  }

  /// base64url -> Uint8Array, which is what applicationServerKey wants.
  function keyBytes(base64url) {
    var padding = '='.repeat((4 - base64url.length % 4) % 4);
    var base64 = (base64url + padding).replace(/-/g, '+').replace(/_/g, '/');
    var raw = atob(base64);
    var out = new Uint8Array(raw.length);
    for (var i = 0; i < raw.length; i++) out[i] = raw.charCodeAt(i);
    return out;
  }

  function register() {
    if (registration) return Promise.resolve(registration);
    if (!('serviceWorker' in navigator)) return Promise.reject(new Error('no-sw'));
    return navigator.serviceWorker.register('/sw.js', { scope: '/' }).then(function (reg) {
      registration = reg;
      return reg;
    });
  }

  /// <summary>The subscription this browser currently holds, or null.</summary>
  function current() {
    return register().then(function (reg) {
      return reg.pushManager.getSubscription();
    }).catch(function () { return null; });
  }

  /// <summary>
  /// Turns push on: permission, subscription, and telling the server where to
  /// send it. Returns a promise so the settings page can show progress.
  /// </summary>
  function enable() {
    if (!possible()) return Promise.reject(new Error(T('settings.pushUnsupported')));
    var key = site().pushKey || '';
    if (!key) return Promise.reject(new Error(T('settings.pushUnsupported')));

    return register().then(function (reg) {
      return Notification.requestPermission().then(function (result) {
        if (result !== 'granted') throw new Error(T('settings.pushDenied'));
        return reg.pushManager.getSubscription().then(function (existing) {
          if (existing) return existing;
          return reg.pushManager.subscribe({
            userVisibleOnly: true,
            applicationServerKey: keyBytes(key)
          });
        });
      });
    }).then(function (sub) {
      return postSubscription(sub);
    }).then(function () {
      return true;
    });
  }

  function postSubscription(sub) {
    var json = sub.toJSON ? sub.toJSON() : {};
    var keys = json.keys || {};
    return new Promise(function (resolve, reject) {
      Api.request('POST', '/api/push/subscribe', {
        endpoint: sub.endpoint,
        p256dh: keys.p256dh || '',
        auth: keys.auth || '',
        agent: navigator.userAgent.slice(0, 80)
      }, function (err) {
        if (err) { reject(new Error(Api.errorText(err))); return; }
        resolve(true);
      });
    });
  }

  /// <summary>Turns push off: tell the server first, then drop the subscription.</summary>
  function disable() {
    return current().then(function (sub) {
      if (!sub) return true;
      var endpoint = sub.endpoint;
      return new Promise(function (resolve) {
        Api.request('POST', '/api/push/unsubscribe', { endpoint: endpoint }, function () { resolve(true); });
      }).then(function () {
        return sub.unsubscribe().catch(function () { return true; });
      });
    }).then(function () {
      return true;
    });
  }

  /// <summary>What the settings page shows: on, off, blocked, or not available.</summary>
  function state() {
    if (!possible()) {
      return Promise.resolve(site().pushEnabled ? 'unsupported' : 'off');
    }
    if (permission() === 'denied') return Promise.resolve('blocked');
    return current().then(function (sub) {
      return sub ? 'on' : 'off';
    }).catch(function () { return 'off'; });
  }

  /// <summary>
  /// Called on every page load: if a subscription already exists, make sure the
  /// server knows about it. A browser can keep its subscription across a server
  /// data wipe, and re-posting it costs one request and fixes that silently.
  /// </summary>
  function sync() {
    if (!possible()) return;
    current().then(function (sub) {
      if (sub) postSubscription(sub).catch(function () { });
    }).catch(function () { });
  }

  return {
    possible: possible,
    state: state,
    enable: enable,
    disable: disable,
    sync: sync,
    permission: permission
  };
})();
