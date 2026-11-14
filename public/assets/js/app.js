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
    notify: { started: false, last: 0, unreadAt: 0 },
    composerDraftId: 0,
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
        startNotifyStream();
        if (window.CallUI) CallUI.init();
        updateTitle((state.me && state.me.unreadNotifications) || 0);
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
    if (state.site && state.site.liveNotifications) return;
    if (state.badgeTimer) return;
    state.badgeTimer = setInterval(function () {
      if (!state.authed || document.hidden) return;
      Api.request('GET', '/api/me', null, function (err, data) {
        if (err) return;
        state.me = data.me || state.me;
        state.user = (data.me && data.me.user) || state.user;
        updateBadges();
      });
    }, 20000);
  }

  function stopBadgePoll() {
    if (state.badgeTimer) { clearInterval(state.badgeTimer); state.badgeTimer = null; }
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

  function updateTitle(n) {
    var name = (state.site && state.site.name) || 'Social';
    document.title = (n > 0 ? '(' + (n > 99 ? '99+' : n) + ') ' : '') + name;
  }

  function desktopNotify(title, body) {
    try {
      if (!window.Notification || Notification.permission !== 'granted') return;
      new Notification(title, { body: body, icon: 'assets/img/favicon.svg' });
    } catch (e) { }
  }

  function announce(n) {
    if (!n) return;
    var a = n.actor || {};
    var name = a.displayName || a.username || T('notifications.someone');
    var text = name + ' ' + n.text;
    UI.toast(text, 'info');
    SoundFX.play('notification');
    if (document.hidden) desktopNotify(T('notifications.title'), text);
  }

  /// Applies an unread counter, but lets a local mark-read win for a moment so a
  /// long-poll reply that was already in flight cannot bring the badge back up.
  function applyUnread(n) {
    if (typeof n !== 'number') return;
    if (Date.now() - (state.notify.unreadAt || 0) < 2500) return;
    setBadge('badge-notif', n);
    updateTitle(n);
  }

  /// Authoritative update from an action the user just performed.
  function setUnreadLocal(n) {
    state.notify.unreadAt = Date.now();
    if (typeof n !== 'number') return;
    setBadge('badge-notif', n);
    updateTitle(n);
  }

  /// Long-poll the server for fresh notifications and announce them instantly.
  /// Every start bumps a token; a stale loop exits on its next callback instead of
  /// piling up another long poll (which is what used to make the tab feel heavy).
  function startNotifyStream() {
    var n = state.notify;
    if (n.started || !state.authed) return;
    if (!(state.site && state.site.liveNotifications)) return;
    n.started = true;
    n.token = (n.token || 0) + 1;
    var token = n.token;

    function alive() { return state.authed && n.token === token; }

    Api.request('GET', '/api/notifications?limit=1', null, function (err, data) {
      if (!alive()) return;
      if (err) { n.started = false; setTimeout(startNotifyStream, 5000); return; }
      var list = (data && data.notifications) || [];
      n.last = list.length ? list[0].id : 0;
      if (data && typeof data.unread === 'number') updateTitle(data.unread);
      pump();
    });

    function pump() {
      if (!alive()) return;
      Api.request('GET', '/api/notifications/wait?after=' + n.last, null, function (err, data) {
        if (!alive()) return;
        if (err) { setTimeout(pump, 5000); return; }
        if (data && data.busy) { setTimeout(pump, 3000); return; }
        var fresh = (data && data.notifications) || [];
        if (data && data.latest && data.latest > n.last) n.last = data.latest;
        if (fresh.length) {
          // oldest first so the toasts stack in the right order
          for (var i = fresh.length - 1; i >= 0; i--) announce(fresh[i]);
          if (typeof data.unread === 'number') {
            state.notify.unreadAt = 0;
            setBadge('badge-notif', data.unread);
            updateTitle(data.unread);
          }
          if (state.route === 'notifications') route();
        } else {
          applyUnread(data && data.unread);
        }
        pump();
      });
    }
  }

  function stopNotifyStream() {
    var n = state.notify;
    n.started = false;
    n.token = (n.token || 0) + 1;
  }

  // ------------------------------------------------------------- feedback

  /// Anonymous note to the operators. Works from the login screen too, so it
  /// deliberately avoids the sealed API and posts plain JSON.
  function openFeedbackModal() {
    var body = '<div class="hint" style="margin-bottom:10px">' + T('feedback.hint') + '</div>' +
      '<div class="field"><label>' + T('feedback.kind') + '</label><select class="select" id="fb-kind">' +
      '<option value="bug">' + T('feedback.kindBug') + '</option>' +
      '<option value="idea">' + T('feedback.kindIdea') + '</option>' +
      '<option value="other">' + T('feedback.kindOther') + '</option>' +
      '</select></div>' +
      '<div class="field"><label>' + T('feedback.message') + '</label>' +
      '<textarea class="textarea" id="fb-message" rows="6" maxlength="2000" placeholder="' +
      UI.attr(T('feedback.placeholder')) + '"></textarea></div>' +
      '<div class="field"><label>' + T('feedback.contact') + '</label>' +
      '<input class="input" id="fb-contact" maxlength="120" placeholder="' + UI.attr(T('feedback.contactHint')) + '"></div>' +
      // honeypot: hidden from humans, irresistible to bots
      '<div style="position:absolute;left:-9999px;top:-9999px" aria-hidden="true">' +
      '<label>Website<input type="text" id="fb-website" tabindex="-1" autocomplete="off"></label></div>' +
      '<div class="tiny muted">' + T('feedback.anon') + '</div>';

    UI.modal({
      title: T('feedback.title'),
      icon: 'comment-dots',
      body: body,
      actions: [
        { label: T('common.cancel'), cls: 'btn-ghost' },
        {
          label: T('common.send'), cls: 'btn-primary', icon: 'paper-plane',
          onClick: function (box, close) {
            var text = box.querySelector('#fb-message').value.trim();
            if (text.length < 3) { UI.toast(T('feedback.needText'), 'err'); return false; }
            var btn = box.querySelectorAll('.modal-f .btn');
            for (var i = 0; i < btn.length; i++) btn[i].disabled = true;
            Api.sendPlain('/api/feedback', {
              kind: box.querySelector('#fb-kind').value,
              message: text,
              contact: box.querySelector('#fb-contact').value,
              page: location.hash || '',
              website: box.querySelector('#fb-website').value
            }, function (err) {
              for (var k = 0; k < btn.length; k++) btn[k].disabled = false;
              if (err) { UI.toast(Api.errorText(err), 'err'); return; }
              UI.toast(T('feedback.sent'), 'ok');
              SoundFX.play('success');
              if (close) close();
            });
            return false;   // stay open until the request settles
          }
        }
      ]
    });
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
    html += tab('#/profile/' + u.id, 'id-card', T('nav.profile'), 'profile');
    html += tab('#/notifications', 'bell', T('nav.notifications'), 'notifications', unreadN, 'badge-notif');
    html += tab('#/settings', 'gear', T('nav.settings'), 'settings');
    // the admin board is the only place where the extra tab is the point
    if (isMod && site.adminBoard) html += tab('#/admin', 'shield-halved', T('nav.admin'), 'admin');
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
    html += side('#/drafts', 'file-pen', T('nav.drafts'), 'drafts');
    html += side('#/music', 'music', T('nav.music'), 'music');
    html += side('#/video', 'film', T('nav.video'), 'video');
    html += side('#/calls', 'phone', T('nav.calls'), 'calls');
    if (!App.state.site || App.state.site.giftsEnabled !== false)
      html += side('#/gifts', 'gift', T('nav.gifts'), 'gifts');
    if (!App.state.site || App.state.site.gamesEnabled !== false)
      html += side('#/games', 'gamepad', T('nav.games'), 'games');
    html += '<div class="side-sep"></div>';
    html += side('#/profile/' + u.id, 'id-card', T('nav.profile'), 'profile');
    html += side('#/settings', 'gear', T('nav.settings'), 'settings');
    if (isMod) html += side('#/admin', 'shield-halved', T('nav.admin'), 'admin');
    html += '<div class="side-sep"></div>';
    html += '<button type="button" class="side-link" id="feedback-btn" style="width:100%;border:0;background:transparent;text-align:left">' + UI.icon('comment-dots') + ' ' + T('feedback.nav') + '</button>';
    html += '<button type="button" class="side-link" id="logout-btn" style="width:100%;border:0;background:transparent;text-align:left">' + UI.icon('right-from-bracket') + ' ' + T('nav.logout') + '</button>';
    html += '</div>';
    html += '<div class="card online-card"><div class="card-h">' + UI.icon('signal') + ' ' + T('online.title') + '</div><div class="online-list" id="online-mini">' + UI.spinner() + '</div></div>';
    html += '</aside>';

    html += '<main class="col-main" id="main-col"></main>';

    html += '<aside class="col-right">';
    if (site.motd) html += '<div class="card"><div class="card-h">' + UI.icon('bullhorn') + ' ' + T('motd.title') + '</div><div class="card-b" style="font-size:12px">' + UI.nl2br(site.motd) + '</div></div>';
    html += '<div class="card"><div class="card-h">' + UI.icon('hashtag') + ' ' + T('tags.title') + '</div><div class="card-b" id="tags-box">' + UI.spinner() + '</div></div>';
    html += '<div class="card"><div class="card-h">' + UI.icon('user-group') + ' ' + T('online.friends') + '</div><div class="online-list" id="friends-online">' + UI.spinner() + '</div></div>';
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
    loadTags();
    loadFriendsOnline();
  }

  /// Right column block: friends who are online right now, like VK's "Friends
  /// online". Reuses /api/friends so there is no new endpoint to keep in sync.
  function loadFriendsOnline() {
    Api.request('GET', '/api/friends', null, function (err, data) {
      var box = document.getElementById('friends-online');
      if (!box) return;
      if (err) { box.innerHTML = '<div class="tiny muted" style="padding:8px">' + T('online.empty') + '</div>'; return; }
      var list = (data && data.friends) || [];
      var online = [];
      for (var i = 0; i < list.length; i++) if (list[i].online) online.push(list[i]);
      if (!online.length) {
        box.innerHTML = '<div class="tiny muted" style="padding:8px">' +
          (list.length ? T('online.noFriendsOnline') : T('friends.empty')) + '</div>';
        return;
      }
      var html = '';
      for (var j = 0; j < online.length && j < 12; j++) {
        html += '<a class="online-user" href="#/u/' + encodeURIComponent(online[j].username) + '">' +
          UI.avatar(online[j], 24) + '<span class="name">' + UI.esc(online[j].displayName) + '</span>' +
          '<span class="presence"></span></a>';
      }
      box.innerHTML = html;
    });
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
    var fb = document.getElementById('feedback-btn');
    if (fb) fb.addEventListener('click', openFeedbackModal);
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

  function loadTags() {
    Api.request('GET', '/api/tags', null, function (err, data) {
      var box = document.getElementById('tags-box');
      if (!box) return;
      if (err) { box.innerHTML = '<div class="tiny muted">' + T('tags.empty') + '</div>'; return; }
      var tags = (data && data.tags) || [];
      if (!tags.length) { box.innerHTML = '<div class="tiny muted">' + T('tags.empty') + '</div>'; return; }
      var html = '<div class="tag-cloud">';
      for (var i = 0; i < tags.length; i++) {
        html += '<a class="tag-chip" href="#/tag/' + encodeURIComponent(tags[i].tag) + '">#' +
          UI.esc(tags[i].tag) + ' <b>' + tags[i].count + '</b></a>';
      }
      box.innerHTML = html + '</div>';
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
    // stop a running voice recording and invalidate the chat long poll
    if (state.im && state.im.stopRec) { try { state.im.stopRec(); } catch (e) { } }
    if (state.im) state.im.loopToken = (state.im.loopToken || 0) + 1;
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
    html += '<div class="center" style="margin-top:6px"><button type="button" class="btn btn-ghost btn-xs" id="feedback-btn-auth">' +
      UI.icon('comment-dots') + ' ' + T('feedback.nav') + '</button></div>';
    html += '</div>';
    html += '<div class="landing-side-note"><button type="button" class="btn btn-ghost btn-sm" id="theme-toggle-auth">' + UI.icon('circle-half-stroke') + ' ' + T('theme.' + UI.getTheme()) + '</button></div>';
    html += '</div></div>';

    root().innerHTML = html;
    var tt = document.getElementById('theme-toggle-auth');
    if (tt) tt.addEventListener('click', function () {
      var next = UI.cycleTheme();
      tt.innerHTML = UI.icon('circle-half-stroke') + ' ' + T('theme.' + next);
    });
    var fba = document.getElementById('feedback-btn-auth');
    if (fba) fba.addEventListener('click', openFeedbackModal);
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
    stopNotifyStream();
    startNotifyStream();
    startBadgePoll();
    if (window.CallUI) CallUI.init();
    updateTitle((state.me && state.me.unreadNotifications) || 0);
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
      stopTimers();
      stopNotifyStream();
      stopBadgePoll();
      if (window.CallUI) CallUI.stop();
      state.notify.last = 0;
      updateTitle(0);
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
    var draftId = Number(qs('draft') || 0);
    if (draftId > 0) loadDraftIntoComposer(draftId);
    loadFeed(false);
  }

  /// Fills the composer from a saved draft and remembers its id so publishing
  /// removes the draft again.
  function loadDraftIntoComposer(id) {
    Api.request('GET', '/api/drafts', null, function (err, data) {
      if (err) { UI.toast(Api.errorText(err), 'err'); return; }
      var list = (data && data.drafts) || [];
      var d = null;
      for (var i = 0; i < list.length; i++) if (Number(list[i].id) === Number(id)) d = list[i];
      if (!d) { UI.toast(T('feed.draftMissing'), 'err'); return; }
      state.composerDraftId = d.id;
      var text = document.getElementById('composer-text');
      if (text) {
        text.value = d.body || '';
        text.dispatchEvent(new Event('input', { bubbles: true }));
      }
      if (d.video) {
        var vBtn = document.getElementById('composer-video');
        var vInput = document.getElementById('composer-video-url');
        var vRow = document.getElementById('composer-video-row');
        if (vBtn && vRow) vBtn.click();
        if (vInput) {
          vInput.value = d.video;
          vInput.dispatchEvent(new Event('input', { bubbles: true }));
        }
      }
      var vis = document.getElementById('composer-visibility');
      if (vis && d.visibility) vis.value = d.visibility;
      if (d.pollQuestion) {
        var pBtn = document.getElementById('composer-poll-btn');
        if (pBtn) pBtn.click();
        var pq = document.getElementById('composer-poll-q');
        if (pq) pq.value = d.pollQuestion;
        var opts = String(d.pollOptions || '').split('\n');
        var add = document.getElementById('composer-poll-add');
        var rows = document.querySelectorAll('#composer-poll-options input');
        for (var k = 0; k < opts.length; k++) {
          if (rows[k]) rows[k].value = opts[k];
          else if (add && opts[k]) { add.click(); rows = document.querySelectorAll('#composer-poll-options input'); if (rows[k]) rows[k].value = opts[k]; }
        }
      }
      UI.toast(T('feed.draftLoaded'), 'ok');
      if (text) text.focus();
    });
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
      '<div class="mention-pop hidden" id="composer-mentions"></div>' +
      '<div class="composer-video-row hidden" id="composer-video-row">' +
      '<div class="composer-video-input">' + UI.icon('video') +
      '<input class="input" id="composer-video-url" placeholder="' + UI.attr(T('feed.videoPlaceholder')) + '" autocomplete="off"></div>' +
      '<div class="composer-video-preview" id="composer-video-preview"></div>' +
      '</div>' +
      '<div class="composer-poll hidden" id="composer-poll">' +
      '<div class="poll-editor-head">' + UI.icon('square-poll-vertical') + '<span>' + T('feed.poll') + '</span>' +
      '<button type="button" class="icon-btn" id="composer-poll-x" title="' + UI.attr(T('common.cancel')) + '">' + UI.icon('xmark') + '</button></div>' +
      '<input class="input" id="composer-poll-q" maxlength="160" placeholder="' + UI.attr(T('feed.pollQuestion')) + '">' +
      '<div id="composer-poll-options"></div>' +
      '<button type="button" class="btn btn-xs" id="composer-poll-add">' + UI.icon('plus') + ' ' + T('feed.pollAddOption') + '</button>' +
      '</div>' +
      '<div class="composer-sched hidden" id="composer-sched">' + UI.icon('clock') +
      '<input class="input" type="datetime-local" id="composer-sched-at">' +
      '<button type="button" class="icon-btn" id="composer-sched-x">' + UI.icon('xmark') + '</button></div>' +
      '<div class="composer-bar">' +
      '<button type="button" class="btn btn-sm" id="composer-photo">' + UI.icon('image') + ' ' + T('feed.photo') + '</button>' +
      '<button type="button" class="btn btn-sm" id="composer-video">' + UI.icon('video') + ' ' + T('feed.video') + '</button>' +
      '<button type="button" class="btn btn-sm" id="composer-poll-btn">' + UI.icon('square-poll-vertical') + ' ' + T('feed.poll') + '</button>' +
      '<button type="button" class="btn btn-sm" id="composer-sched-btn">' + UI.icon('clock') + ' ' + T('feed.schedule') + '</button>' +
      '<button type="button" class="btn btn-sm" id="composer-draft-btn">' + UI.icon('file-pen') + ' ' + T('feed.saveDraft') + '</button>' +
      '<input type="file" id="composer-file" accept="image/png,image/jpeg,image/gif,image/webp" class="hidden">' +
      '<select class="select" id="composer-visibility" style="width:auto;padding:4px 24px 4px 8px;font-size:11.5px">' +
      '<option value="public">' + T('feed.public') + '</option>' +
      '<option value="friends">' + T('feed.friendsOnly') + '</option>' +
      '<option value="private">' + T('feed.private') + '</option>' +
      '</select>' +
      '<button type="submit" class="btn btn-primary btn-sm right" id="composer-submit">' + UI.icon('paper-plane') + ' ' + T('feed.post') + '</button>' +
      '</div></form>';
  }

  function bindComposer(wallId, groupId) {
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
    var pop = document.getElementById('composer-mentions');
    var pollBtn = document.getElementById('composer-poll-btn');
    var pollBox = document.getElementById('composer-poll');
    var pollQ = document.getElementById('composer-poll-q');
    var pollOptions = document.getElementById('composer-poll-options');
    var schedBtn = document.getElementById('composer-sched-btn');
    var schedBox = document.getElementById('composer-sched');
    var schedAt = document.getElementById('composer-sched-at');
    var draftBtn = document.getElementById('composer-draft-btn');
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

    // ------------------------------------------------------------ poll editor
    var pollSlots = 0;

    function addPollOption(value) {
      if (!pollOptions || pollSlots >= 6) return;
      pollSlots++;
      var wrap = document.createElement('div');
      wrap.className = 'poll-option-row';
      wrap.innerHTML = '<input class="input" maxlength="80" placeholder="' + UI.attr(T('feed.pollOption')) + ' ' + pollSlots + '">' +
        '<button type="button" class="icon-btn poll-option-x">' + UI.icon('xmark') + '</button>';
      var input = wrap.querySelector('input');
      if (value) input.value = value;
      wrap.querySelector('.poll-option-x').addEventListener('click', function () {
        if (pollOptions.querySelectorAll('.poll-option-row').length <= 2) {
          input.value = '';
          return;
        }
        wrap.parentNode.removeChild(wrap);
        pollSlots--;
      });
      pollOptions.appendChild(wrap);
    }

    function resetPoll() {
      if (!pollBox) return;
      pollBox.className = 'composer-poll hidden';
      if (pollQ) pollQ.value = '';
      if (pollOptions) pollOptions.innerHTML = '';
      pollSlots = 0;
    }

    if (pollBtn && pollBox) pollBtn.addEventListener('click', function () {
      var open = pollBox.className.indexOf('hidden') < 0;
      if (open) { resetPoll(); return; }
      pollBox.className = 'composer-poll';
      addPollOption('');
      addPollOption('');
      if (pollQ) pollQ.focus();
    });
    var pollX = document.getElementById('composer-poll-x');
    if (pollX) pollX.addEventListener('click', resetPoll);
    var pollAdd = document.getElementById('composer-poll-add');
    if (pollAdd) pollAdd.addEventListener('click', function () { addPollOption(''); });

    // -------------------------------------------------------- scheduling
    if (schedBtn && schedBox) schedBtn.addEventListener('click', function () {
      var open = schedBox.className.indexOf('hidden') < 0;
      schedBox.className = open ? 'composer-sched hidden' : 'composer-sched';
      if (!open && schedAt) {
        // default to an hour from now, in local time
        var d = new Date(Date.now() + 3600000);
        var pad = function (n) { return n < 10 ? '0' + n : '' + n; };
        schedAt.value = d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) +
          'T' + pad(d.getHours()) + ':' + pad(d.getMinutes());
        schedAt.focus();
      }
    });
    var schedX = document.getElementById('composer-sched-x');
    if (schedX) schedX.addEventListener('click', function () {
      if (schedAt) schedAt.value = '';
      if (schedBox) schedBox.className = 'composer-sched hidden';
    });

    // ------------------------------------------------------------ drafts
    function currentDraft() {
      var question = pollQ ? pollQ.value.trim() : '';
      var opts = [];
      var inputs = pollOptions ? pollOptions.querySelectorAll('input') : [];
      for (var i = 0; i < inputs.length; i++) {
        var v = inputs[i].value.trim();
        if (v) opts.push(v);
      }
      return {
        body: text ? text.value.trim() : '',
        video: videoInput ? videoInput.value.trim() : '',
        visibility: val('composer-visibility') || 'public',
        wall: wallId || 0,
        group: groupId || 0,
        poll: question ? { question: question, options: opts } : null
      };
    }

    if (draftBtn) draftBtn.addEventListener('click', function () {
      var d = currentDraft();
      if (!d.body && !d.video && !d.poll) { UI.toast(T('feed.draftEmpty'), 'err'); return; }
      draftBtn.disabled = true;
      d.id = state.composerDraftId || 0;
      Api.request('POST', '/api/drafts/save', d, function (err, data) {
        draftBtn.disabled = false;
        if (err) { UI.toast(Api.errorText(err), 'err'); return; }
        state.composerDraftId = data && data.id ? data.id : 0;
        UI.toast(T('feed.draftSaved'), 'ok');
        SoundFX.play('success');
      });
    });

    // ------------------------------------------------------ @mention helper
    var mentionSeq = 0;

    function hideMentions() {
      if (!pop) return;
      pop.className = 'mention-pop hidden';
      pop.innerHTML = '';
    }

    function currentMention() {
      if (!text || typeof text.selectionStart !== 'number') return null;
      var pos = text.selectionStart;
      var before = text.value.slice(0, pos);
      var m = /(^|[^\w@])@([A-Za-z0-9_.-]{1,24})$/.exec(before);
      if (!m) return null;
      return { name: m[2], start: pos - m[2].length - 1, end: pos };
    }

    function insertMention(name) {
      var m = currentMention();
      if (!m || !text) return;
      var before = text.value.slice(0, m.start);
      var after = text.value.slice(m.end);
      text.value = before + '@' + name + ' ' + after;
      var pos = (before + '@' + name + ' ').length;
      try { text.setSelectionRange(pos, pos); } catch (e) { }
      hideMentions();
      text.focus();
      text.style.height = 'auto';
      text.style.height = Math.min(220, text.scrollHeight) + 'px';
    }

    function lookupMentions() {
      if (!text || !pop) return;
      var m = currentMention();
      if (!m) { hideMentions(); return; }
      var seq = ++mentionSeq;
      Api.request('GET', '/api/people?q=' + encodeURIComponent(m.name) + '&limit=6', null, function (err, data) {
        if (seq !== mentionSeq) return;
        if (err) { hideMentions(); return; }
        var users = (data && data.users) || [];
        if (!users.length) { hideMentions(); return; }
        var html = '';
        for (var i = 0; i < users.length; i++) {
          html += '<button type="button" class="mention-item" data-name="' + UI.attr(users[i].username) + '">' +
            UI.avatar(users[i], 22) + '<span>' + UI.esc(users[i].displayName || users[i].username) +
            ' <em>@' + UI.esc(users[i].username) + '</em></span></button>';
        }
        pop.innerHTML = html;
        pop.className = 'mention-pop';
        var items = pop.querySelectorAll('.mention-item');
        for (var j = 0; j < items.length; j++) {
          items[j].addEventListener('mousedown', function (e) {
            if (e) e.preventDefault();
            insertMention(this.getAttribute('data-name'));
          });
        }
      });
    }

    if (text) {
      text.addEventListener('input', lookupMentions);
      text.addEventListener('click', lookupMentions);
      text.addEventListener('keydown', function (e) {
        var key = e.keyCode || e.which;
        if (key === 27) { hideMentions(); return; }
        if (key === 13 && pop && pop.className.indexOf('hidden') < 0) {
          var first = pop.querySelector('.mention-item');
          if (first) {
            e.preventDefault();
            insertMention(first.getAttribute('data-name'));
          }
        }
      });
      text.addEventListener('blur', function () { setTimeout(hideMentions, 200); });
    }
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

      // poll, when the editor is open
      var poll = null;
      if (pollBox && pollBox.className.indexOf('hidden') < 0) {
        var question = pollQ ? pollQ.value.trim() : '';
        var opts = [];
        var inputs = pollOptions ? pollOptions.querySelectorAll('input') : [];
        for (var pi = 0; pi < inputs.length; pi++) {
          var ov = inputs[pi].value.trim();
          if (ov) opts.push(ov);
        }
        if (question.length < 2) { UI.toast(T('feed.pollNeedQuestion'), 'err'); if (pollQ) pollQ.focus(); return; }
        if (opts.length < 2) { UI.toast(T('feed.pollNeedOptions'), 'err'); return; }
        poll = { question: question, options: opts };
      }

      if (!body && !imageData && !videoUrl && !poll) { if (text) text.focus(); return; }
      var scheduledFor = '';
      if (schedAt && schedAt.value && schedBox && schedBox.className.indexOf('hidden') < 0) {
        var when = new Date(schedAt.value);
        if (isNaN(when.getTime()) || when.getTime() <= Date.now()) {
          UI.toast(T('feed.scheduleFuture'), 'err');
          return;
        }
        scheduledFor = when.toISOString();
      }
      var btn = document.getElementById('composer-submit');
      setBusy(btn, true, T('feed.posting'));
      var payload = {
        body: body,
        visibility: val('composer-visibility') || 'public',
        wall: wallId || 0,
        group: groupId || 0,
        image: imageData || '',
        video: videoUrl,
        poll: poll,
        scheduledFor: scheduledFor
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
        resetPoll();
        if (schedAt) schedAt.value = '';
        if (schedBox) schedBox.className = 'composer-sched hidden';
        // a published draft is no longer a draft
        if (state.composerDraftId) {
          Api.request('POST', '/api/drafts/delete', { id: state.composerDraftId }, function () { });
          state.composerDraftId = 0;
        }
        SoundFX.play('post');
        UI.toast(scheduledFor ? T('feed.scheduled') : T('feed.posted'), 'ok');
        if (state.route === 'feed' && !groupId && !scheduledFor) {
          state.feed.posts.unshift(data.post);
          renderFeedList(null);
          loadTags();
        } else if (wallId || groupId || scheduledFor) {
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
      (p.wallUser ? '<span>· ' + UI.icon('arrow-right') + ' ' + UI.esc(p.wallUser.displayName) + '</span>' : '') +
      (p.pinned ? '<span class="pin-mark">' + UI.icon('thumbtack') + ' ' + T('feed.pinned') + '</span>' : '');
    var body = UI.linkify(p.body || '').replace(/\n/g, '<br>');
    var img = UI.safeUrl(p.image);
    var vid = videoHtml(p.video);
    var poll = pollHtml(p.poll);
    var html = '<article class="post" data-post="' + p.id + '">' +
      '<div class="post-head">' + UI.avatar(author, 38) +
      '<div class="post-who"><div class="meta">' + meta + '</div></div>' +
      '<div class="post-menu">' +
      (p.canPin ? '<button type="button" class="icon-btn post-pin' + (p.pinned ? ' active' : '') + '" data-id="' + p.id + '" data-on="' + (p.pinned ? '1' : '0') + '" title="' + UI.attr(T(p.pinned ? 'feed.unpin' : 'feed.pin')) + '">' + UI.icon('thumbtack') + '</button>' : '') +
      (p.canEdit ? '<button type="button" class="icon-btn post-edit" data-id="' + p.id + '" title="' + UI.attr(T('feed.editPost')) + '">' + UI.icon('pen') + '</button>' : '') +
      (p.canDelete ? '<button type="button" class="icon-btn post-delete" data-id="' + p.id + '" title="' + UI.attr(T('common.delete')) + '">' + UI.icon('trash') + '</button>' : '') +
      '</div>' +
      '</div>' +
      (body ? '<div class="post-body">' + body + '</div>' : '') +
      (img ? '<div class="post-media"><img class="post-img" src="' + UI.attr(img) + '" alt="" loading="lazy"></div>' : '') +
      vid +
      repostCardHtml(p) +
      poll +
      postReactionsHtml(p) +
      '<div class="post-actions">' +
      '<button type="button" class="pa post-like' + (p.liked ? ' liked' : '') + '" data-id="' + p.id + '">' + UI.icon(p.liked ? 'heart' : 'heart') + ' <span>' + (p.liked ? T('feed.liked') : T('feed.like')) + '</span> <b class="likes-count" data-id="' + p.id + '" title="' + UI.attr(T('feed.likedBy')) + '">' + (p.likes || 0) + '</b></button>' +
      '<button type="button" class="pa post-react" data-id="' + p.id + '" title="' + UI.attr(T('feed.react')) + '">' + UI.icon('face-smile') + ' <span>' + T('feed.react') + '</span></button>' +
      '<button type="button" class="pa post-comment-toggle" data-id="' + p.id + '">' + UI.icon('comment') + ' <span>' + T('feed.comment') + '</span> <b>' + (p.comments || 0) + '</b></button>' +
      '<button type="button" class="pa post-share" data-id="' + p.id + '">' + UI.icon('share') + ' <span>' + T('feed.share') + '</span> <b class="reposts-count">' + (p.reposts || 0) + '</b></button>' +
      '<button type="button" class="pa post-bookmark' + (p.bookmarked ? ' active' : '') + '" data-id="' + p.id + '" title="' + UI.attr(T('feed.bookmark')) + '">' + UI.icon('bookmark') + ' <span>' + (p.bookmarked ? T('feed.bookmarked') : T('feed.bookmark')) + '</span></button>' +
      '<a class="pa" href="#/post/' + p.id + '">' + UI.icon('up-right-from-square') + '</a>' +
      '</div>' +
      '<div class="comments hidden" data-comments="' + p.id + '"></div>' +
      '</article>';
    return html;
  }

  // -------------------------------------------------------------- reposts

  function repostCardHtml(p) {
    var r = p.repostOf;
    if (!r) return '';
    if (r.deleted) {
      return '<div class="repost-card deleted">' + UI.icon('circle-exclamation') + ' ' + T('feed.repostGone') + '</div>';
    }
    var a = p.repostAuthor || {};
    var img = UI.safeUrl(r.image);
    var html = '<a class="repost-card" href="#/post/' + r.id + '">' +
      '<div class="repost-head">' + UI.avatar(a, 24) +
      '<span class="name">' + UI.esc(a.displayName || a.username || '?') + '</span>' +
      '<span class="when">' + UI.timeAgo(r.created) + '</span></div>';
    if (r.body) html += '<div class="repost-body">' + UI.linkify(r.body).replace(/\n/g, '<br>') + '</div>';
    if (img) html += '<img class="repost-img" src="' + UI.attr(img) + '" alt="" loading="lazy">';
    else if (r.video) html += '<div class="repost-video">' + UI.icon('video') + ' ' + UI.esc(String(r.video).replace(/^https?:\/\//i, '').slice(0, 60)) + '</div>';
    html += '<div class="repost-foot">' + UI.icon('heart') + ' ' + (r.likes || 0) +
      ' · ' + UI.icon('comment') + ' ' + (r.comments || 0) +
      ' · ' + UI.icon('share') + ' ' + (r.reposts || 0) + '</div>';
    return html + '</a>';
  }

  var shareOutside = null;

  function closeShareMenu() {
    var menu = document.getElementById('share-menu');
    if (menu && menu.parentNode) menu.parentNode.removeChild(menu);
    if (shareOutside) {
      document.removeEventListener('mousedown', shareOutside, true);
      shareOutside = null;
    }
  }

  function openShareMenu(anchor, postId) {
    closeShareMenu();
    var menu = document.createElement('div');
    menu.className = 'share-menu';
    menu.id = 'share-menu';
    menu.innerHTML =
      '<button type="button" class="share-item" data-act="repost">' + UI.icon('retweet') + ' ' + T('feed.repost') + '</button>' +
      '<button type="button" class="share-item" data-act="link">' + UI.icon('link') + ' ' + T('feed.copyLink') + '</button>';
    document.body.appendChild(menu);

    var rect = anchor.getBoundingClientRect();
    var width = menu.offsetWidth || 190;
    var left = Math.max(8, Math.min(window.innerWidth - width - 8, rect.left));
    var top = rect.top - (menu.offsetHeight || 70) - 6;
    if (top < 8) top = rect.bottom + 6;
    menu.style.left = Math.round(left) + 'px';
    menu.style.top = Math.round(top) + 'px';

    menu.querySelector('[data-act="repost"]').addEventListener('click', function () {
      closeShareMenu();
      openRepostModal(postId);
    });
    menu.querySelector('[data-act="link"]').addEventListener('click', function () {
      closeShareMenu();
      sharePost(postId);
    });

    shareOutside = function (e) {
      if (menu.parentNode && !menu.contains(e.target)) closeShareMenu();
    };
    setTimeout(function () {
      if (shareOutside) document.addEventListener('mousedown', shareOutside, true);
    }, 0);
  }

  function openRepostModal(postId) {
    var body = '<div class="field"><label>' + T('feed.repostComment') + '</label>' +
      '<textarea class="textarea" id="rp-body" rows="3" maxlength="' + (state.site.maxPostLength || 8000) + '" placeholder="' + UI.attr(T('feed.repostPlaceholder')) + '"></textarea></div>' +
      '<div class="field"><label>' + T('feed.visibility') + '</label><select class="select" id="rp-vis">' + visibilityOptions('public') + '</select></div>' +
      '<div style="display:flex;gap:6px;justify-content:flex-end">' +
      '<button type="button" class="btn btn-ghost" id="rp-cancel">' + T('common.cancel') + '</button>' +
      '<button type="button" class="btn btn-primary" id="rp-go">' + UI.icon('retweet') + ' ' + T('feed.repost') + '</button>' +
      '</div>';

    UI.modal({ title: T('feed.repost'), icon: 'retweet', body: body, actions: [] });
    var boxes = document.querySelectorAll('.modal');
    var box = boxes[boxes.length - 1];
    if (!box) return;

    function close() {
      var scrim = box.parentNode;
      if (scrim && scrim.parentNode) scrim.parentNode.removeChild(scrim);
    }

    box.querySelector('#rp-cancel').addEventListener('click', close);
    box.querySelector('#rp-go').addEventListener('click', function () {
      var btn = this;
      btn.disabled = true;
      Api.request('POST', '/api/post/repost', {
        id: Number(postId),
        body: box.querySelector('#rp-body').value,
        visibility: box.querySelector('#rp-vis').value
      }, function (err, data) {
        btn.disabled = false;
        if (err) { UI.toast(Api.errorText(err), 'err'); return; }
        close();
        UI.toast(T('feed.reposted'), 'ok');
        SoundFX.play('post');
        if (state.route === 'feed') route();
      });
    });
  }

  // ------------------------------------------------------- post reactions
  var POST_REACTIONS = ['👍', '😂', '😮', '😢', '🔥', '🎉'];

  function postReactionsHtml(p) {
    var list = p.reactions || [];
    if (!list.length) return '';
    var html = '<div class="post-reactions">';
    for (var i = 0; i < list.length; i++) {
      var r = list[i];
      html += '<button type="button" class="reaction-chip' + (r.mine ? ' mine' : '') +
        '" data-post-react="' + p.id + '" data-emoji="' + UI.attr(r.emoji) + '" title="' + UI.attr(T('feed.reactCount', r.count)) + '">' +
        '<span class="re-emoji">' + UI.esc(r.emoji) + '</span><b>' + r.count + '</b></button>';
    }
    return html + '</div>';
  }

  var postReactOutside = null;

  function closePostReactPicker() {
    var pop = document.getElementById('post-react-pop');
    if (pop && pop.parentNode) pop.parentNode.removeChild(pop);
    if (postReactOutside) {
      document.removeEventListener('mousedown', postReactOutside, true);
      postReactOutside = null;
    }
  }

  function openPostReactPicker(anchor, postId) {
    closePostReactPicker();
    var pop = document.createElement('div');
    pop.className = 'react-pop';
    pop.id = 'post-react-pop';
    var html = '';
    for (var i = 0; i < POST_REACTIONS.length; i++) {
      html += '<button type="button" class="react-item" data-emoji="' + UI.attr(POST_REACTIONS[i]) + '">' +
        UI.esc(POST_REACTIONS[i]) + '</button>';
    }
    pop.innerHTML = html;
    document.body.appendChild(pop);

    var rect = anchor.getBoundingClientRect();
    var width = pop.offsetWidth || 240;
    var left = Math.max(8, Math.min(window.innerWidth - width - 8, rect.left - width / 2 + 20));
    var top = rect.top - (pop.offsetHeight || 40) - 6;
    if (top < 8) top = rect.bottom + 6;
    pop.style.left = Math.round(left) + 'px';
    pop.style.top = Math.round(top) + 'px';

    var items = pop.querySelectorAll('.react-item');
    for (var k = 0; k < items.length; k++) {
      items[k].addEventListener('click', function () {
        var emoji = this.getAttribute('data-emoji');
        closePostReactPicker();
        sendPostReaction(postId, emoji);
      });
    }

    postReactOutside = function (e) {
      if (pop.parentNode && !pop.contains(e.target)) closePostReactPicker();
    };
    setTimeout(function () {
      if (postReactOutside) document.addEventListener('mousedown', postReactOutside, true);
    }, 0);
  }

  function sendPostReaction(postId, emoji) {
    Api.request('POST', '/api/reaction', { id: Number(postId), emoji: emoji }, function (err, data) {
      if (err) { UI.toast(Api.errorText(err), 'err'); return; }
      var article = document.querySelector('[data-post="' + postId + '"]');
      if (!article || !data.post) return;
      var old = article.querySelector('.post-reactions');
      var fresh = document.createElement('div');
      fresh.innerHTML = postReactionsHtml(data.post);
      var html = fresh.innerHTML;
      if (old) {
        if (html) old.outerHTML = html;
        else if (old.parentNode) old.parentNode.removeChild(old);
      } else if (html) {
        var actions = article.querySelector('.post-actions');
        if (actions && actions.parentNode) actions.insertAdjacentHTML('beforebegin', html);
      }
      bindPosts(article);
      SoundFX.play('click');
    });
  }

  // ---------------------------------------------------------- post video

  function pollHtml(poll) {
    if (!poll || !poll.options || !poll.options.length) return '';
    var voted = typeof poll.myVote === 'number' && poll.myVote >= 0;
    var html = '<div class="poll" data-post-poll="1">' +
      '<div class="poll-q">' + UI.icon('square-poll-vertical') + ' ' + UI.esc(poll.question || '') + '</div>';
    for (var i = 0; i < poll.options.length; i++) {
      var o = poll.options[i];
      html += '<button type="button" class="poll-opt' + (o.mine ? ' mine' : '') + (voted ? ' voted' : '') +
        '" data-option="' + i + '">' +
        '<span class="poll-bar" style="width:' + (voted ? (o.percent || 0) : 0) + '%"></span>' +
        '<span class="poll-text">' + UI.esc(o.text) + '</span>' +
        (voted ? '<span class="poll-pct">' + (o.percent || 0) + '%</span>' : '') +
        '</button>';
    }
    html += '<div class="poll-foot">' + (voted ? UI.icon('check') + ' ' + T('feed.pollVoted') + ' · ' : '') +
      T('feed.pollVotes', poll.total || 0) + (voted ? ' · <span class="poll-hint">' + T('feed.pollChange') + '</span>' : '') +
      '</div></div>';
    return html;
  }

  function bindPolls(scope) {
    var opts = (scope || document).querySelectorAll('.poll-opt');
    for (var i = 0; i < opts.length; i++) {
      if (opts[i].getAttribute('data-bound')) continue;
      opts[i].setAttribute('data-bound', '1');
      opts[i].addEventListener('click', function () {
        var pollBox = this;
        while (pollBox && !pollBox.getAttribute('data-post-poll')) pollBox = pollBox.parentNode;
        var article = pollBox;
        while (article && !article.getAttribute('data-post')) article = article.parentNode;
        if (!article) return;
        votePoll(article.getAttribute('data-post'), Number(this.getAttribute('data-option')), pollBox);
      });
    }
  }

  function votePoll(postId, option, box) {
    var buttons = box.querySelectorAll('.poll-opt');
    for (var i = 0; i < buttons.length; i++) buttons[i].disabled = true;
    Api.request('POST', '/api/poll', { post: Number(postId), option: option }, function (err, data) {
      for (var k = 0; k < buttons.length; k++) buttons[k].disabled = false;
      if (err) { UI.toast(Api.errorText(err), 'err'); return; }
      var p = data && data.post;
      if (!p || !p.poll) return;
      var fresh = document.createElement('div');
      fresh.innerHTML = pollHtml(p.poll);
      var replacement = fresh.firstChild;
      box.parentNode.replaceChild(replacement, box);
      bindPolls(replacement.parentNode);
      SoundFX.play('click');
    });
  }

  // ------------------------------------------------------------ post tools

  function visibilityOptions(selected) {
    var opts = [['public', 'feed.public'], ['friends', 'feed.friendsOnly'], ['private', 'feed.private']];
    var html = '';
    for (var i = 0; i < opts.length; i++) {
      html += '<option value="' + opts[i][0] + '"' + (selected === opts[i][0] ? ' selected' : '') + '>' +
        T(opts[i][1]) + '</option>';
    }
    return html;
  }

  function likedBy(id) {
    Api.request('GET', '/api/likes?post=' + encodeURIComponent(id), null, function (err, data) {
      if (err) { UI.toast(Api.errorText(err), 'err'); return; }
      var users = data.users || [];
      var body = '';
      if (!users.length) body = UI.empty('heart', T('feed.noLikes'));
      else {
        body = '<div class="like-list">';
        for (var i = 0; i < users.length; i++) {
          var u = users[i];
          body += '<a class="online-user" href="#/u/' + encodeURIComponent(u.username) + '">' +
            UI.avatar(u, 28) + '<span class="name">' + UI.esc(u.displayName || u.username) + '</span></a>';
        }
        body += '</div>';
      }
      body += '<div class="tiny muted" style="margin-top:8px">' + T('feed.likesTotal', data.total || 0) + '</div>';
      UI.modal({
        title: T('feed.likedBy'), icon: 'heart', body: body,
        actions: [{ label: T('common.close'), cls: 'btn-primary' }]
      });
    });
  }

  function togglePin(id, btn) {
    var on = btn.getAttribute('data-on') !== '1';
    btn.disabled = true;
    Api.request('POST', '/api/post/pin', { id: Number(id), on: on }, function (err, data) {
      btn.disabled = false;
      if (err) { UI.toast(Api.errorText(err), 'err'); return; }
      btn.setAttribute('data-on', data.pinned ? '1' : '0');
      btn.className = 'icon-btn post-pin' + (data.pinned ? ' active' : '');
      btn.title = data.pinned ? T('feed.unpin') : T('feed.pin');
      UI.toast(data.pinned ? T('feed.pinnedOk') : T('feed.unpinnedOk'), 'ok');
      SoundFX.play('success');
      // walls and group feeds sort pinned posts first, so reload them
      if (state.route === 'profile' || state.route === 'group') route();
    });
  }

  function editPost(id) {
    Api.request('GET', '/api/post?id=' + encodeURIComponent(id), null, function (err, data) {
      if (err) { UI.toast(Api.errorText(err), 'err'); return; }
      var p = data.post || {};
      var img = UI.safeUrl(p.image);

      var body = '<div class="field"><label>' + T('feed.postText') + '</label>' +
        '<textarea class="textarea" id="ep-body" rows="6" maxlength="' + (state.site.maxPostLength || 8000) + '">' +
        UI.esc(p.body || '') + '</textarea></div>';
      body += '<div class="field"><label>' + T('feed.video') + '</label>' +
        '<input class="input" id="ep-video" value="' + UI.attr(p.video || '') + '" placeholder="https://youtube.com/watch?v=..."></div>';
      body += '<div class="field"><label>' + T('feed.visibility') + '</label>' +
        '<select class="select" id="ep-vis">' + visibilityOptions(p.visibility) + '</select></div>';
      if (img) {
        body += '<div class="field"><label>' + T('feed.photo') + '</label><div class="ep-image">' +
          '<img src="' + UI.attr(img) + '" alt="">' +
          '<label class="check"><input type="checkbox" id="ep-drop-image"> <span>' + T('feed.removePhoto') + '</span></label>' +
          '</div></div>';
      }
      body += '<div class="field"><label>' + T('feed.replacePhoto') + '</label>' +
        '<input type="file" id="ep-file" accept="image/png,image/jpeg,image/gif,image/webp">' +
        '<div class="hint">' + T('settings.avatarHint', state.site.maxPostImageKB || 4096) + '</div></div>';
      body += '<div style="display:flex;gap:6px;justify-content:flex-end">' +
        '<button type="button" class="btn btn-ghost" id="ep-cancel">' + T('common.cancel') + '</button>' +
        '<button type="button" class="btn btn-primary" id="ep-save">' + UI.icon('floppy-disk') + ' ' + T('common.save') + '</button>' +
        '</div>';

      UI.modal({ title: T('feed.editPost'), icon: 'pen', wide: true, body: body, actions: [] });
      var boxes = document.querySelectorAll('.modal');
      var box = boxes[boxes.length - 1];
      if (!box) return;

      var close = function () {
        var scrim = box.parentNode;
        if (scrim && scrim.parentNode) scrim.parentNode.removeChild(scrim);
      };

      box.querySelector('#ep-cancel').addEventListener('click', close);
      box.querySelector('#ep-save').addEventListener('click', function () {
        var btn = this;
        var drop = box.querySelector('#ep-drop-image');
        var file = box.querySelector('#ep-file');
        var f = file && file.files && file.files[0];

        function finish(action, imageData) {
          btn.disabled = true;
          var payload = {
            id: Number(id),
            body: box.querySelector('#ep-body').value,
            video: box.querySelector('#ep-video').value.trim(),
            visibility: box.querySelector('#ep-vis').value,
            imageAction: action
          };
          if (action === 'replace') payload.image = imageData;
          Api.request('POST', '/api/post/edit', payload, function (e2, d2) {
            btn.disabled = false;
            if (e2) { UI.toast(Api.errorText(e2), 'err'); return; }
            close();
            UI.toast(T('feed.editedOk'), 'ok');
            SoundFX.play('success');
            var el = document.querySelector('[data-post="' + id + '"]');
            if (el) {
              var parent = el.parentNode;
              var tmp = document.createElement('div');
              tmp.innerHTML = postHtml(d2.post);
              var fresh = tmp.firstChild;
              parent.insertBefore(fresh, el);
              parent.removeChild(el);
              bindPosts(parent);
              if (fresh && fresh.scrollIntoView) { try { fresh.scrollIntoView({ block: 'nearest' }); } catch (e3) { } }
            } else route();
          });
        }

        if (f) {
          UI.fileToDataUrl(f, (state.site.maxPostImageKB || 4096) * 1024, function (e2, dataUrl) {
            if (e2) {
              UI.toast(e2 === 'too-large' ? T('settings.avatarHint', state.site.maxPostImageKB) : T('errors.generic'), 'err');
              return;
            }
            finish('replace', dataUrl);
          });
        } else if (drop && drop.checked) {
          finish('remove', '');
        } else {
          finish('keep', '');
        }
      });
    });
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
      shares[k].addEventListener('click', function () { openShareMenu(this, this.getAttribute('data-id')); });
    }
    var marks = (scope || document).querySelectorAll('.post-bookmark');
    for (var bm = 0; bm < marks.length; bm++) {
      if (marks[bm].getAttribute('data-bound')) continue;
      marks[bm].setAttribute('data-bound', '1');
      marks[bm].addEventListener('click', function () { toggleBookmark(this.getAttribute('data-id'), this); });
    }
    var counts = (scope || document).querySelectorAll('.likes-count');
    for (var lc = 0; lc < counts.length; lc++) {
      if (counts[lc].getAttribute('data-bound')) continue;
      counts[lc].setAttribute('data-bound', '1');
      counts[lc].addEventListener('click', function (e) {
        e.preventDefault();
        e.stopPropagation();
        likedBy(this.getAttribute('data-id'));
      });
    }
    var pins = (scope || document).querySelectorAll('.post-pin');
    for (var pn = 0; pn < pins.length; pn++) {
      if (pins[pn].getAttribute('data-bound')) continue;
      pins[pn].setAttribute('data-bound', '1');
      pins[pn].addEventListener('click', function () { togglePin(this.getAttribute('data-id'), this); });
    }
    var edits = (scope || document).querySelectorAll('.post-edit');
    for (var ed = 0; ed < edits.length; ed++) {
      if (edits[ed].getAttribute('data-bound')) continue;
      edits[ed].setAttribute('data-bound', '1');
      edits[ed].addEventListener('click', function () { editPost(this.getAttribute('data-id')); });
    }
    var reactBtns = (scope || document).querySelectorAll('.post-react');
    for (var rb = 0; rb < reactBtns.length; rb++) {
      if (reactBtns[rb].getAttribute('data-bound')) continue;
      reactBtns[rb].setAttribute('data-bound', '1');
      reactBtns[rb].addEventListener('click', function () {
        openPostReactPicker(this, this.getAttribute('data-id'));
      });
    }
    var reactChips = (scope || document).querySelectorAll('[data-post-react]');
    for (var rc = 0; rc < reactChips.length; rc++) {
      if (reactChips[rc].getAttribute('data-bound')) continue;
      reactChips[rc].setAttribute('data-bound', '1');
      reactChips[rc].addEventListener('click', function () {
        sendPostReaction(this.getAttribute('data-post-react'), this.getAttribute('data-emoji'));
      });
    }
    bindPolls(scope);
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
      if (window.GiftUI) html += GiftUI.cardHtml(u, data);
      html += badgesCard(u.badges, isSelf);
      var photoList = data.photos || [];
      if (photoList.length) {
        html += '<div class="card"><div class="card-h">' + UI.icon('images') + ' ' + T('profile.photos') +
          '<span class="right"><a href="#/photos/' + u.id + '">' + T('common.all') + '</a></span></div>' +
          '<div class="card-b"><div class="photo-grid">';
        for (var ph = 0; ph < photoList.length; ph++) {
          var psrc = UI.safeUrl(photoList[ph].image);
          if (!psrc) continue;
          html += '<button type="button" class="photo-cell" data-full="' + UI.attr(psrc) + '">' +
            '<img src="' + UI.attr(psrc) + '" alt="" loading="lazy"></button>';
        }
        html += '</div></div></div>';
      }
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
      if (window.GiftUI) GiftUI.bind(main);
      var cells = main.querySelectorAll('.photo-cell');
      for (var ci = 0; ci < cells.length; ci++) {
        cells[ci].addEventListener('click', function () { UI.imageModal(this.getAttribute('data-full')); });
      }
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
      stopTimers();
      stopNotifyStream();
      stopBadgePoll();
      if (window.CallUI) CallUI.stop();
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
    desktopNotify: desktopNotify,
    applyUnread: applyUnread,
    setUnreadLocal: setUnreadLocal,
    setBadge: setBadge,
    stopTimers: stopTimers
  };

  views.feed = feedView;
  views.post = postView;
  views.profile = profileView;
  views.notfound = notFoundView;

  return api;
})();
