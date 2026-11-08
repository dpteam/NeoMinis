/* =========================================================================
   Api - encrypted transport for /api/. Every authenticated call is signed
   (HMAC-SHA256) and sealed (HMAC-CTR + encrypt-then-MAC). The session key
   lives in sessionStorage only, never in localStorage or a URL.
   ========================================================================= */
var Api = (function () {
  'use strict';

  var KEY_STORE = 'social.key';
  var api = {
    site: null,
    hs: null,
    user: null,
    me: null,
    key: null,
    keyHex: '',
    timeOffset: 0,
    lastRecovery: '',
    onAuthLost: null,
    busy: false
  };

  function nowTs() {
    return Math.floor(Date.now() / 1000) + (api.timeOffset || 0);
  }

  function randomHex(n) { return DptCrypto.randomHex(n); }

  function xhr(method, url, body, headers, cb) {
    var req = new XMLHttpRequest();
    req.open(method, url, true);
    req.timeout = 30000;
    if (headers) {
      for (var k in headers) {
        if (headers.hasOwnProperty(k)) {
          try { req.setRequestHeader(k, headers[k]); } catch (e) { }
        }
      }
    }
    req.onreadystatechange = function () {
      if (req.readyState !== 4) return;
      if (req.status === 0) {
        cb({ status: 0, code: 'network', message: I18N.t('errors.network') });
        return;
      }
      cb(null, req);
    };
    req.ontimeout = function () {
      cb({ status: 0, code: 'network', message: I18N.t('errors.network') });
    };
    try { req.send(body === undefined ? null : body); }
    catch (e) { cb({ status: 0, code: 'network', message: String(e && e.message || e) }); }
  }

  function parseJson(text) {
    try { return JSON.parse(text); } catch (e) { return null; }
  }

  function decodeResponse(req, key) {
    var enc = req.getResponseHeader('X-Api-Enc');
    var text = req.responseText || '';
    if (enc === '1') {
      var plain = DptCrypto.open(key, text);
      if (!plain) return null;
      text = DptCrypto.utf8Decode(plain);
    }
    return parseJson(text);
  }

  function apiError(status, data) {
    return {
      status: status || 0,
      code: (data && data.error) || 'generic',
      message: (data && data.message) || '',
      data: data || {}
    };
  }

  function request(method, path, bodyObj, cb) {
    if (!api.key) {
      cb({ status: 401, code: 'auth-required', message: I18N.t('errors.session') });
      return;
    }
    var ts = String(nowTs());
    var nonce = randomHex(16);
    var plain = bodyObj === undefined || bodyObj === null ? '' : JSON.stringify(bodyObj);
    var wire = '';
    // GET without a body stays bodyless; everything else carries a sealed envelope.
    if (plain !== '' || method !== 'GET') {
      wire = DptCrypto.seal(api.key, DptCrypto.utf8(plain || '{}'));
    }
    var sig = DptCrypto.signRequest(api.key, method, path, ts, nonce, DptCrypto.utf8(wire));
    var headers = {
      'X-Api-Ts': ts,
      'X-Api-Nonce': nonce,
      'X-Api-Sig': sig,
      'X-Api-Enc': '1',
      'Accept': 'application/json'
    };
    if (wire) headers['Content-Type'] = 'text/plain; charset=utf-8';

    xhr(method, path, wire || null, headers, function (err, req) {
      if (err) { cb(err); return; }
      var data = decodeResponse(req, api.key);
      if (!data) {
        cb({ status: req.status, code: 'bad-envelope', message: I18N.t('errors.generic') });
        return;
      }
      if (req.status === 401 && (data.error === 'auth-required' || data.error === 'bad-signature' || data.error === 'replay' || data.error === 'stale')) {
        clearSession();
        if (api.onAuthLost) api.onAuthLost();
        cb(apiError(req.status, data));
        return;
      }
      if (req.status < 200 || req.status >= 300 || data.ok === false) {
        cb(apiError(req.status, data));
        return;
      }
      cb(null, data);
    });
  }

  function authRequest(path, authHeaders, k0, bodyObj, cb) {
    var ts = String(nowTs());
    var nonce = randomHex(16);
    var plain = JSON.stringify(bodyObj || {});
    var wire = DptCrypto.seal(k0, DptCrypto.utf8(plain));
    var sig = DptCrypto.signRequest(k0, 'POST', path, ts, nonce, DptCrypto.utf8(wire));
    var headers = {
      'X-Api-Ts': ts,
      'X-Api-Nonce': nonce,
      'X-Api-Sig': sig,
      'X-Api-Enc': '1',
      'Content-Type': 'text/plain; charset=utf-8',
      'Accept': 'application/json'
    };
    for (var k in authHeaders) if (authHeaders.hasOwnProperty(k)) headers[k] = authHeaders[k];

    xhr('POST', path, wire, headers, function (err, req) {
      if (err) { cb(err); return; }
      var data = decodeResponse(req, k0);
      if (!data) {
        cb({ status: req.status, code: 'bad-envelope', message: I18N.t('errors.generic') });
        return;
      }
      if (req.status < 200 || req.status >= 300 || data.ok === false) {
        cb(apiError(req.status, data));
        return;
      }
      cb(null, data);
    });
  }

  function loadHandshake(cb) {
    xhr('GET', '/api/handshake', null, { 'Accept': 'application/json' }, function (err, req) {
      if (err) { cb(err); return; }
      var data = parseJson(req.responseText);
      if (!data || !data.ok) {
        cb({ status: req.status, code: 'handshake', message: I18N.t('errors.generic') });
        return;
      }
      api.hs = data;
      api.site = data.site || {};      api.timeOffset = Number(data.serverTimeUnix || 0) - Math.floor(Date.now() / 1000);
      cb(null, data);
    });
  }

  function storeSession(verifier, sessionSalt, data) {
    api.key = DptCrypto.deriveKey(verifier, sessionSalt, (api.hs && api.hs.kdfIterations) || 12000);
    api.keyHex = DptCrypto.bytesToHex(api.key);
    try { sessionStorage.setItem(KEY_STORE, api.keyHex); } catch (e) { }
    api.user = data.user || null;
    api.me = data.me || null;
    if (data.site) api.site = data.site;
    if (data.recoveryCode) api.lastRecovery = data.recoveryCode;
  }

  function restoreSession() {
    try {
      var hex = sessionStorage.getItem(KEY_STORE);
      if (!hex || hex.length !== 64) return false;
      api.key = DptCrypto.hexToBytes(hex);
      api.keyHex = hex;
      return true;
    } catch (e) { return false; }
  }

  function clearSession() {
    api.key = null;
    api.keyHex = '';
    api.user = null;
    api.me = null;
    try { sessionStorage.removeItem(KEY_STORE); } catch (e) { }
  }

  function bootstrap(cb) {
    loadHandshake(function (err) {
      if (err) { cb(err, false); return; }
      if (!restoreSession()) { cb(null, false); return; }
      request('GET', '/api/me', null, function (err2, data) {
        if (err2) {
          clearSession();
          cb(null, false);
          return;
        }
        api.me = data.me || null;
        api.user = (data.me && data.me.user) || null;
        if (data.site) api.site = data.site;
        cb(null, true);
      });
    });
  }

  function login(username, password, captcha, cb) {
    loadHandshake(function (err, hs) {
      if (err) { cb(err); return; }
      var user = String(username || '').trim().toLowerCase();
      var iter = hs.kdfIterations || 12000;
      var verifier;
      try { verifier = DptCrypto.deriveVerifier(password, user, iter); }
      catch (e) { cb({ code: 'crypto', message: I18N.t('errors.generic') }); return; }
      var salt = randomHex(16);
      var k0 = DptCrypto.deriveKey(verifier, salt, iter);
      var body = {};
      if (captcha && captcha.id) { body.captchaId = captcha.id; body.captcha = captcha.code; }
      authRequest('/api/login', {
        'X-Auth-User': user,
        'X-Auth-Verifier': verifier,
        'X-Auth-Salt': salt
      }, k0, body, function (err2, data) {
        if (err2) { cb(err2); return; }
        storeSession(verifier, data.sessionSalt, data);
        cb(null, data);
      });
    });
  }

  function register(form, cb) {
    loadHandshake(function (err, hs) {
      if (err) { cb(err); return; }
      var user = String(form.username || '').trim().toLowerCase();
      var iter = hs.kdfIterations || 12000;
      var verifier;
      try { verifier = DptCrypto.deriveVerifier(form.password, user, iter); }
      catch (e) { cb({ code: 'crypto', message: I18N.t('errors.generic') }); return; }
      var salt = randomHex(16);
      var k0 = DptCrypto.deriveKey(verifier, salt, iter);
      var body = {
        displayName: form.displayName || user,
        captchaId: form.captchaId || '',
        captcha: form.captcha || '',
        invite: form.invite || ''
      };
      authRequest('/api/register', {
        'X-Auth-User': user,
        'X-Auth-Verifier': verifier,
        'X-Auth-Salt': salt
      }, k0, body, function (err2, data) {
        if (err2) { cb(err2); return; }
        storeSession(verifier, data.sessionSalt, data);
        api.lastRecovery = data.recoveryCode || '';
        cb(null, data);
      });
    });
  }

  function recover(username, code, newPassword, cb) {
    loadHandshake(function (err, hs) {
      if (err) { cb(err); return; }
      var user = String(username || '').trim().toLowerCase();
      var iter = hs.kdfIterations || 12000;
      var verifier;
      try { verifier = DptCrypto.deriveVerifier(newPassword, user, iter); }
      catch (e) { cb({ code: 'crypto', message: I18N.t('errors.generic') }); return; }
      var salt = randomHex(16);
      var k0 = DptCrypto.deriveKey(String(code || '').trim().toUpperCase(), salt, iter);
      authRequest('/api/recover', {
        'X-Auth-User': user,
        'X-Auth-Recover': String(code || '').trim().toUpperCase(),
        'X-Auth-Salt': salt
      }, k0, { newVerifier: verifier }, function (err2, data) {
        if (err2) { cb(err2); return; }
        cb(null, data);
      });
    });
  }

  function logout(cb) {
    request('POST', '/api/logout', {}, function () {
      clearSession();
      if (cb) cb();
    });
  }

  function errorText(err) {
    if (!err) return I18N.t('errors.generic');
    var code = err.code || '';
    if (code === 'network') return I18N.t('errors.network');
    if (code === 'auth-required') return I18N.t('errors.session');
    if (code === 'rate-limit' || code === 'slow-down') return I18N.t('errors.rateLimit');
    if (code === 'read-only') return I18N.t('errors.readOnly');
    if (code === 'forbidden' || code === 'not-member' || code === 'not-friend') return I18N.t('errors.forbidden');
    if (code === 'notfound' || code === 'no-user' || code === 'no-post' || code === 'no-comment' || code === 'no-group' || code === 'no-message' || code === 'no-report') return I18N.t('errors.notFound');
    if (code === 'captcha') return I18N.t('auth.captchaWrong');
    if (code === 'bad-credentials') return I18N.t('auth.badCredentials');
    if (code === 'locked') return I18N.t('auth.locked');
    if (code === 'banned') return err.message || I18N.t('auth.banned');
    if (code === 'private') return I18N.t('profile.private');
    if (err.message) return err.message;
    return I18N.t('errors.generic');
  }

  api.request = request;
  api.authRequest = authRequest;
  api.handshake = loadHandshake;
  api.bootstrap = bootstrap;
  api.login = login;
  api.register = register;
  api.recover = recover;
  api.logout = logout;
  api.clearSession = clearSession;
  api.errorText = errorText;
  api.decodeResponse = decodeResponse;
  api.parseJson = parseJson;

  return api;
})();
