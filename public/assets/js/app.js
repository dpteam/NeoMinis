/* =========================================================================
   Social SPA - core: boot, auth, shell, router, feed, post, profile.
   ES5 only. All user supplied strings are escaped through UI.esc/linkify.
   ========================================================================= */
var App = (function () {
  'use strict';

  function T(k, a, b, c) { return I18N.t(k, a, b, c); }

  var state = {
    authed: false,
    route: '',
    parts: [],
    me: null,
    user: null,
    site: null,
    feed: { tab: 'home', posts: [], next: 0, hasMore: false, loading: false },
    im: { withId: 0, timer: null },
    adminLogs: { timer: null },
    booted: false
  };

  var views = {};

  function root() { return document.getElementById('app'); }

  function go(hash) {
    if (location.hash === hash) { route(); return; }
    location.hash = hash;
  }

  function parseHash() {
    var h = String(location.hash || '').replace(/^#\/?/, '');
    if (!h) h = 'feed';
    var q = h.indexOf('?');
    var query = '';
    if (q >= 0) { query = h.substring(q + 1); h = h.substring(0, q); }
    var parts = h.split('/').filter(function (x) { return x.length > 0; });
    return { path: parts[0] || 'feed', parts: parts, query: query };
  }

  function qs(name) {
    var raw = parseHash().query || '';
    var parts = raw.split('&');
    for (var i = 0; i < parts.length; i++) {
      if (!parts[i]) continue;
      var eq = parts[i].indexOf('=');
      var key = eq < 0 ? parts[i] : parts[i].substring(0, eq);
      if (key !== name) continue;
      var value = eq < 0 ? '' : parts[i].substring(eq + 1);
      try { return decodeURIComponent(value.replace(/\+/g, ' ')); } catch (e) { return value; }
    }
    return '';
  }

  // ------------------------------------------------------------------ boot

  function init() {
    UI.applyTheme(UI.getTheme());
    I18N.setLang(I18N.detect((Api.site && Api.site.defaultLanguage) || 'ru'));
    bindGlobal();
    var bootText = document.getElementById('boot-text');
    if (bootText) bootText.textContent = T('app.loading');

    Api.bootstrap(function (err, authed) {
      state.booted = true;
      state.authed = !!authed;
      if (Api.site) {
        var savedTheme = null, savedLang = null;
        try { savedTheme = localStorage.getItem('social.theme'); savedLang = localStorage.getItem('social.lang'); } catch (e) { }
        if (!savedTheme && Api.site.defaultTheme) UI.applyTheme(Api.site.defaultTheme);
        if (!savedLang && Api.site.defaultLanguage) I18N.setLang(Api.site.defaultLanguage);
      }
      if (authed) {
        state.me = Api.me;
        state.user = (Api.me && Api.me.user) || Api.user;
        state.site = Api.site;
        applyUserPrefs();
        renderShell();
        route();
        startBadgePoll();
      } else {
        renderAuth('login');
      }
      hideBoot();
    });
  }

  function hideBoot() {
    var boot = document.getElementById('boot');
    if (!boot) return;
    boot.className = 'boot done';
    setTimeout(function () { if (boot.parentNode) boot.parentNode.removeChild(boot); }, 400);
  }

  function applyUserPrefs() {
    var u = state.user;
    if (!u) return;
    if (u.theme && UI.themes.indexOf(u.theme) >= 0) UI.applyTheme(u.theme);
    if (u.lang && I18N.langs.indexOf(u.lang) >= 0) I18N.setLang(u.lang);
  }

  function startBadgePoll() {
    setInterval(function () {
      if (!state.authed || document.hidden) return;
      Api.request('GET', '/api/me', null, function (err, data) {
        if (err) return;
        state.me = data.me || state.me;
        state.user = (data.me && data.me.user) || state.user;
        updateBadges();
      });
    }, 20000);
  }

  function updateBadges() {
    var me = state.me || {};
    setBadge('badge-notif', me.unreadNotifications);
    setBadge('badge-msg', me.unreadMessages);
    setBadge('badge-friends', me.friendRequests);
  }

  function setBadge(id, n) {
    var el = document.getElementById(id);
    if (!el) return;
    if (n > 0) { el.textContent = n > 99 ? '99+' : String(n); el.className = 'count'; }
    else { el.textContent = ''; el.className = 'hidden'; }
  }

  // ------------------------------------------------------------------ shell

  function renderShell() {
    var me = state.me || {};
    var u = state.user || me.user || {};
    state.site = Api.site || state.site || {};
    var site = state.site;
    var unreadN = me.unreadNotifications || 0;
    var unreadM = me.unreadMessages || 0;
    var friendReq = me.friendRequests || 0;
    var isAdmin = u.role === 'admin';
    var isMod = (u.role === 'admin' || u.role === 'moderator') && !!(state.site && state.site.adminBoard);

    var html = '';
    html += '<div class="topbar"><div class="topbar-inner">';
    html += '<a class="brand" href="#/feed">' +
      '<span class="brand-mark">' + UI.esc(site.shortName || 'S') + '</span>' +
      '<span class="brand-text"><b>' + UI.esc(site.name || 'Social') + '</b><small>' + UI.esc(site.tagline || '') + '</small></span>' +
      '</a>';
    html += '<form class="topsearch" id="top-search" role="search">' + UI.icon('magnifying-glass') +
      '<input type="search" id="top-search-input" placeholder="' + UI.attr(T('search.placeholder')) + '" autocomplete="off"></form>';
    html += '<nav class="tabs">';
    html += tab('#/feed', 'house', T('nav.feed'), 'feed');
    html += tab('#/friends', 'user-group', T('nav.friends'), 'friends', friendReq, 'badge-friends');
    html += tab('#/im', 'envelope', T('nav.messages'), 'im', unreadM, 'badge-msg');
    html += tab('#/people', 'users', T('nav.people'), 'people');
    html += tab('#/groups', 'people-group', T('nav.groups'), 'groups');
    html += tab('#/notifications', 'bell', T('nav.notifications'), 'notifications', unreadN, 'badge-notif');
    html += tab('#/bookmarks', 'bookmark', T('nav.bookmarks'), 'bookmarks');
    if (isMod) html += tab('#/admin', 'shield-halved', T('nav.admin'), 'admin');
    html += '</nav>';
    html += '<div class="top-right">';
    html += '<button type="button" class="icon-btn" id="theme-toggle" title="' + UI.attr(T('settings.theme')) + '">' + UI.icon('circle-half-stroke') + '</button>';
    html += '<a class="top-user" href="#/profile/' + u.id + '">' + UI.avatar(u, 24) + '<span class="name">' + UI.esc(u.displayName || u.username) + '</span></a>';
    html += '</div>';
    html += '</div></div>';

    html += '<div class="shell">';
    html += '<aside class="col-left">';
    html += '<div class="card"><div class="mini-profile">' +
      '<a href="#/profile/' + u.id + '">' + UI.avatar(u, 64, false) + '</a>' +
      '<a class="name" href="#/profile/' + u.id + '">' + UI.esc(u.displayName || u.username) + '</a>' +
      '<div class="status">' + UI.esc(u.status || ('@' + u.username)) + '</div>' +
      '<div class="mini-stats"><div><b>' + (u.friends || 0) + '</b><span>' + T('profile.friends') + '</span></div>' +
      '<div><b>' + (u.posts || 0) + '</b><span>' + T('profile.posts') + '</span></div>' +
      '<div><b>' + (u.likes || 0) + '</b><span>' + T('profile.likes') + '</span></div></div>' +
      '</div></div>';
    html += '<div class="card side-nav">';
    html += side('#/feed', 'house', T('nav.feed'), 'feed');
    html += side('#/friends', 'user-group', T('nav.friends'), 'friends', friendReq);
    html += side('#/im', 'envelope', T('nav.messages'), 'im', unreadM);
    html += side('#/people', 'users', T('nav.people'), 'people');
    html += side('#/groups', 'people-group', T('nav.groups'), 'groups');
    html += side('#/notifications', 'bell', T('nav.notifications'), 'notifications', unreadN);
    html += side('#/bookmarks', 'bookmark', T('nav.bookmarks'), 'bookmarks');
    html += '<div class="side-sep"></div>';
    html += side('#/profile/' + u.id, 'id-card', T('nav.profile'), 'profile');
    html += side('#/settings', 'gear', T('nav.settings'), 'settings');
    if (isMod) html += side('#/admin', 'shield-halved', T('nav.admin'), 'admin');
    html += '<div class="side-sep"></div>';
    html += '<button type="button" class="side-link" id="logout-btn" style="width:100%;border:0;background:transparent;text-align:left">' + UI.icon('right-from-bracket') + ' ' + T('nav.logout') + '</button>';
    html += '</div>';
    html += '<div class="card online-card"><div class="card-h">' + UI.icon('signal') + ' ' + T('online.title') + '</div><div class="online-list" id="online-mini">' + UI.spinner() + '</div></div>';
    html += '</aside>';

    html += '<main class="col-main" id="main-col"></main>';

    html += '<aside class="col-right">';
    if (site.motd) html += '<div class="card"><div class="card-h">' + UI.icon('bullhorn') + ' ' + T('motd.title') + '</div><div class="card-b" style="font-size:12px">' + UI.nl2br(site.motd) + '</div></div>';
    html += '<div class="card"><div class="card-h">' + UI.icon('chart-simple') + ' ' + T('stats.title') + '</div><div class="card-b" id="stats-box">' + UI.spinner() + '</div></div>';
    html += '<div class="card"><div class="card-h">' + UI.icon('comments') + ' ' + T('online.title') + '</div><div class="online-list" id="online-list">' + UI.spinner() + '</div></div>';
    html += '<div class="card"><div class="card-f" style="text-align:center;border-top:0">' + UI.esc(site.footer || T('footer.powered')) + '</div></div>';
    html += '</aside>';
    html += '</div>';

    root().innerHTML = html;
    updateBadges();
    bindShell();
    loadOnline();
    loadStats();
  }

  function tab(href, iconName, label, key, count, badgeId) {
    var active = state.route === key || (key === 'profile' && state.route === 'profile');
    return '<a class="tab' + (active ? ' active' : '') + '" href="' + href + '">' + UI.icon(iconName) +
      '<span class="lbl">' + UI.esc(label) + '</span>' +
      (count ? '<span class="count" id="' + badgeId + '">' + (count > 99 ? '99+' : count) + '</span>' : (badgeId ? '<span class="hidden" id="' + badgeId + '"></span>' : '')) +
      '</a>';
  }

  function side(href, iconName, label, key, count) {
    var active = state.route === key;
    return '<a class="side-link' + (active ? ' active' : '') + '" href="' + href + '">' + UI.icon(iconName) +
      '<span>' + UI.esc(label) + '</span>' +
      (count ? '<span class="count">' + (count > 99 ? '99+' : count) + '</span>' : '') + '</a>';
  }

  function bindShell() {
    var t = document.getElementById('theme-toggle');
    if (t) t.addEventListener('click', function () {
      var next = UI.cycleTheme();
      SoundFX.play('theme');
      saveSetting({ theme: next });
      toast(T('settings.theme') + ': ' + T('theme.' + next), 'ok');
    });
    var lo = document.getElementById('logout-btn');
    if (lo) lo.addEventListener('click', doLogout);
    var sf = document.getElementById('top-search');
    if (sf) sf.addEventListener('submit', function (e) {
      e.preventDefault();
      var input = document.getElementById('top-search-input');
      var q = input ? input.value.trim() : '';
      if (q.length >= 2) go('#/search?q=' + encodeURIComponent(q));
    });
  }

  function loadOnline() {
    Api.request('GET', '/api/online', null, function (err, data) {
      if (err) return;
      var users = data.users || [];
      var mini = document.getElementById('online-mini');
      var full = document.getElementById('online-list');
      if (!users.length) {
        if (mini) mini.innerHTML = '<div class="tiny muted" style="padding:8px">' + T('online.empty') + '</div>';
        if (full) full.innerHTML = '<div class="tiny muted" style="padding:8px">' + T('online.empty') + '</div>';
        return;
      }
      var html = '';
      for (var i = 0; i < users.length; i++) {
        html += '<a class="online-user" href="#/u/' + encodeURIComponent(users[i].username) + '">' +
          UI.avatar(users[i], 24) + '<span class="name">' + UI.esc(users[i].displayName) + '</span><span class="presence"></span></a>';
      }
      if (mini) mini.innerHTML = html;
      if (full) full.innerHTML = html;
    });
  }

  function loadStats() {
    Api.request('GET', '/api/stats', null, function (err, data) {
      if (err) return;
      var box = document.getElementById('stats-box');
      if (!box) return;
      var days = data.days || [];
      var max = 1;
      for (var i = 0; i < days.length; i++) if (days[i].views > max) max = days[i].views;
      var bars = '';
      for (var j = 0; j < days.length; j++) {
        var h = Math.max(2, Math.round((days[j].views / max) * 34));
        bars += '<div title="' + UI.attr(days[j].day + ': ' + days[j].views + ' / ' + days[j].unique) + '" style="flex:1;display:flex;align-items:flex-end;height:36px">' +
          '<div style="width:100%;height:' + h + 'px;background:var(--accent);opacity:.65;border-radius:3px 3px 0 0"></div></div>';
      }
      box.innerHTML = '<div class="stat-grid" style="grid-template-columns:1fr 1fr">' +
        '<div class="stat-card"><b>' + data.users + '</b><span>' + T('stats.users') + '</span></div>' +
        '<div class="stat-card"><b>' + data.online + '</b><span>' + T('stats.online') + '</span></div>' +
        '<div class="stat-card"><b>' + data.posts + '</b><span>' + T('stats.posts') + '</span></div>' +
        '<div class="stat-card"><b>' + data.visitsTotal + '</b><span>' + T('stats.visits') + '</span></div>' +
        '</div><div style="display:flex;gap:2px;margin-top:10px" title="' + UI.attr(T('stats.today') + ': ' + data.visitsToday) + '">' + bars + '</div>';
    });
  }

  // ------------------------------------------------------------------ router

  function route() {
    if (!state.authed) { renderAuth('login'); return; }
    stopTimers();
    var r = parseHash();    state.route = r.path;
    state.parts = r.parts;

    var view = views[r.path];
    if (r.path === 'u' && r.parts[1]) view = views.profile;
    if (!view) view = views.notfound || views.feed;

    var main = document.getElementById('main-col');
    if (!main) { renderShell(); main = document.getElementById('main-col'); }
    if (!main) return;
    UI.scrollTop();
    try {
      view(main, r);
    } catch (e) {
      main.innerHTML = '<div class="card"><div class="card-b"><div class="alert err">' + UI.esc(String(e && e.message || e)) + '</div></div></div>';
    }
    updateNavActive();
  }

  function updateNavActive() {
    var links = document.querySelectorAll('.tabs .tab, .side-nav .side-link');
    for (var i = 0; i < links.length; i++) {
      links[i].className = links[i].className.replace(/\s*active\b/g, '');
      var href = links[i].getAttribute('href') || '';
      var key = href.replace(/^#\//, '').split('/')[0].split('?')[0];
      var active = key === state.route || (key === 'profile' && state.route === 'profile') ||
                   (key === 'im' && state.route === 'im') || (key === 'admin' && state.route === 'admin');
      if (active) links[i].className += ' active';
    }
  }

  function stopTimers() {
    if (state.im && state.im.timer) { clearInterval(state.im.timer); state.im.timer = null; }
    if (state.adminLogs && state.adminLogs.timer) { clearInterval(state.adminLogs.timer); state.adminLogs.timer = null; }
  }

  // ------------------------------------------------------------------ auth

  function renderAuth(mode) {
    state.authed = false;
    var site = Api.site || {};
    var html = '<div class="landing">';
    html += '<div class="landing-hero">';
    html += '<div class="landing-brand"><span class="brand-mark">' + UI.esc(site.shortName || 'S') + '</span><b>' + UI.esc(site.name || 'Social') + '</b></div>';
    html += '<h1>' + UI.esc(site.tagline || T('app.tagline')) + '</h1>';
    html += '<p class="lead">' + UI.esc(site.description || '') + '</p>';
    html += '<div class="landing-features">' +
      feature('comments', T('nav.feed'), T('feed.composerPlaceholder')) +
      feature('user-group', T('nav.friends'), T('friends.emptyHint')) +
      feature('envelope', T('nav.messages'), T('messages.select')) +
      feature('moon', T('settings.theme'), T('theme.light') + ' / ' + T('theme.dark') + ' / ' + T('theme.oled')) +
      '</div>';
    html += '<div class="landing-side-note">' + UI.esc(site.motd || '') + '</div>';
    html += '</div>';

    html += '<div>';
    html += '<div class="auth-card">';
    html += '<div class="auth-head"><h2>' + (mode === 'register' ? T('auth.registerTitle') : mode === 'recover' ? T('auth.recoverTitle') : T('auth.loginTitle')) + '</h2><p>' +
      (mode === 'register' ? T('auth.registerSub') : mode === 'recover' ? T('auth.recoverSub') : T('auth.loginSub')) + '</p></div>';
    html += '<div class="auth-tabs">';
    html += '<button type="button" class="auth-tab' + (mode === 'login' ? ' active' : '') + '" data-auth="login">' + T('auth.login') + '</button>';
    if (site.registration !== false) html += '<button type="button" class="auth-tab' + (mode === 'register' ? ' active' : '') + '" data-auth="register">' + T('auth.register') + '</button>';
    html += '<button type="button" class="auth-tab' + (mode === 'recover' ? ' active' : '') + '" data-auth="recover">' + T('auth.recover') + '</button>';
    html += '</div>';
    html += '<div class="auth-body" id="auth-body">';
    if (mode === 'register' && site.registration === false) html += '<div class="alert info">' + T('auth.registrationClosed') + '</div>';
    else if (mode === 'register') html += registerForm(site);
    else if (mode === 'recover') html += recoverForm();
    else html += loginForm();
    html += '</div>';
    html += '<div class="auth-foot">' + (site.firstUserAdmin && (Api.hs ? Api.hs.userCount === 0 : false) ? T('auth.firstAdmin') : UI.esc(site.footer || T('footer.powered'))) + '</div>';
    html += '</div>';
    html += '<div class="landing-side-note"><button type="button" class="btn btn-ghost btn-sm" id="theme-toggle-auth">' + UI.icon('circle-half-stroke') + ' ' + T('theme.' + UI.getTheme()) + '</button></div>';
    html += '</div></div>';

    root().innerHTML = html;
    var tt = document.getElementById('theme-toggle-auth');
    if (tt) tt.addEventListener('click', function () {
      var next = UI.cycleTheme();
      tt.innerHTML = UI.icon('circle-half-stroke') + ' ' + T('theme.' + next);
    });
    var tabs = document.querySelectorAll('[data-auth]');
    for (var i = 0; i < tabs.length; i++) {
      tabs[i].addEventListener('click', function () { renderAuth(this.getAttribute('data-auth')); });
    }
    bindAuthForms(mode);
  }

  function feature(iconName, title, text) {
    return '<div class="landing-feature">' + UI.icon(iconName) + '<div><b>' + UI.esc(title) + '</b><span>' + UI.esc(text) + '</span></div></div>';
  }

  function captchaBlock() {
    var hs = Api.hs || {};
    var c = hs.captcha;
    if (!hs.captchaRequired || !c) return '';
    return '<div class="field"><label>' + T('auth.captcha') + '</label><div class="captcha-box">' +
      '<div class="captcha" id="captcha-img" title="' + UI.attr(T('common.refresh')) + '">' + c.svg + '</div>' +
      '<input class="input" type="text" id="captcha-code" inputmode="numeric" maxlength="4" placeholder="0000" autocomplete="off">' +
      '</div><div class="hint">' + T('auth.captchaHint') + '</div></div>';
  }

  function loginForm() {
    return '<form id="login-form">' +
      '<div class="field"><label>' + T('auth.username') + '</label><input class="input" type="text" id="login-user" autocomplete="username" required></div>' +
      '<div class="field"><label>' + T('auth.password') + '</label><input class="input" type="password" id="login-pass" autocomplete="current-password" required></div>' +
      captchaBlock() +
      '<div id="auth-error"></div>' +
      '<button class="btn btn-primary btn-block" type="submit" id="login-submit">' + UI.icon('right-to-bracket') + ' ' + T('auth.signIn') + '</button>' +
      '<div class="center tiny muted" style="margin-top:10px"><a href="#" data-auth="recover">' + T('auth.forgot') + '</a></div>' +
      '</form>';
  }

  function registerForm(site) {
    return '<form id="register-form">' +
      '<div class="field"><label>' + T('auth.username') + '</label><input class="input" type="text" id="reg-user" autocomplete="username" required><div class="hint">' + T('auth.usernameHint') + '</div></div>' +
      '<div class="field"><label>' + T('auth.displayName') + '</label><input class="input" type="text" id="reg-name" maxlength="40" autocomplete="nickname"></div>' +
      '<div class="inline-fields"><div class="field"><label>' + T('auth.password') + '</label><input class="input" type="password" id="reg-pass" autocomplete="new-password" required></div>' +
      '<div class="field"><label>' + T('auth.password2') + '</label><input class="input" type="password" id="reg-pass2" autocomplete="new-password" required></div></div>' +
      (site.inviteOnly ? '<div class="field"><label>' + T('auth.invite') + '</label><input class="input" type="text" id="reg-invite"></div>' : '') +
      captchaBlock() +
      '<label class="check" style="margin:10px 0"><input type="checkbox" id="reg-terms"> <span>' + T('auth.terms') + '</span></label>' +
      '<div id="auth-error"></div>' +
      '<button class="btn btn-primary btn-block" type="submit" id="reg-submit">' + UI.icon('user-plus') + ' ' + T('auth.createAccount') + '</button>' +
      '</form>';
  }

  function recoverForm() {
    return '<form id="recover-form">' +
      '<div class="field"><label>' + T('auth.username') + '</label><input class="input" type="text" id="rec-user" autocomplete="username" required></div>' +
      '<div class="field"><label>' + T('auth.recoveryCode') + '</label><input class="input" type="text" id="rec-code" autocomplete="off" required><div class="hint">' + T('auth.recoveryHint') + '</div></div>' +
      '<div class="field"><label>' + T('auth.newPassword') + '</label><input class="input" type="password" id="rec-pass" autocomplete="new-password" required></div>' +
      '<div id="auth-error"></div>' +
      '<button class="btn btn-primary btn-block" type="submit" id="rec-submit">' + UI.icon('key') + ' ' + T('auth.reset') + '</button>' +
      '</form>';
  }

  function bindAuthForms(mode) {
    var cap = document.getElementById('captcha-img');
    if (cap) cap.addEventListener('click', function () {
      Api.handshake(function (err, hs) {
        if (err) return;
        var holder = document.getElementById('captcha-img');
        if (holder && hs.captcha) holder.innerHTML = hs.captcha.svg;
        var code = document.getElementById('captcha-code');
        if (code) code.value = '';
      });
    });

    var login = document.getElementById('login-form');
    if (login) login.addEventListener('submit', function (e) {
      e.preventDefault();
      var btn = document.getElementById('login-submit');
      var user = val('login-user');
      var pass = val('login-pass');
      var captcha = { id: '', code: '' };
      if (Api.hs && Api.hs.captcha) { captcha.id = Api.hs.captcha.id; captcha.code = val('captcha-code'); }
      setBusy(btn, true, T('common.loading'));
      Api.login(user, pass, captcha, function (err, data) {
        setBusy(btn, false, T('auth.signIn'));
        if (err) { authError(Api.errorText(err)); SoundFX.play('error'); return; }
        onLoggedIn(data);
      });
    });

    var reg = document.getElementById('register-form');
    if (reg) reg.addEventListener('submit', function (e) {
      e.preventDefault();
      var btn = document.getElementById('reg-submit');
      var user = val('reg-user');
      var pass = val('reg-pass');
      var pass2 = val('reg-pass2');
      if (pass.length < 8) { authError(T('auth.passwordShort')); return; }
      if (pass !== pass2) { authError(T('auth.passwordMismatch')); return; }
      if (!document.getElementById('reg-terms').checked) { authError(T('auth.terms')); return; }
      var captcha = { id: '', code: '' };
      if (Api.hs && Api.hs.captcha) { captcha.id = Api.hs.captcha.id; captcha.code = val('captcha-code'); }
      setBusy(btn, true, T('common.loading'));
      Api.register({
        username: user,
        displayName: val('reg-name'),
        password: pass,
        captchaId: captcha.id,
        captcha: captcha.code,
        invite: val('reg-invite')
      }, function (err, data) {
        setBusy(btn, false, T('auth.createAccount'));
        if (err) { authError(Api.errorText(err)); SoundFX.play('error'); return; }
        onLoggedIn(data, true);
      });
    });

    var rec = document.getElementById('recover-form');
    if (rec) rec.addEventListener('submit', function (e) {
      e.preventDefault();
      var btn = document.getElementById('rec-submit');
      var pass = val('rec-pass');
      if (pass.length < 8) { authError(T('auth.passwordShort')); return; }
      setBusy(btn, true, T('common.loading'));
      Api.recover(val('rec-user'), val('rec-code'), pass, function (err, data) {
        setBusy(btn, false, T('auth.reset'));
        if (err) { authError(Api.errorText(err)); SoundFX.play('error'); return; }
        UI.modal({
          title: T('admin.recovery'),
          icon: 'key',
          body: '<p class="muted" style="margin-top:0">' + T('auth.recoverySaved') + '</p>' +
                '<div class="input mono" style="font-size:14px;text-align:center;user-select:all">' + UI.esc(data.recoveryCode) + '</div>',
          actions: [
            { label: T('auth.copy'), cls: 'btn-ghost', onClick: function () { UI.copy(data.recoveryCode); UI.toast(T('auth.copied'), 'ok'); return false; } },
            { label: T('auth.login'), cls: 'btn-primary', onClick: function () { renderAuth('login'); } }
          ]
        });
        SoundFX.play('success');
      });
    });
  }

  function authError(text) {
    var box = document.getElementById('auth-error');
    if (box) box.innerHTML = '<div class="alert err">' + UI.esc(text) + '</div>';
  }

  function onLoggedIn(data, isRegister) {
    state.authed = true;
    state.me = data.me || null;
    state.user = (data.me && data.me.user) || data.user || null;
    state.site = data.site || Api.site || state.site;
    applyUserPrefs();
    renderShell();
    go('#/feed');
    SoundFX.play('login');
    if (isRegister && data.recoveryCode) {
      UI.modal({
        title: T('auth.saveRecovery'),
        icon: 'key',
        dismissable: false,
        body: '<p class="muted" style="margin-top:0">' + T('auth.recoverySaved') + '</p>' +
              '<div class="input mono" style="font-size:15px;text-align:center;user-select:all">' + UI.esc(data.recoveryCode) + '</div>',
        actions: [
          { label: T('auth.copy'), cls: 'btn-ghost', keepOpen: true, onClick: function () { UI.copy(data.recoveryCode); UI.toast(T('auth.copied'), 'ok'); return false; } },
          { label: T('common.close'), cls: 'btn-primary' }
        ]
      });
    } else {
      UI.toast(T('auth.welcomeBack') + ', ' + (state.user ? state.user.displayName : ''), 'ok');
    }
  }

  function doLogout() {
    Api.logout(function () {
      state.authed = false;
      state.me = null;
      state.user = null;
      SoundFX.play('logout');
      renderAuth('login');
    });
  }

  function val(id) {
    var el = document.getElementById(id);
    return el ? el.value : '';
  }

  function setBusy(btn, busy, label) {
    if (!btn) return;
    btn.disabled = !!busy;
    btn.innerHTML = busy ? (UI.spinner() + ' ' + UI.esc(label || '')) : UI.esc(label || '');
  }

  // ------------------------------------------------------------------ feed

  function feedView(main, r) {
    var tab = qs('tab') || 'home';
    state.feed = { tab: tab, posts: [], next: 0, hasMore: false, loading: false };
    var html = composerHtml(0);
    html += '<div class="feed-tabs">' +
      feedTab('home', T('feed.home')) + feedTab('global', T('feed.global')) + feedTab('friends', T('feed.friends')) +
      '</div>';
    html += '<div id="feed-list"></div>';
    html += '<div id="feed-more"></div>';
    main.innerHTML = html;
    bindComposer(0);
    bindFeedTabs();
    loadFeed(false);
  }

  function feedTab(tab, label) {
    return '<a class="tab2' + (state.feed.tab === tab ? ' active' : '') + '" href="#/feed?tab=' + tab + '">' + UI.esc(label) + '</a>';
  }

  function bindFeedTabs() {
    var tabs = document.querySelectorAll('.feed-tabs .tab2');
    for (var i = 0; i < tabs.length; i++) {
      tabs[i].addEventListener('click', function (e) {
        e.preventDefault();
        var href = this.getAttribute('href');
        go(href);
      });
    }
  }

  function loadFeed(more) {
    if (state.feed.loading) return;
    state.feed.loading = true;
    var list = document.getElementById('feed-list');
    var moreBox = document.getElementById('feed-more');
    if (moreBox) moreBox.innerHTML = '<div class="center" style="padding:10px">' + UI.spinner() + '</div>';
    var url = '/api/feed?tab=' + encodeURIComponent(state.feed.tab) + '&limit=15' + (more && state.feed.next ? '&before=' + state.feed.next : '');
    Api.request('GET', url, null, function (err, data) {
      state.feed.loading = false;
      if (err) {
        if (moreBox) moreBox.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>';
        return;
      }
      var posts = data.posts || [];
      state.feed.posts = more ? state.feed.posts.concat(posts) : posts;
      state.feed.next = data.next || 0;
      state.feed.hasMore = !!data.hasMore;
      renderFeedList(more ? posts : null);
    });
  }

  function renderFeedList(appendPosts) {
    var list = document.getElementById('feed-list');
    if (!list) return;
    if (!appendPosts) {
      if (!state.feed.posts.length) {
        list.innerHTML = '<div class="card"><div class="card-b">' + UI.empty('inbox', T('feed.empty'), T('feed.emptyHint')) + '</div></div>';
        var mb = document.getElementById('feed-more');
        if (mb) mb.innerHTML = '';
        return;
      }
      var html = '';
      for (var i = 0; i < state.feed.posts.length; i++) html += postHtml(state.feed.posts[i]);
      list.innerHTML = html;
    } else {
      for (var j = 0; j < appendPosts.length; j++) list.insertAdjacentHTML('beforeend', postHtml(appendPosts[j]));
    }
    bindPosts(list);
    var more = document.getElementById('feed-more');
    if (more) {
      more.innerHTML = state.feed.hasMore
        ? '<button type="button" class="load-more" id="feed-load-more">' + T('feed.loadMore') + '</button>'
        : (state.feed.posts.length ? '<div class="center tiny muted" style="padding:10px">' + T('feed.noMore') + '</div>' : '');
      var b = document.getElementById('feed-load-more');
      if (b) b.addEventListener('click', function () { loadFeed(true); });
    }
  }

  function composerHtml(wallId) {
    var u = state.user || {};
    return '<form class="composer" id="composer" data-wall="' + (wallId || 0) + '">' +
      '<div class="composer-top">' + UI.avatar(u, 36) +
      '<textarea id="composer-text" maxlength="' + (state.site.maxPostLength || 8000) + '" placeholder="' + UI.attr(T('feed.composerPlaceholder')) + '"></textarea>' +
      '</div>' +
      '<div class="composer-preview hidden" id="composer-preview"></div>' +
      '<div class="composer-video-row hidden" id="composer-video-row">' +
      '<div class="composer-video-input">' + UI.icon('video') +
      '<input class="input" id="composer-video-url" placeholder="' + UI.attr(T('feed.videoPlaceholder')) + '" autocomplete="off"></div>' +
      '<div class="composer-video-preview" id="composer-video-preview"></div>' +
      '</div>' +
      '<div class="composer-bar">' +
      '<button type="button" class="btn btn-sm" id="composer-photo">' + UI.icon('image') + ' ' + T('feed.photo') + '</button>' +
      '<button type="button" class="btn btn-sm" id="composer-video">' + UI.icon('video') + ' ' + T('feed.video') + '</button>' +
      '<input type="file" id="composer-file" accept="image/png,image/jpeg,image/gif,image/webp" class="hidden">' +
      '<select class="select" id="composer-visibility" style="width:auto;padding:4px 24px 4px 8px;font-size:11.5px">' +
      '<option value="public">' + T('feed.public') + '</option>' +
      '<option value="friends">' + T('feed.friendsOnly') + '</option>' +
      '<option value="private">' + T('feed.private') + '</option>' +
      '</select>' +
      '<button type="submit" class="btn btn-primary btn-sm right" id="composer-submit">' + UI.icon('paper-plane') + ' ' + T('feed.post') + '</button>' +
      '</div></form>';
  }

  function bindComposer(wallId) {
    var form = document.getElementById('composer');
    if (!form) return;
    var text = document.getElementById('composer-text');
    var file = document.getElementById('composer-file');
    var photo = document.getElementById('composer-photo');
    var preview = document.getElementById('composer-preview');
    var videoBtn = document.getElementById('composer-video');
    var videoRow = document.getElementById('composer-video-row');
    var videoInput = document.getElementById('composer-video-url');
    var videoPreview = document.getElementById('composer-video-preview');
    var imageData = '';

    if (text) {
      text.addEventListener('input', function () {
        text.style.height = 'auto';
        text.style.height = Math.min(220, text.scrollHeight) + 'px';
      });
    }
    if (photo && file) photo.addEventListener('click', function () { file.click(); });
    if (videoBtn && videoRow) videoBtn.addEventListener('click', function () {
      var open = !/(^|\s)hidden(\s|$)/.test(videoRow.className);
      videoRow.className = open ? 'composer-video-row hidden' : 'composer-video-row';
      if (!open && videoInput) videoInput.focus();
    });
    if (videoInput && videoPreview) videoInput.addEventListener('input', function () {
      var u = videoInput.value.trim();
      videoPreview.innerHTML = u ? videoHtml(u) : '';
    });
    if (file) file.addEventListener('change', function () {
      var f = file.files && file.files[0];
      if (!f) return;
      UI.fileToDataUrl(f, (state.site.maxPostImageKB || 4096) * 1024, function (err, dataUrl) {
        if (err) { UI.toast(err === 'too-large' ? T('settings.avatarHint', state.site.maxPostImageKB) : T('errors.generic'), 'err'); return; }
        imageData = dataUrl;
        preview.className = 'composer-preview';
        preview.innerHTML = '<img src="' + UI.attr(dataUrl) + '" alt=""> <button type="button" class="btn btn-xs btn-ghost" id="composer-remove">' + T('feed.removePhoto') + '</button>';
        var rm = document.getElementById('composer-remove');
        if (rm) rm.addEventListener('click', function () { imageData = ''; file.value = ''; preview.className = 'composer-preview hidden'; preview.innerHTML = ''; });
      });
    });
    form.addEventListener('submit', function (e) {
      e.preventDefault();
      var body = text ? text.value.trim() : '';
      var videoUrl = videoInput ? videoInput.value.trim() : '';
      if (videoUrl && !/^https?:\/\//i.test(videoUrl)) { UI.toast(T('feed.videoBadUrl'), 'err'); return; }
      if (!body && !imageData && !videoUrl) { if (text) text.focus(); return; }
      var btn = document.getElementById('composer-submit');
      setBusy(btn, true, T('feed.posting'));
      var payload = {
        body: body,
        visibility: val('composer-visibility') || 'public',
        wall: wallId || 0,
        image: imageData || '',
        video: videoUrl
      };
      Api.request('POST', '/api/post', payload, function (err, data) {
        setBusy(btn, false, T('feed.post'));
        if (err) { UI.toast(Api.errorText(err), 'err'); SoundFX.play('error'); return; }
        if (text) { text.value = ''; text.style.height = 'auto'; }
        imageData = '';
        if (preview) { preview.className = 'composer-preview hidden'; preview.innerHTML = ''; }
        if (file) file.value = '';
        if (videoInput) videoInput.value = '';
        if (videoPreview) videoPreview.innerHTML = '';
        if (videoRow) videoRow.className = 'composer-video-row hidden';
        SoundFX.play('post');
        UI.toast(T('feed.posted'), 'ok');
        if (state.route === 'feed') {
          state.feed.posts.unshift(data.post);
          renderFeedList(null);
        } else if (wallId) {
          route();
        }
      });
    });
  }

  function postHtml(p) {
    var author = p.author || {};
    var meta = '<a class="name" href="#/u/' + encodeURIComponent(author.username || '') + '">' + UI.esc(author.displayName || author.username) + '</a>' +
      '<span>' + UI.timeAgo(p.created) + '</span>' +
      (p.edited ? '<span>· ' + T('feed.edited') + '</span>' : '') +
      (p.visibility && p.visibility !== 'public' ? '<span>· ' + UI.icon(p.visibility === 'private' ? 'lock' : 'user-group') + ' ' + T(p.visibility === 'private' ? 'feed.private' : 'feed.friendsOnly') + '</span>' : '') +
      (p.wallUser ? '<span>· ' + UI.icon('arrow-right') + ' ' + UI.esc(p.wallUser.displayName) + '</span>' : '');
    var body = UI.linkify(p.body || '').replace(/\n/g, '<br>');
    var img = UI.safeUrl(p.image);
    var vid = videoHtml(p.video);
    var html = '<article class="post" data-post="' + p.id + '">' +
      '<div class="post-head">' + UI.avatar(author, 38) +
      '<div class="post-who"><div class="meta">' + meta + '</div></div>' +
      '<div class="post-menu">' + (p.canDelete ? '<button type="button" class="icon-btn post-delete" data-id="' + p.id + '" title="' + UI.attr(T('common.delete')) + '">' + UI.icon('trash') + '</button>' : '') + '</div>' +
      '</div>' +
      (body ? '<div class="post-body">' + body + '</div>' : '') +
      (img ? '<div class="post-media"><img class="post-img" src="' + UI.attr(img) + '" alt="" loading="lazy"></div>' : '') +
      vid +
      '<div class="post-actions">' +
      '<button type="button" class="pa post-like' + (p.liked ? ' liked' : '') + '" data-id="' + p.id + '">' + UI.icon(p.liked ? 'heart' : 'heart') + ' <span>' + (p.liked ? T('feed.liked') : T('feed.like')) + '</span> <b>' + (p.likes || 0) + '</b></button>' +
      '<button type="button" class="pa post-comment-toggle" data-id="' + p.id + '">' + UI.icon('comment') + ' <span>' + T('feed.comment') + '</span> <b>' + (p.comments || 0) + '</b></button>' +
      '<button type="button" class="pa post-share" data-id="' + p.id + '">' + UI.icon('share') + ' <span>' + T('feed.share') + '</span></button>' +
      '<button type="button" class="pa post-bookmark' + (p.bookmarked ? ' active' : '') + '" data-id="' + p.id + '" title="' + UI.attr(T('feed.bookmark')) + '">' + UI.icon('bookmark') + ' <span>' + (p.bookmarked ? T('feed.bookmarked') : T('feed.bookmark')) + '</span></button>' +
      '<a class="pa" href="#/post/' + p.id + '">' + UI.icon('up-right-from-square') + '</a>' +
      '</div>' +
      '<div class="comments hidden" data-comments="' + p.id + '"></div>' +
      '</article>';
    return html;
  }

  // ---------------------------------------------------------- post video

  var YT_RE = /(?:youtube\.com\/(?:watch\?(?:.*&)?v=|embed\/|shorts\/|live\/)|youtu\.be\/)([A-Za-z0-9_-]{6,20})/i;

  function youtubeId(url) {
    var m = YT_RE.exec(url || '');
    return m ? m[1] : '';
  }

  function videoHtml(url) {
    var src = UI.safeUrl(url);
    if (!src) return '';
    var yt = youtubeId(src);
    if (yt) {
      return '<div class="post-video"><a class="video-card" href="' + UI.attr(src) + '" target="_blank" rel="noopener noreferrer nofollow">' +
        '<img src="' + UI.attr('https://i.ytimg.com/vi/' + yt + '/hqdefault.jpg') + '" alt="" loading="lazy">' +
        '<span class="video-play">' + UI.icon('play') + '</span>' +
        '<span class="video-label">' + UI.icon('video') + ' YouTube</span></a></div>';
    }
    if (/\.(mp4|m4v|webm|ogv|ogg)(\?|#|$)/i.test(src)) {
      return '<div class="post-video"><video controls preload="metadata" src="' + UI.attr(src) + '"></video></div>';
    }
    return '<div class="post-video"><a class="video-link" href="' + UI.attr(src) + '" target="_blank" rel="noopener noreferrer nofollow">' +
      UI.icon('circle-play') + '<span>' + UI.esc(src.replace(/^https?:\/\//i, '').slice(0, 90)) + '</span></a></div>';
  }

  function bindPosts(scope) {
    var likes = (scope || document).querySelectorAll('.post-like');
    for (var i = 0; i < likes.length; i++) {
      if (likes[i].getAttribute('data-bound')) continue;
      likes[i].setAttribute('data-bound', '1');
      likes[i].addEventListener('click', function () { toggleLike(this.getAttribute('data-id'), this); });
    }
    var toggles = (scope || document).querySelectorAll('.post-comment-toggle');
    for (var j = 0; j < toggles.length; j++) {
      if (toggles[j].getAttribute('data-bound')) continue;
      toggles[j].setAttribute('data-bound', '1');
      toggles[j].addEventListener('click', function () { toggleComments(this.getAttribute('data-id')); });
    }
    var shares = (scope || document).querySelectorAll('.post-share');
    for (var k = 0; k < shares.length; k++) {
      if (shares[k].getAttribute('data-bound')) continue;
      shares[k].setAttribute('data-bound', '1');
      shares[k].addEventListener('click', function () { sharePost(this.getAttribute('data-id')); });
    }
    var marks = (scope || document).querySelectorAll('.post-bookmark');
    for (var bm = 0; bm < marks.length; bm++) {
      if (marks[bm].getAttribute('data-bound')) continue;
      marks[bm].setAttribute('data-bound', '1');
      marks[bm].addEventListener('click', function () { toggleBookmark(this.getAttribute('data-id'), this); });
    }
    var dels = (scope || document).querySelectorAll('.post-delete');
    for (var d = 0; d < dels.length; d++) {
      if (dels[d].getAttribute('data-bound')) continue;
      dels[d].setAttribute('data-bound', '1');
      dels[d].addEventListener('click', function () { deletePost(this.getAttribute('data-id')); });
    }
    var imgs = (scope || document).querySelectorAll('.post-img');
    for (var m = 0; m < imgs.length; m++) {
      if (imgs[m].getAttribute('data-bound')) continue;
      imgs[m].setAttribute('data-bound', '1');
      imgs[m].addEventListener('click', function () { UI.imageModal(this.getAttribute('src')); });
    }
  }

  function toggleLike(id, btn) {
    Api.request('POST', '/api/like', { id: Number(id) }, function (err, data) {
      if (err) { UI.toast(Api.errorText(err), 'err'); return; }
      var b = btn.querySelector('b');
      if (b) b.textContent = data.likes;
      if (data.liked) { btn.className = 'pa post-like liked'; SoundFX.play('like'); }
      else btn.className = 'pa post-like';
      var span = btn.querySelector('span');
      if (span) span.textContent = data.liked ? T('feed.liked') : T('feed.like');
    });
  }

  function toggleComments(id) {
    var box = document.querySelector('[data-comments="' + id + '"]');
    if (!box) return;
    if (!box.className.match(/hidden/)) { box.className = 'comments hidden'; return; }
    box.className = 'comments';
    box.innerHTML = '<div class="center" style="padding:8px">' + UI.spinner() + '</div>';
    Api.request('GET', '/api/comments?post=' + encodeURIComponent(id) + '&limit=50', null, function (err, data) {
      if (err) { box.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>'; return; }
      var comments = data.comments || [];
      var html = '';
      for (var i = 0; i < comments.length; i++) html += commentHtml(comments[i]);
      html += '<form class="comment-form" data-post="' + id + '">' +
        UI.avatar(state.user, 26) +
        '<input class="input" type="text" maxlength="' + (state.site.maxCommentLength || 2000) + '" placeholder="' + UI.attr(T('post.commentPlaceholder')) + '">' +
        '<button class="btn btn-sm btn-primary" type="submit">' + T('post.send') + '</button>' +
        '</form>';
      box.innerHTML = html;
      var form = box.querySelector('form');
      form.addEventListener('submit', function (e) {
        e.preventDefault();
        var input = form.querySelector('input');
        var text = input.value.trim();
        if (!text) return;
        input.disabled = true;
        Api.request('POST', '/api/comment', { post: Number(id), body: text }, function (err2, data2) {
          input.disabled = false;
          if (err2) { UI.toast(Api.errorText(err2), 'err'); return; }
          input.value = '';
          var first = box.querySelector('.comment');
          if (first) first.insertAdjacentHTML('beforebegin', commentHtml(data2.comment));
          else box.insertAdjacentHTML('afterbegin', commentHtml(data2.comment));
          SoundFX.play('comment');
        });
      });
    });
  }

  function commentHtml(c) {
    var a = c.author || {};
    return '<div class="comment" data-comment="' + c.id + '">' + UI.avatar(a, 26) +
      '<div class="comment-main"><div class="who"><a href="#/u/' + encodeURIComponent(a.username || '') + '">' + UI.esc(a.displayName || a.username) + '</a>' +
      '<span>' + UI.timeAgo(c.created) + '</span>' +
      (c.canDelete ? '<button type="button" class="btn btn-xs btn-ghost comment-delete" data-id="' + c.id + '" style="float:right">' + UI.icon('trash') + '</button>' : '') +
      '</div><div class="text">' + UI.linkify(c.body || '').replace(/\n/g, '<br>') + '</div></div></div>';
  }

  function deletePost(id) {
    UI.confirm(T('feed.delete'), T('feed.deleteConfirm'), T('common.delete'), function () {
      Api.request('POST', '/api/post/delete', { id: Number(id) }, function (err) {
        if (err) { UI.toast(Api.errorText(err), 'err'); return; }
        var el = document.querySelector('[data-post="' + id + '"]');
        if (el) el.parentNode.removeChild(el);
        UI.toast(T('feed.deleted'), 'ok');
        SoundFX.play('success');
      });
    }, true);
  }

  function deleteComment(id) {
    UI.confirm(T('common.delete'), T('common.confirm'), T('common.delete'), function () {
      Api.request('POST', '/api/comment/delete', { id: Number(id) }, function (err) {
        if (err) { UI.toast(Api.errorText(err), 'err'); return; }
        var el = document.querySelector('[data-comment="' + id + '"]');
        if (el) el.parentNode.removeChild(el);
        UI.toast(T('feed.deleted'), 'ok');
      });
    }, true);
  }

  function sharePost(id) {
    var url = location.origin + location.pathname + '#/post/' + id;
    UI.copy(url);
    UI.toast(T('feed.copiedLink'), 'ok');
    SoundFX.play('click');
  }

  function toggleBookmark(id, btn) {
    Api.request('POST', '/api/bookmark', { id: Number(id) }, function (err, data) {
      if (err) { UI.toast(Api.errorText(err), 'err'); return; }
      if (btn) {
        var span = btn.querySelector('span');
        if (data.bookmarked) { btn.className = 'pa post-bookmark active'; if (span) span.textContent = T('feed.bookmarked'); }
        else { btn.className = 'pa post-bookmark'; if (span) span.textContent = T('feed.bookmark'); }
      }
      UI.toast(data.bookmarked ? T('feed.bookmarkAdded') : T('feed.bookmarkRemoved'), 'ok');
      SoundFX.play('click');
      // on the bookmarks page a removed post should disappear right away
      if (!data.bookmarked && state.route === 'bookmarks') {
        var el = document.querySelector('[data-post="' + id + '"]');
        if (el && el.parentNode) el.parentNode.removeChild(el);
      }
    });
  }

  // ------------------------------------------------------------------ post

  function postView(main, r) {
    var id = r.parts[1];
    main.innerHTML = '<div class="center" style="padding:20px">' + UI.spinner() + '</div>';
    Api.request('GET', '/api/post?id=' + encodeURIComponent(id), null, function (err, data) {
      if (err) {
        main.innerHTML = '<div class="card"><div class="card-b"><div class="alert err">' + UI.esc(Api.errorText(err)) + '</div><a class="btn" href="#/feed">' + T('common.back') + '</a></div></div>';
        return;
      }
      var p = data.post;
      var html = '<div class="feed-tabs"><a class="tab2" href="#/feed">' + UI.icon('arrow-left') + ' ' + T('common.back') + '</a></div>';
      html += postHtml(p);
      main.innerHTML = html;
      bindPosts(main);
      var box = main.querySelector('[data-comments="' + p.id + '"]');
      if (box) {
        box.className = 'comments';
        var comments = data.comments || [];
        var ch = '';
        for (var i = 0; i < comments.length; i++) ch += commentHtml(comments[i]);
        ch += '<form class="comment-form" data-post="' + p.id + '">' + UI.avatar(state.user, 26) +
          '<input class="input" type="text" maxlength="' + (state.site.maxCommentLength || 2000) + '" placeholder="' + UI.attr(T('post.commentPlaceholder')) + '">' +
          '<button class="btn btn-sm btn-primary" type="submit">' + T('post.send') + '</button></form>';
        box.innerHTML = ch;
        var form = box.querySelector('form');
        form.addEventListener('submit', function (e) {
          e.preventDefault();
          var input = form.querySelector('input');
          var text = input.value.trim();
          if (!text) return;
          Api.request('POST', '/api/comment', { post: p.id, body: text }, function (err2, data2) {
            if (err2) { UI.toast(Api.errorText(err2), 'err'); return; }
            input.value = '';
            var first = box.querySelector('.comment');
            if (first) first.insertAdjacentHTML('beforebegin', commentHtml(data2.comment));
            SoundFX.play('comment');
          });
        });
      }
    });
  }

  // ------------------------------------------------------------------ badges

  function badgeCatalog() {
    return (state.site && state.site.badges) || [];
  }

  function badgeList(csv) {
    var ids = String(csv || '').split(',');
    var cat = badgeCatalog();
    var out = [];
    for (var i = 0; i < ids.length; i++) {
      var id = ids[i].replace(/^\s+|\s+$/g, '');
      if (!id) continue;
      var icon = 'award';
      for (var j = 0; j < cat.length; j++) if (cat[j].id === id) { icon = cat[j].icon; break; }
      out.push({ id: id, icon: icon });
    }
    return out;
  }

  function badgesHtml(csv) {
    var list = badgeList(csv);
    if (!list.length) return '';
    var html = '<div class="badge-row">';
    for (var i = 0; i < list.length; i++) {
      var label = T('badge.' + list[i].id);
      html += '<span class="ach" title="' + UI.attr(label) + '">' + UI.icon(list[i].icon) +
        '<span>' + UI.esc(label) + '</span></span>';
    }
    return html + '</div>';
  }

  function badgeIcons(csv) {
    var list = badgeList(csv);
    if (!list.length) return '';
    var html = '<div class="badge-row compact">';
    for (var i = 0; i < list.length; i++) {
      html += '<span class="ach" title="' + UI.attr(T('badge.' + list[i].id)) + '">' + UI.icon(list[i].icon) + '</span>';
    }
    return html + '</div>';
  }

  function badgesCard(csv, showLocked) {
    var cat = badgeCatalog();
    if (!cat.length) return '';
    var owned = {};
    var list = badgeList(csv);
    for (var i = 0; i < list.length; i++) owned[list[i].id] = true;
    var items = '';
    for (var j = 0; j < cat.length; j++) {
      var id = cat[j].id;
      var on = !!owned[id];
      if (!on && !showLocked) continue;
      items += '<div class="ach-item' + (on ? '' : ' locked') + '">' + UI.icon(cat[j].icon) +
        '<div><b>' + UI.esc(T('badge.' + id)) + '</b><span>' + UI.esc(T('badge.' + id + '.desc')) + '</span></div></div>';
    }
    if (!items) return '';
    return '<div class="card"><div class="card-h">' + UI.icon('trophy') + ' ' + T('profile.badges') +
      '<span class="right tiny muted">' + list.length + ' / ' + cat.length + '</span></div>' +
      '<div class="card-b"><div class="ach-list">' + items + '</div></div></div>';
  }

  function moodIcon(mood) {
    if (mood === 'happy') return 'face-smile';
    if (mood === 'sad') return 'face-frown';
    if (mood === 'neutral') return 'face-meh';
    return '';
  }

  // ------------------------------------------------------------------ profile

  function profileView(main, r) {
    var id = r.parts[1];
    var username = r.path === 'u' ? (r.parts[1] || '') : '';
    var url = username ? '/api/profile?username=' + encodeURIComponent(username) : '/api/profile' + (id ? '?user=' + encodeURIComponent(id) : '');
    main.innerHTML = '<div class="center" style="padding:20px">' + UI.spinner() + '</div>';
    Api.request('GET', url, null, function (err, data) {
      if (err) {
        main.innerHTML = '<div class="card"><div class="card-b"><div class="alert err">' + UI.esc(Api.errorText(err)) + '</div></div></div>';
        return;
      }
      var u = data.user;
      if (username && u && String(u.id) !== String(id)) {
        // canonicalize /u/name -> /profile/id without adding history noise
        try { history.replaceState(null, '', '#/profile/' + u.id); } catch (e) { }
      }
      var isSelf = u.isSelf;
      var actions = '';
      if (isSelf) actions += '<a class="btn btn-primary" href="#/settings">' + UI.icon('pen') + ' ' + T('profile.edit') + '</a>';
      else {
        if (u.isFriend) {
          actions += '<a class="btn btn-primary" href="#/im/' + u.id + '">' + UI.icon('envelope') + ' ' + T('profile.message') + '</a>';
          actions += '<button type="button" class="btn friend-action" data-user="' + u.id + '" data-action="remove">' + UI.icon('user-minus') + ' ' + T('profile.removeFriend') + '</button>';
        } else if (u.requestPending) {
          actions += '<button type="button" class="btn friend-action" data-user="' + u.id + '" data-action="remove">' + UI.icon('clock') + ' ' + T('profile.cancelRequest') + '</button>';
        } else if (u.incomingRequest) {
          actions += '<button type="button" class="btn btn-ok friend-action" data-user="' + u.id + '" data-action="accept">' + UI.icon('check') + ' ' + T('profile.accept') + '</button>';
          actions += '<button type="button" class="btn friend-action" data-user="' + u.id + '" data-action="decline">' + UI.icon('xmark') + ' ' + T('profile.decline') + '</button>';
        } else if (!u.blocked) {
          actions += '<button type="button" class="btn btn-primary friend-action" data-user="' + u.id + '" data-action="request">' + UI.icon('user-plus') + ' ' + T('profile.addFriend') + '</button>';
        }
        if (u.canMessage) actions += '<a class="btn" href="#/im/' + u.id + '">' + UI.icon('envelope') + ' ' + T('profile.message') + '</a>';
      }

      var info = '';
      if (u.city) info += row(T('profile.city'), UI.esc(u.city));
      if (u.country) info += row(T('profile.country'), UI.esc(u.country));
      if (u.birthday) info += row(T('profile.birthday'), UI.esc(u.birthday));
      if (u.gender) info += row(T('profile.gender'), UI.esc(u.gender === 'm' ? T('profile.male') : u.gender === 'f' ? T('profile.female') : T('profile.other')));
      if (u.website) info += row(T('profile.website'), '<a href="' + UI.attr(UI.safeUrl(u.website)) + '" target="_blank" rel="noopener noreferrer nofollow">' + UI.esc(u.website) + '</a>');
      if (u.favGames) info += row(T('profile.favGames'), UI.esc(u.favGames));
      if (u.favMovies) info += row(T('profile.favMovies'), UI.esc(u.favMovies));
      if (u.favMusic) info += row(T('profile.favMusic'), UI.esc(u.favMusic));
      if (u.favBooks) info += row(T('profile.favBooks'), UI.esc(u.favBooks));
      info += row(T('profile.joined'), UI.formatDate(u.created));
      if (u.bio) info += '<div style="margin-top:10px;font-size:12.5px;white-space:pre-wrap">' + UI.nl2br(u.bio) + '</div>';

      var html = '<div class="profile-cover"></div>';
      html += '<div class="profile-head">' + UI.avatar(u, 96, false) +
        '<div class="profile-head-main"><h1>' + UI.esc(u.displayName) + ' ' + UI.roleBadge(u.role) + '</h1>' +
        '<div class="username">@' + UI.esc(u.username) + ' · ' + (u.online ? T('profile.online') : T('profile.offline')) + '</div>' +
        (u.status || u.mood ? '<div class="status">' + (u.mood ? UI.icon(moodIcon(u.mood)) + ' ' : '') + UI.esc(u.status || T('mood.' + u.mood)) + '</div>' : '') +
        badgeIcons(u.badges) + '</div>' +
        '<div class="profile-actions">' + actions + '</div></div>';

      html += '<div class="profile-grid">';
      html += '<div><div class="card"><div class="card-h">' + UI.icon('circle-info') + ' ' + T('profile.about') + '</div><div class="card-b">' +
        '<div class="stat-grid" style="margin-bottom:10px">' +
        '<div class="stat-card"><b>' + (u.friends || 0) + '</b><span>' + T('profile.friends') + '</span></div>' +
        '<div class="stat-card"><b>' + (u.posts || 0) + '</b><span>' + T('profile.posts') + '</span></div>' +
        '<div class="stat-card"><b>' + (u.likes || 0) + '</b><span>' + T('profile.likes') + '</span></div></div>' +
        '<table class="info-table">' + info + '</table></div></div>';
      html += badgesCard(u.badges, isSelf);
      if ((data.friends || []).length) {
        html += '<div class="card"><div class="card-h">' + UI.icon('user-group') + ' ' + T('profile.friends') + ' <span class="right"><a href="#/friends?user=' + u.id + '">' + T('common.all') + '</a></span></div><div class="card-b"><div class="user-grid">';
        for (var f = 0; f < data.friends.length; f++) html += userCard(data.friends[f]);
        html += '</div></div></div>';
      }
      if ((data.groups || []).length) {
        html += '<div class="card"><div class="card-h">' + UI.icon('people-group') + ' ' + T('profile.groups') + '</div><div class="card-b">';
        for (var g = 0; g < data.groups.length; g++) html += '<a class="online-user" href="#/group/' + data.groups[g].id + '">' + UI.icon('people-group') + ' <span class="name">' + UI.esc(data.groups[g].name) + '</span></a>';
        html += '</div></div>';
      }
      html += '</div>';

      html += '<div>';
      if (data.canPost) html += composerHtml(u.id);
      html += '<div id="wall-list"></div><div id="wall-more"></div></div>';
      html += '</div>';
      main.innerHTML = html;
      bindFriendActions(main);
      if (data.canPost) bindComposer(u.id);
      var wall = data.posts || [];
      var list = document.getElementById('wall-list');
      if (list) {
        if (!wall.length) list.innerHTML = '<div class="card"><div class="card-b">' + UI.empty('inbox', T('feed.empty'), T('feed.emptyHint')) + '</div></div>';
        else {
          var wh = '';
          for (var w = 0; w < wall.length; w++) wh += postHtml(wall[w]);
          list.innerHTML = wh;
          bindPosts(list);
        }
      }
      var more = document.getElementById('wall-more');
      if (more && data.posts && data.posts.length >= 10) {
        more.innerHTML = '<button type="button" class="load-more" id="wall-load-more">' + T('feed.loadMore') + '</button>';
        var mb = document.getElementById('wall-load-more');
        var next = data.posts[data.posts.length - 1].id;
        mb.addEventListener('click', function () {
          mb.disabled = true;
          Api.request('GET', '/api/wall?user=' + u.id + '&before=' + next + '&limit=10', null, function (err2, data2) {
            mb.disabled = false;
            if (err2) { UI.toast(Api.errorText(err2), 'err'); return; }
            var posts = data2.posts || [];
            for (var i = 0; i < posts.length; i++) list.insertAdjacentHTML('beforeend', postHtml(posts[i]));
            bindPosts(list);
            if (!posts.length || !data2.hasMore) mb.parentNode.removeChild(mb);
            else next = data2.next;
          });
        });
      }
    });
  }

  function row(label, value) {
    return '<tr><td>' + UI.esc(label) + '</td><td>' + value + '</td></tr>';
  }

  function userCard(u) {
    var action = '';
    if (state.user && Number(u.id) === Number(state.user.id)) action = '<a class="btn btn-sm" href="#/profile/' + u.id + '">' + T('nav.profile') + '</a>';
    else if (u.isFriend) action = '<a class="btn btn-sm" href="#/im/' + u.id + '">' + T('profile.message') + '</a>';
    else if (u.requestPending) action = '<button class="btn btn-sm" disabled>' + T('profile.cancelRequest') + '</button>';
    else if (u.incomingRequest) action = '<button class="btn btn-sm btn-ok friend-action" data-user="' + u.id + '" data-action="accept">' + T('profile.accept') + '</button>';
    else action = '<button class="btn btn-sm friend-action" data-user="' + u.id + '" data-action="request">' + T('profile.addFriend') + '</button>';
    return '<div class="user-card"><a href="#/u/' + encodeURIComponent(u.username) + '">' + UI.avatar(u, 52) + '</a>' +
      '<a class="name" href="#/u/' + encodeURIComponent(u.username) + '">' + UI.esc(u.displayName) + '</a>' +
      '<div class="sub">' + (u.online ? T('profile.online') : '@' + UI.esc(u.username)) + '</div>' + action + '</div>';
  }

  function bindFriendActions(scope) {
    var btns = (scope || document).querySelectorAll('.friend-action');
    for (var i = 0; i < btns.length; i++) {
      if (btns[i].getAttribute('data-bound')) continue;
      btns[i].setAttribute('data-bound', '1');
      btns[i].addEventListener('click', function () { friendAction(this.getAttribute('data-user'), this.getAttribute('data-action'), this); });
    }
  }

  function friendAction(userId, action, btn) {
    btn.disabled = true;
    Api.request('POST', '/api/friend', { user: Number(userId), action: action }, function (err, data) {
      btn.disabled = false;
      if (err) { UI.toast(Api.errorText(err), 'err'); return; }
      var msg = data.status === 'accepted' ? T('friends.accepted') : data.status === 'removed' ? T('friends.removed') : data.status === 'declined' ? T('friends.declined') : data.status === 'blocked' ? T('friends.blocked') : T('friends.requestSent');
      UI.toast(msg, 'ok');
      SoundFX.play('friend');
      route();
    });
  }

  // ------------------------------------------------------------------ common

  function saveSetting(patch) {
    if (!state.authed) return;
    Api.request('POST', '/api/settings', patch, function (err) {
      if (err) { /* silent: theme/lang are local too */ }
    });
  }

  function notFoundView(main) {
    main.innerHTML = '<div class="card"><div class="card-b">' + UI.empty('compass', '404', T('errors.notFound')) + '</div></div>';
  }

  function bindGlobal() {
    window.addEventListener('hashchange', function () {
      if (!state.authed) return;
      route();
    });
    document.addEventListener('click', function (e) {
      var t = e.target;
      while (t && t !== document) {
        if (t.getAttribute && t.getAttribute('data-auth')) {
          e.preventDefault();
          renderAuth(t.getAttribute('data-auth'));
          return;
        }
        if (t.className && String(t.className).indexOf('comment-delete') >= 0) {
          e.preventDefault();
          deleteComment(t.getAttribute('data-id'));
          return;
        }
        t = t.parentNode;
      }
    });
    Api.onAuthLost = function () {
      state.authed = false;
      state.me = null;
      state.user = null;
      renderAuth('login');
      UI.toast(T('errors.session'), 'err');
    };
  }

  // public api
  var api = {
    state: state,
    views: views,
    init: init,
    route: route,
    go: go,
    T: T,
    qs: qs,
    renderShell: renderShell,
    renderAuth: renderAuth,
    loadOnline: loadOnline,
    loadStats: loadStats,
    userCard: userCard,
    bindFriendActions: bindFriendActions,
    friendAction: friendAction,
    postHtml: postHtml,
    bindPosts: bindPosts,
    badgeCatalog: badgeCatalog,
    badgeList: badgeList,
    badgesHtml: badgesHtml,
    badgesCard: badgesCard,
    videoHtml: videoHtml,
    moodIcon: moodIcon,
    composerHtml: composerHtml,
    bindComposer: bindComposer,
    commentHtml: commentHtml,
    saveSetting: saveSetting,
    setBusy: setBusy,
    val: val,
    updateBadges: updateBadges,
    toast: UI.toast,
    doLogout: doLogout,
    onLoggedIn: onLoggedIn,
    stopTimers: stopTimers
  };

  views.feed = feedView;
  views.post = postView;
  views.profile = profileView;
  views.notfound = notFoundView;

  return api;
})();
