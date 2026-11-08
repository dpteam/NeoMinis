/* =========================================================================
   Social SPA - remaining views: friends, people, messages, groups,
   notifications, settings, admin, search. Loaded after app.js.
   ========================================================================= */
(function () {
  'use strict';

  var T = App.T;
  var state = App.state;
  var views = App.views;

  function card(title, iconName, body, right) {
    return '<div class="card"><div class="card-h">' + UI.icon(iconName) + ' ' + UI.esc(title) +
      (right ? '<span class="right">' + right + '</span>' : '') + '</div>' +
      '<div class="card-b">' + body + '</div></div>';
  }

  function spinnerCard() {
    return '<div class="card"><div class="card-b center" style="padding:26px">' + UI.spinner() + '</div></div>';
  }

  function actionCard(u) {
    var isSelf = state.user && Number(u.id) === Number(state.user.id);
    if (isSelf) {
      return '<div class="user-card"><a href="#/profile/' + u.id + '">' + UI.avatar(u, 52) + '</a>' +
        '<a class="name" href="#/profile/' + u.id + '">' + UI.esc(u.displayName) + '</a>' +
        '<div class="sub">' + T('common.you') + '</div>' +
        '<a class="btn btn-sm" href="#/profile/' + u.id + '">' + T('nav.profile') + '</a></div>';
    }
    var a = '';
    if (u.isFriend) {
      a = '<a class="btn btn-sm" href="#/im/' + u.id + '">' + T('profile.message') + '</a>' +
          '<button class="btn btn-sm friend-action" data-user="' + u.id + '" data-action="remove">' + T('profile.removeFriend') + '</button>';
    } else if (u.requestPending) {
      a = '<button class="btn btn-sm friend-action" data-user="' + u.id + '" data-action="remove">' + T('profile.cancelRequest') + '</button>';
    } else if (u.incomingRequest) {
      a = '<button class="btn btn-sm btn-ok friend-action" data-user="' + u.id + '" data-action="accept">' + T('profile.accept') + '</button>' +
          '<button class="btn btn-sm friend-action" data-user="' + u.id + '" data-action="decline">' + T('profile.decline') + '</button>';
    } else {
      a = '<button class="btn btn-sm friend-action" data-user="' + u.id + '" data-action="request">' + T('profile.addFriend') + '</button>';
    }
    return '<div class="user-card"><a href="#/u/' + encodeURIComponent(u.username) + '">' + UI.avatar(u, 52) + '</a>' +
      '<a class="name" href="#/u/' + encodeURIComponent(u.username) + '">' + UI.esc(u.displayName) + '</a>' +
      '<div class="sub">' + (u.online ? T('profile.online') : '@' + UI.esc(u.username)) + '</div>' +
      '<div style="display:flex;gap:4px;flex-wrap:wrap;justify-content:center">' + a + '</div></div>';
  }

  // --------------------------------------------------------------- friends

  views.friends = function (main, r) {
    var userId = App.qs('user');
    main.innerHTML = spinnerCard();
    Api.request('GET', '/api/friends' + (userId ? '?user=' + encodeURIComponent(userId) : ''), null, function (err, data) {
      if (err) { main.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>'; return; }
      var self = data.user && data.user.id === (state.user && state.user.id);
      var friends = data.friends || [];
      var incoming = data.incoming || [];
      var outgoing = data.outgoing || [];
      var tab = App.qs('tab') || 'all';
      var html = '<div class="card"><div class="card-h">' + UI.icon('user-group') + ' ' + T('friends.title') +
        ' <span class="right">' + (data.total || 0) + '</span></div><div class="card-b">';
      if (self) {
        html += '<div class="feed-tabs" style="margin-bottom:10px">' +
          '<a class="tab2' + (tab === 'all' ? ' active' : '') + '" href="#/friends?tab=all">' + T('friends.all') + ' (' + friends.length + ')</a>' +
          '<a class="tab2' + (tab === 'incoming' ? ' active' : '') + '" href="#/friends?tab=incoming">' + T('friends.incoming') + ' (' + incoming.length + ')</a>' +
          '<a class="tab2' + (tab === 'outgoing' ? ' active' : '') + '" href="#/friends?tab=outgoing">' + T('friends.outgoing') + ' (' + outgoing.length + ')</a>' +
          '</div>';
      }
      var list = tab === 'incoming' ? incoming : tab === 'outgoing' ? outgoing : friends;
      if (!list.length) html += UI.empty('user-group', T('friends.empty'), T('friends.emptyHint'));
      else {
        html += '<div class="user-grid">';
        for (var i = 0; i < list.length; i++) html += actionCard(list[i]);
        html += '</div>';
      }
      html += '</div></div>';
      main.innerHTML = html;
      App.bindFriendActions(main);
    });
  };

  // ---------------------------------------------------------------- people

  views.people = function (main, r) {
    var q = App.qs('q') || '';
    state.people = { q: q, offset: 0, users: [], total: 0, loading: false };
    main.innerHTML = '<div class="card"><div class="card-h">' + UI.icon('users') + ' ' + T('people.title') + '</div><div class="card-b">' +
      '<form id="people-search" style="display:flex;gap:7px;margin-bottom:10px">' +
      '<input class="input" id="people-q" type="search" placeholder="' + UI.attr(T('people.search')) + '" value="' + UI.attr(q) + '">' +
      '<button class="btn btn-primary" type="submit">' + T('common.search') + '</button></form>' +
      '<div id="people-list"></div><div id="people-more"></div></div></div>';
    var form = document.getElementById('people-search');
    form.addEventListener('submit', function (e) {
      e.preventDefault();
      var v = document.getElementById('people-q').value.trim();
      App.go('#/people' + (v ? '?q=' + encodeURIComponent(v) : ''));
    });
    loadPeople(false);
  };

  function loadPeople(more) {
    if (state.people.loading) return;
    state.people.loading = true;
    var box = document.getElementById('people-list');
    var moreBox = document.getElementById('people-more');
    if (moreBox) moreBox.innerHTML = '<div class="center" style="padding:8px">' + UI.spinner() + '</div>';
    var url = '/api/people?limit=24&offset=' + (more ? state.people.offset : 0) + (state.people.q ? '&q=' + encodeURIComponent(state.people.q) : '');
    Api.request('GET', url, null, function (err, data) {
      state.people.loading = false;
      if (err) { if (moreBox) moreBox.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>'; return; }
      var users = data.users || [];
      state.people.total = data.total || 0;
      state.people.offset = (more ? state.people.offset : 0) + users.length;
      state.people.users = more ? state.people.users.concat(users) : users;
      if (!more) {
        if (!users.length) box.innerHTML = UI.empty('users', T('people.empty'));
        else {
          var h = '<div class="user-grid">';
          for (var i = 0; i < users.length; i++) h += actionCard(users[i]);
          h += '</div>';
          box.innerHTML = h;
        }
      } else {
        for (var j = 0; j < users.length; j++) box.querySelector('.user-grid').insertAdjacentHTML('beforeend', actionCard(users[j]));
      }
      App.bindFriendActions(box);
      if (moreBox) {
        moreBox.innerHTML = data.hasMore ? '<button class="load-more" id="people-load">' + T('people.loadMore') + '</button>' : '';
        var b = document.getElementById('people-load');
        if (b) b.addEventListener('click', function () { loadPeople(true); });
      }
    });
  }

  // -------------------------------------------------------------- messages

  views.im = function (main, r) {
    var withId = Number(r.parts[1] || 0);
    if (state.im.timer) { clearInterval(state.im.timer); state.im.timer = null; }
    state.im.withId = withId;
    main.innerHTML = '<div class="card"><div class="card-b center" style="padding:26px">' + UI.spinner() + '</div></div>';
    Api.request('GET', '/api/messages/threads', null, function (err, data) {
      if (err) { main.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>'; return; }
      var threads = data.threads || [];
      var html = '<div class="card"><div class="card-h">' + UI.icon('envelope') + ' ' + T('messages.title') +
        '<span class="right"><a href="#/people">' + T('profile.message') + '</a></span></div>';
      html += '<div class="msg-layout"><div class="threads" id="threads">';
      if (!threads.length) html += '<div class="empty" style="padding:24px 10px">' + UI.icon('inbox') + '<b>' + T('messages.noThreads') + '</b></div>';
      for (var i = 0; i < threads.length; i++) html += threadHtml(threads[i], withId);
      html += '</div><div class="chat" id="chat">';
      if (!withId) html += '<div class="chat-empty">' + UI.icon('comments') + ' ' + T('messages.select') + '</div>';
      else html += '<div class="chat-empty">' + UI.spinner() + '</div>';
      html += '</div></div></div>';
      main.innerHTML = html;
      var ts = main.querySelectorAll('.thread');
      for (var j = 0; j < ts.length; j++) {
        ts[j].addEventListener('click', function () { App.go('#/im/' + this.getAttribute('data-user')); });
      }
      if (withId) loadConversation(withId, false);
      state.im.timer = setInterval(function () {
        if (App.state.route !== 'im' || !state.im.withId) return;
        if (document.hidden) return;
        loadConversation(state.im.withId, true);
      }, 5000);
    });
  };

  function threadHtml(t, activeId) {
    var o = t.other || {};
    var last = t.last || {};
    return '<div class="thread' + (Number(activeId) === Number(o.id) ? ' active' : '') + '" data-user="' + o.id + '">' +
      UI.avatar(o, 34) + '<div class="thread-main"><div class="name">' + UI.esc(o.displayName) + '</div>' +
      '<div class="last">' + UI.esc((last.body || '').substring(0, 60)) + '</div></div>' +
      (t.unread ? '<span class="unread">' + t.unread + '</span>' : '') + '</div>';
  }

  function loadConversation(withId, silent) {
    Api.request('GET', '/api/messages?with=' + withId + '&limit=60', null, function (err, data) {
      if (err) { if (!silent) UI.toast(Api.errorText(err), 'err'); return; }
      var chat = document.getElementById('chat');
      if (!chat) return;
      var other = data.other || {};
      var messages = data.messages || [];
      var lastId = messages.length ? messages[messages.length - 1].id : 0;
      if (silent && chat.getAttribute('data-last') === String(lastId) && document.getElementById('chat-form')) return;
      var oldText = document.getElementById('chat-text');
      var typed = oldText ? oldText.value : '';
      var hadFocus = oldText && document.activeElement === oldText;
      var body = '<div class="chat-head">' + UI.avatar(other, 32) +
        '<div><div class="name">' + UI.esc(other.displayName) + '</div><div class="tiny muted">@' + UI.esc(other.username) + '</div></div>' +
        '<a class="btn btn-sm right" href="#/profile/' + other.id + '">' + T('nav.profile') + '</a></div>';
      body += '<div class="chat-body" id="chat-body">';
      if (!messages.length) body += '<div class="chat-empty">' + T('messages.empty') + '</div>';
      for (var i = 0; i < messages.length; i++) body += bubbleHtml(messages[i], other);
      body += '</div>';
      body += '<form class="chat-form" id="chat-form">' +
        '<textarea id="chat-text" maxlength="' + (state.site.maxMessageLength || 4000) + '" placeholder="' + UI.attr(T('messages.placeholder')) + '"></textarea>' +
        '<button class="btn btn-primary" type="submit">' + UI.icon('paper-plane') + '</button></form>';
      var oldBody = document.getElementById('chat-body');
      var atBottom = true;
      if (oldBody) atBottom = (oldBody.scrollHeight - oldBody.scrollTop - oldBody.clientHeight) < 60;
      chat.innerHTML = body;
      chat.setAttribute('data-last', lastId);
      var newBody = document.getElementById('chat-body');
      if (newBody && (atBottom || !silent)) newBody.scrollTop = newBody.scrollHeight;
      var newText = document.getElementById('chat-text');
      if (newText && typed) newText.value = typed;
      if (newText && hadFocus) { try { newText.focus(); } catch (e) { } }
      var form = document.getElementById('chat-form');
      form.addEventListener('submit', function (e) {
        e.preventDefault();
        var ta = document.getElementById('chat-text');
        var text = ta.value.trim();
        if (!text) return;
        ta.disabled = true;
        Api.request('POST', '/api/messages/send', { to: withId, body: text }, function (err2, data2) {
          ta.disabled = false;
          if (err2) { UI.toast(Api.errorText(err2), 'err'); return; }
          ta.value = '';
          SoundFX.play('message');
          loadConversation(withId, true);
          refreshThreads();
        });
      });
      if (!silent) refreshThreads();
    });
  }

  function bubbleHtml(m, other) {
    var mine = m.mine;
    var u = mine ? state.user : other;
    return '<div class="bubble-row' + (mine ? ' mine' : '') + '">' + UI.avatar(u, 26) +
      '<div class="bubble">' + UI.linkify(m.body || '').replace(/\n/g, '<br>') +
      '<span class="time">' + UI.timeAgo(m.created) + '</span></div></div>';
  }

  function refreshThreads() {
    Api.request('GET', '/api/messages/threads', null, function (err, data) {
      if (err) return;
      var box = document.getElementById('threads');
      if (!box) return;
      var threads = data.threads || [];
      var h = '';
      for (var i = 0; i < threads.length; i++) h += threadHtml(threads[i], state.im.withId);
      box.innerHTML = h || '<div class="empty" style="padding:24px 10px">' + UI.icon('inbox') + '<b>' + T('messages.noThreads') + '</b></div>';
      var ts = box.querySelectorAll('.thread');
      for (var j = 0; j < ts.length; j++) {
        ts[j].addEventListener('click', function () { App.go('#/im/' + this.getAttribute('data-user')); });
      }
    });
  }

  // ---------------------------------------------------------------- groups

  views.groups = function (main, r) {
    main.innerHTML = spinnerCard();
    Api.request('GET', '/api/groups', null, function (err, data) {
      if (err) { main.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>'; return; }
      var groups = data.groups || [];
      var html = '<div class="card"><div class="card-h">' + UI.icon('people-group') + ' ' + T('groups.title') +
        '<span class="right"><button class="btn btn-xs btn-primary" id="group-new">' + UI.icon('plus') + ' ' + T('groups.create') + '</button></span></div><div class="card-b">';
      if (!groups.length) html += UI.empty('people-group', T('groups.empty'));
      else {
        html += '<div class="user-grid">';
        for (var i = 0; i < groups.length; i++) {
          var g = groups[i];
          html += '<div class="user-card"><a href="#/group/' + g.id + '">' + UI.avatar({ id: g.id, displayName: g.name, avatar: g.avatar }, 52) + '</a>' +
            '<a class="name" href="#/group/' + g.id + '">' + UI.esc(g.name) + '</a>' +
            '<div class="sub">' + T('groups.members') + ': ' + g.members + ' · ' + (g.visibility === 'private' ? T('groups.private') : T('groups.public')) + '</div></div>';
        }
        html += '</div>';
      }
      html += '</div></div>';
      main.innerHTML = html;
      var nb = document.getElementById('group-new');
      if (nb) nb.addEventListener('click', newGroupModal);
    });
  };

  function newGroupModal() {
    UI.modal({
      title: T('groups.create'),
      icon: 'people-group',
      body: '<div class="field"><label>' + T('groups.name') + '</label><input class="input" id="g-name" maxlength="80"></div>' +
        '<div class="field"><label>' + T('groups.description') + '</label><textarea class="textarea" id="g-desc" maxlength="2000"></textarea></div>' +
        '<div class="field"><label>' + T('groups.visibility') + '</label><select class="select" id="g-vis"><option value="public">' + T('groups.public') + '</option><option value="private">' + T('groups.private') + '</option></select></div>',
      actions: [
        { label: T('common.cancel'), cls: 'btn-ghost' },
        {
          label: T('common.create'), cls: 'btn-primary', onClick: function (box) {
            var name = box.querySelector('#g-name').value.trim();
            if (name.length < 2) { UI.toast(T('groups.name'), 'err'); return false; }
            Api.request('POST', '/api/group/create', {
              name: name,
              description: box.querySelector('#g-desc').value,
              visibility: box.querySelector('#g-vis').value
            }, function (err, data) {
              if (err) { UI.toast(Api.errorText(err), 'err'); return; }
              SoundFX.play('success');
              App.go('#/group/' + data.group.id);
            });
          }
        }
      ]
    });
  }

  views.group = function (main, r) {
    var id = r.parts[1];
    main.innerHTML = spinnerCard();
    Api.request('GET', '/api/group?id=' + encodeURIComponent(id), null, function (err, data) {
      if (err) { main.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>'; return; }
      var g = data.group;
      var html = '<div class="card"><div class="group-head">' +
        UI.avatar({ id: g.id, displayName: g.name, avatar: g.avatar }, 72, false) +
        '<div style="flex:1;min-width:0"><h1>' + UI.esc(g.name) + '</h1><div class="desc">' + UI.esc(g.description || '') + '</div>' +
        '<div class="tiny" style="opacity:.8;margin-top:4px">' + T('groups.members') + ': ' + g.members + ' · ' + (g.visibility === 'private' ? T('groups.private') : T('groups.public')) + '</div></div>' +
        '<button class="btn" id="group-toggle">' + (g.isMember ? (g.isOwner ? UI.icon('crown') + ' ' + T('groups.owner') : UI.icon('right-from-bracket') + ' ' + T('groups.leave')) : UI.icon('plus') + ' ' + T('groups.join')) + '</button>' +
        '</div><div class="card-b">';
      html += '<div class="feed-tabs"><a class="tab2 active" href="#/group/' + g.id + '">' + T('groups.posts') + '</a></div>';
      if (g.isMember) html += App.composerHtml(0);
      html += '<div id="group-posts"></div><div id="group-more"></div>';
      html += '</div></div>';
      html += '<div class="card"><div class="card-h">' + UI.icon('users') + ' ' + T('groups.members') + '</div><div class="card-b"><div class="user-grid">';
      var members = data.members || [];
      for (var i = 0; i < members.length; i++) html += App.userCard(members[i]);
      html += '</div></div></div>';
      main.innerHTML = html;
      if (g.isMember) {
        // group posts go through /api/post with group id
        bindGroupComposer(g.id);
      }
      var tg = document.getElementById('group-toggle');
      if (tg) tg.addEventListener('click', function () {
        var action = g.isMember ? 'leave' : 'join';
        Api.request('POST', '/api/group/' + action, { id: g.id }, function (err2, data2) {
          if (err2) { UI.toast(Api.errorText(err2), 'err'); return; }
          UI.toast(action === 'join' ? T('groups.joined') : T('groups.left'), 'ok');
          SoundFX.play('success');
          App.route();
        });
      });
      renderGroupPosts(g, data.posts || []);
      App.bindFriendActions(main);
    });
  };

  function bindGroupComposer(groupId) {
    var form = document.getElementById('composer');
    if (!form) return;
    // replace the wall id with a group marker handled by the submit override
    form.setAttribute('data-group', groupId);
    form.addEventListener('submit', function (e) {
      e.preventDefault();
      var text = document.getElementById('composer-text');
      var body = text ? text.value.trim() : '';
      if (!body) return;
      var btn = document.getElementById('composer-submit');
      App.setBusy(btn, true, T('feed.posting'));
      Api.request('POST', '/api/post', { body: body, group: Number(groupId), visibility: 'public' }, function (err, data) {
        App.setBusy(btn, false, T('feed.post'));
        if (err) { UI.toast(Api.errorText(err), 'err'); return; }
        if (text) text.value = '';
        SoundFX.play('post');
        App.route();
      });
    }, true);
  }

  function renderGroupPosts(g, posts) {
    var box = document.getElementById('group-posts');
    if (!box) return;
    if (!posts.length) { box.innerHTML = UI.empty('inbox', T('feed.empty'), T('feed.emptyHint')); return; }
    var h = '';
    for (var i = 0; i < posts.length; i++) h += App.postHtml(posts[i]);
    box.innerHTML = h;
    App.bindPosts(box);
    var more = document.getElementById('group-more');
    var next = posts[posts.length - 1].id;
    if (more && posts.length >= 10) {
      more.innerHTML = '<button class="load-more" id="group-load">' + T('feed.loadMore') + '</button>';
      var b = document.getElementById('group-load');
      b.addEventListener('click', function () {
        b.disabled = true;
        Api.request('GET', '/api/group/feed?id=' + g.id + '&before=' + next + '&limit=10', null, function (err, data) {
          b.disabled = false;
          if (err) { UI.toast(Api.errorText(err), 'err'); return; }
          var list = data.posts || [];
          for (var j = 0; j < list.length; j++) box.insertAdjacentHTML('beforeend', App.postHtml(list[j]));
          App.bindPosts(box);
          if (!list.length || !data.hasMore) b.parentNode.removeChild(b);
          else next = data.next;
        });
      });
    }
  }

  // --------------------------------------------------------- notifications

  views.notifications = function (main, r) {
    main.innerHTML = spinnerCard();
    Api.request('GET', '/api/notifications?limit=50', null, function (err, data) {
      if (err) { main.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>'; return; }
      var list = data.notifications || [];
      var html = '<div class="card"><div class="card-h">' + UI.icon('bell') + ' ' + T('notifications.title') +
        '<span class="right"><button class="btn btn-xs" id="notif-read">' + T('notifications.markAll') + '</button></span></div><div class="card-b flush">';
      if (!list.length) html += UI.empty('bell', T('notifications.empty'));
      else {
        for (var i = 0; i < list.length; i++) {
          var n = list[i];
          var a = n.actor || {};
          var ic = n.type === 'like' ? 'heart' : n.type === 'comment' ? 'comment' : n.type === 'friend' ? 'user-plus' : n.type === 'message' ? 'envelope' : n.type === 'admin' ? 'bullhorn' : 'bell';
          html += '<a class="notif' + (n.read ? '' : ' unread') + '" href="' + UI.attr(n.link || '#/notifications') + '">' +
            UI.avatar(a, 32) + '<div class="notif-main"><span class="who">' + UI.esc(a.displayName || a.username || '?') + '</span> ' + UI.esc(n.text) +
            '<div class="when">' + UI.timeAgo(n.created) + '</div></div><span class="type">' + UI.icon(ic) + '</span></a>';
        }
      }
      html += '</div></div>';
      main.innerHTML = html;
      var b = document.getElementById('notif-read');
      if (b) b.addEventListener('click', function () {
        Api.request('POST', '/api/notifications/read', { all: true }, function (err2) {
          if (err2) { UI.toast(Api.errorText(err2), 'err'); return; }
          UI.toast(T('notifications.marked'), 'ok');
          App.route();
        });
      });
    });
  };

  // -------------------------------------------------------------- settings

  views.settings = function (main, r) {
    main.innerHTML = spinnerCard();
    Api.request('GET', '/api/settings', null, function (err, data) {
      if (err) { main.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>'; return; }
      var u = data.user;
      var themes = (state.site && state.site.themes) || ['light', 'dark', 'oled'];
      var langs = (state.site && state.site.languages) || ['ru', 'en', 'uk', 'lt'];
      var themeHtml = '';
      for (var i = 0; i < themes.length; i++) {
        themeHtml += '<button type="button" class="theme-dot ' + themes[i] + (UI.getTheme() === themes[i] ? ' active' : '') + '" data-theme-pick="' + themes[i] + '"><span class="sw"></span>' + T('theme.' + themes[i]) + '</button>';
      }
      var langHtml = '';
      for (var j = 0; j < langs.length; j++) {
        langHtml += '<option value="' + langs[j] + '"' + (I18N.getLang() === langs[j] ? ' selected' : '') + '>' + T('lang.' + langs[j]) + '</option>';
      }
      var ev = SoundFX.getEvents();
      var events = ['click', 'post', 'like', 'comment', 'message', 'notification', 'friend', 'login', 'error', 'success', 'theme'];
      var evHtml = '';
      for (var k = 0; k < events.length; k++) {
        evHtml += '<label class="check"><input type="checkbox" data-sound-event="' + events[k] + '"' + (ev[events[k]] !== false ? ' checked' : '') + '> <span>' + T('sound.' + events[k]) + '</span></label>';
      }

      var html = '<div class="card"><div class="card-h">' + UI.icon('gear') + ' ' + T('settings.title') + '</div><div class="card-b">';
      html += '<div class="settings-grid">';

      html += '<div class="settings-block"><h3>' + T('settings.profile') + '</h3>' +
        '<div class="field"><label>' + T('settings.avatar') + '</label><div style="display:flex;gap:10px;align-items:center">' +
        UI.avatar(u, 64, false) + '<div><button type="button" class="btn btn-sm" id="avatar-btn">' + T('settings.uploadAvatar') + '</button>' +
        '<input type="file" id="avatar-file" class="hidden" accept="image/png,image/jpeg,image/gif,image/webp">' +
        '<div class="hint">' + T('settings.avatarHint', state.site.maxAvatarKB || 512) + '</div></div></div></div>' +
        '<div class="field"><label>' + T('auth.displayName') + '</label><input class="input" id="s-name" maxlength="40" value="' + UI.attr(u.displayName) + '"></div>' +
        '<div class="field"><label>' + T('settings.status') + '</label><input class="input" id="s-status" maxlength="' + (state.site.maxStatusLength || 140) + '" value="' + UI.attr(u.status || '') + '"></div>' +
        '<div class="field"><label>' + T('settings.bio') + '</label><textarea class="textarea" id="s-bio" maxlength="' + (state.site.maxBioLength || 2000) + '">' + UI.esc(u.bio || '') + '</textarea></div>' +
        '<div class="inline-fields"><div class="field"><label>' + T('settings.city') + '</label><input class="input" id="s-city" value="' + UI.attr(u.city || '') + '"></div>' +
        '<div class="field"><label>' + T('settings.country') + '</label><input class="input" id="s-country" value="' + UI.attr(u.country || '') + '"></div></div>' +
        '<div class="inline-fields"><div class="field"><label>' + T('settings.birthday') + '</label><input class="input" id="s-birthday" type="date" value="' + UI.attr(u.birthday || '') + '"></div>' +
        '<div class="field"><label>' + T('settings.gender') + '</label><select class="select" id="s-gender">' +
        '<option value=""' + (!u.gender ? ' selected' : '') + '>' + T('common.none') + '</option>' +
        '<option value="m"' + (u.gender === 'm' ? ' selected' : '') + '>' + T('profile.male') + '</option>' +
        '<option value="f"' + (u.gender === 'f' ? ' selected' : '') + '>' + T('profile.female') + '</option>' +
        '<option value="other"' + (u.gender === 'other' ? ' selected' : '') + '>' + T('profile.other') + '</option></select></div></div>' +
        '<div class="field"><label>' + T('settings.website') + '</label><input class="input" id="s-website" value="' + UI.attr(u.website || '') + '" placeholder="https://"></div>' +
        '<div class="field"><label>' + T('settings.email') + '</label><input class="input" id="s-email" type="email" value="' + UI.attr(u.email || '') + '"></div>' +
        '<button class="btn btn-primary" id="save-profile">' + UI.icon('floppy-disk') + ' ' + T('settings.save') + '</button></div>';

      html += '<div class="settings-block"><h3>' + T('settings.appearance') + '</h3>' +
        '<div class="field"><label>' + T('settings.theme') + '</label><div class="theme-picker">' + themeHtml + '</div></div>' +
        '<div class="field"><label>' + T('settings.language') + '</label><select class="select" id="s-lang">' + langHtml + '</select></div>' +
        '<div class="field"><label>' + T('settings.sound') + '</label><label class="check" style="margin-bottom:6px"><input type="checkbox" id="s-sound"' + (SoundFX.isEnabled() ? ' checked' : '') + '> <span>' + T('settings.soundOn') + '</span></label>' +
        '<input type="range" class="range" id="s-volume" min="0" max="1" step="0.05" value="' + SoundFX.getVolume() + '">' +
        '<div class="hint">' + T('settings.volume') + '</div></div>' +
        '<div class="field"><label>' + T('settings.events') + '</label><div class="sound-events">' + evHtml + '</div></div>' +
        '<div class="field"><label>' + T('settings.privacy') + '</label><select class="select" id="s-privacy">' +
        '<option value="public"' + (u.privacy === 'public' ? ' selected' : '') + '>' + T('feed.public') + '</option>' +
        '<option value="friends"' + (u.privacy === 'friends' ? ' selected' : '') + '>' + T('feed.friendsOnly') + '</option>' +
        '<option value="private"' + (u.privacy === 'private' ? ' selected' : '') + '>' + T('feed.private') + '</option></select></div>' +
        '<label class="check"><input type="checkbox" id="s-hide"' + (u.hideOnline ? ' checked' : '') + '> <span>' + T('settings.hideOnline') + '</span></label></div>';

      html += '<div class="settings-block"><h3>' + T('settings.password') + '</h3>' +
        '<div class="field"><label>' + T('settings.oldPassword') + '</label><input class="input" type="password" id="p-old" autocomplete="current-password"></div>' +
        '<div class="field"><label>' + T('settings.newPassword') + '</label><input class="input" type="password" id="p-new" autocomplete="new-password"></div>' +
        '<button class="btn" id="change-pass">' + UI.icon('key') + ' ' + T('settings.changePassword') + '</button></div>';

      html += '<div class="settings-block"><h3>' + T('settings.account') + '</h3>' +
        '<div class="tiny muted" style="margin-bottom:8px">@' + UI.esc(u.username) + ' · ' + T('profile.joined') + ' ' + UI.formatDate(u.created) + '</div>' +
        '<button class="btn btn-danger" id="logout-2">' + UI.icon('right-from-bracket') + ' ' + T('settings.logout') + '</button></div>';

      html += '</div></div></div>';
      main.innerHTML = html;

      // avatar
      var ab = document.getElementById('avatar-btn');
      var af = document.getElementById('avatar-file');
      if (ab && af) {
        ab.addEventListener('click', function () { af.click(); });
        af.addEventListener('change', function () {
          var f = af.files && af.files[0];
          if (!f) return;
          UI.fileToDataUrl(f, (state.site.maxAvatarKB || 512) * 1024, function (e2, dataUrl) {
            if (e2) { UI.toast(e2 === 'too-large' ? T('settings.avatarHint', state.site.maxAvatarKB) : T('errors.generic'), 'err'); return; }
            Api.request('POST', '/api/avatar', { data: dataUrl }, function (e3, d3) {
              if (e3) { UI.toast(Api.errorText(e3), 'err'); return; }
              state.user.avatar = d3.avatar;
              UI.toast(T('settings.saved'), 'ok');
              SoundFX.play('success');
              App.route();
            });
          });
        });
      }

      // profile save
      var sp = document.getElementById('save-profile');
      if (sp) sp.addEventListener('click', function () {
        sp.disabled = true;
        Api.request('POST', '/api/profile', {
          displayName: App.val('s-name'),
          status: App.val('s-status'),
          bio: App.val('s-bio'),
          city: App.val('s-city'),
          country: App.val('s-country'),
          birthday: App.val('s-birthday'),
          gender: App.val('s-gender'),
          website: App.val('s-website'),
          email: App.val('s-email')
        }, function (e2, d2) {
          sp.disabled = false;
          if (e2) { UI.toast(Api.errorText(e2), 'err'); return; }
          state.user = d2.user;
          UI.toast(T('profile.saved'), 'ok');
          SoundFX.play('success');
          App.renderShell();
          App.route();
        });
      });

      // themes
      var tds = document.querySelectorAll('[data-theme-pick]');
      for (var ti = 0; ti < tds.length; ti++) {
        tds[ti].addEventListener('click', function () {
          var name = this.getAttribute('data-theme-pick');
          UI.applyTheme(name);
          SoundFX.play('theme');
          App.saveSetting({ theme: name });
          for (var z = 0; z < tds.length; z++) tds[z].className = 'theme-dot ' + tds[z].getAttribute('data-theme-pick');
          this.className += ' active';
        });
      }

      // language
      var sl = document.getElementById('s-lang');
      if (sl) sl.addEventListener('change', function () {
        I18N.setLang(sl.value);
        App.saveSetting({ lang: sl.value });
        App.renderShell();
        App.route();
      });

      // sound
      var ss = document.getElementById('s-sound');
      if (ss) ss.addEventListener('change', function () {
        SoundFX.setEnabled(ss.checked);
        App.saveSetting({ sound: ss.checked });
      });
      var sv = document.getElementById('s-volume');
      if (sv) sv.addEventListener('input', function () { SoundFX.setVolume(sv.value); });
      var se = document.querySelectorAll('[data-sound-event]');
      for (var si = 0; si < se.length; si++) {
        se[si].addEventListener('change', function () {
          SoundFX.setEventEnabled(this.getAttribute('data-sound-event'), this.checked);
        });
      }

      // privacy + hide online
      var pr = document.getElementById('s-privacy');
      if (pr) pr.addEventListener('change', function () { App.saveSetting({ privacy: pr.value }); });
      var sh = document.getElementById('s-hide');
      if (sh) sh.addEventListener('change', function () { App.saveSetting({ hideOnline: sh.checked }); });

      // password
      var cp = document.getElementById('change-pass');
      if (cp) cp.addEventListener('click', function () {
        var oldP = App.val('p-old');
        var newP = App.val('p-new');
        if (newP.length < 8) { UI.toast(T('auth.passwordShort'), 'err'); return; }
        cp.disabled = true;
        var iter = (Api.hs && Api.hs.kdfIterations) || 12000;
        var oldV = DptCrypto.deriveVerifier(oldP, state.user.username, iter);
        var newV = DptCrypto.deriveVerifier(newP, state.user.username, iter);
        Api.request('POST', '/api/password', { oldVerifier: oldV, newVerifier: newV }, function (e2) {
          cp.disabled = false;
          if (e2) { UI.toast(Api.errorText(e2), 'err'); return; }
          Api.clearSession();
          state.authed = false;
          UI.toast(T('settings.passwordChanged'), 'ok');
          SoundFX.play('success');
          App.renderAuth('login');
        });
      });

      var lo = document.getElementById('logout-2');
      if (lo) lo.addEventListener('click', App.doLogout);
    });
  };

  // ----------------------------------------------------------------- admin

  views.admin = function (main, r) {
    var site = state.site || {};
    if (!site.adminBoard) {
      var port = site.adminPort || 0;
      var host = location.hostname;
      var link = port ? (location.protocol + '//' + host + ':' + port + '/#/admin') : '';
      main.innerHTML = '<div class="card"><div class="card-h">' + UI.icon('shield-halved') + ' ' + T('admin.title') + '</div>' +
        '<div class="card-b">' + UI.empty('lock', T('admin.title'),
          port ? (T('admin.onOtherPort') + ' ' + link) : T('admin.disabled')) +
        (link ? '<div class="center"><a class="btn btn-primary" href="' + UI.attr(link) + '">' + T('admin.open') + '</a></div>' : '') +
        '</div></div>';
      return;
    }
    var sub = r.parts[1] || 'overview';
    if (state.adminLogs.timer) { clearInterval(state.adminLogs.timer); state.adminLogs.timer = null; }
    var tabs = '<div class="feed-tabs" style="margin-bottom:10px">' +
      adminTab('overview', 'chart-pie', T('admin.overview'), sub) +
      adminTab('users', 'users', T('admin.users'), sub) +
      adminTab('reports', 'flag', T('admin.reports'), sub) +
      adminTab('logs', 'terminal', T('admin.logs'), sub) +
      '</div>';
    main.innerHTML = tabs + '<div id="admin-body">' + spinnerCard() + '</div>';
    var body = document.getElementById('admin-body');
    if (sub === 'users') adminUsers(body);
    else if (sub === 'reports') adminReports(body);
    else if (sub === 'logs') adminLogs(body);
    else adminOverview(body);
  };

  function adminTab(key, iconName, label, active) {
    return '<a class="tab2' + (active === key ? ' active' : '') + '" href="#/admin/' + key + '">' + UI.icon(iconName) + ' ' + UI.esc(label) + '</a>';
  }

  function adminOverview(body) {
    Api.request('GET', '/api/admin/overview', null, function (err, data) {
      if (err) { body.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>'; return; }
      var c = data.counts || {};
      var html = '<div class="admin-grid">' +
        stat('users', c.users, T('admin.users')) +
        stat('online', c.online, T('admin.online')) +
        stat('posts', c.posts, T('admin.posts')) +
        stat('comments', c.comments, T('admin.comments')) +
        stat('messages', c.messages, T('admin.messages')) +
        stat('groups', c.groups, T('admin.groups')) +
        stat('sessions', c.sessions, T('admin.sessions')) +
        stat('reports', c.openReports, T('admin.openReports'), c.openReports ? 'warn' : 'ok') +
        '</div>';
      html += '<div class="card"><div class="card-h">' + UI.icon('screwdriver-wrench') + ' ' + T('admin.tools') +
        '<span class="right"><button class="btn btn-xs" id="adm-backup">' + T('admin.backup') + '</button> <button class="btn btn-xs" id="adm-reload">' + T('admin.reload') + '</button> <button class="btn btn-xs" id="adm-repair">' + T('admin.repair') + '</button> <button class="btn btn-xs btn-primary" id="adm-broadcast">' + T('admin.broadcast') + '</button></span></div>' +
        '<div class="card-b"><div class="info-table" style="font-size:12px">' +
        '<div style="display:flex;justify-content:space-between;padding:4px 0"><span class="muted">' + T('admin.storage') + '</span><b>' + UI.bytes(data.storageBytes) + '</b></div>' +
        '<div style="display:flex;justify-content:space-between;padding:4px 0"><span class="muted">' + T('admin.lastSave') + '</span><b>' + UI.timeAgo(data.lastSave) + '</b></div>' +
        '<div style="display:flex;justify-content:space-between;padding:4px 0"><span class="muted">' + T('admin.uptime') + '</span><b>' + Math.floor((data.uptimeSec || 0) / 60) + 'm</b></div>' +
        '<div style="display:flex;justify-content:space-between;padding:4px 0"><span class="muted">' + T('admin.peak') + '</span><b>' + data.peakOnline + '</b></div>' +
        '<div style="display:flex;justify-content:space-between;padding:4px 0"><span class="muted">' + T('admin.apiRequests') + '</span><b>' + data.apiRequests + ' / ' + data.apiErrors + '</b></div>' +
        (data.storageReadOnly ? '<div class="alert err" style="margin-top:8px">' + T('admin.readOnly') + ': ' + UI.esc(data.saveError || '') + '</div>' : '') +
        '</div></div></div>';
      html += '<div class="card"><div class="card-h">' + UI.icon('user-plus') + ' ' + T('admin.recentUsers') + '</div><div class="card-b flush"><div class="table-wrap"><table class="table"><thead><tr><th>#</th><th>' + T('auth.username') + '</th><th>' + T('admin.role') + '</th><th>' + T('profile.joined') + '</th><th></th></tr></thead><tbody>';
      var ru = data.recentUsers || [];
      for (var i = 0; i < ru.length; i++) {
        var u = ru[i];
        html += '<tr><td>' + u.id + '</td><td><div class="user-cell">' + UI.avatar(u, 22) + '<a href="#/u/' + encodeURIComponent(u.username) + '">' + UI.esc(u.displayName) + '</a></div></td>' +
          '<td>' + UI.esc(u.role) + '</td><td>' + UI.formatDate(u.created) + '</td>' +
          '<td><a class="btn btn-xs" href="#/admin/users">' + T('common.open') + '</a></td></tr>';
      }
      html += '</tbody></table></div></div></div>';
      html += '<div class="card"><div class="card-h">' + UI.icon('flag') + ' ' + T('admin.reports') + '</div><div class="card-b flush">' + reportsHtml(data.reports || []) + '</div></div>';
      html += '<div class="card"><div class="card-h">' + UI.icon('terminal') + ' ' + T('admin.logs') + '</div><div class="card-b">' + logHtml(data.logs || []) + '</div></div>';
      body.innerHTML = html;
      bindAdminTools(body);
      bindReportActions(body);
    });
  }

  function stat(iconName, value, label, cls) {
    return '<div class="admin-stat ' + (cls || '') + '"><b>' + (value === undefined ? 0 : value) + '</b><span>' + UI.esc(label) + '</span></div>';
  }

  function bindAdminTools(scope) {
    var b = scope.querySelector('#adm-backup');
    if (b) b.addEventListener('click', function () {
      Api.request('POST', '/api/admin/backup', {}, function (err, data) {
        if (err) { UI.toast(Api.errorText(err), 'err'); return; }
        UI.toast(T('admin.backupDone') + ': ' + data.path, 'ok');
      });
    });
    var r = scope.querySelector('#adm-reload');
    if (r) r.addEventListener('click', function () {
      Api.request('POST', '/api/admin/reload', {}, function (err, data) {
        if (err) { UI.toast(Api.errorText(err), 'err'); return; }
        UI.toast(T('admin.reloaded'), 'ok');
      });
    });
    var p = scope.querySelector('#adm-repair');
    if (p) p.addEventListener('click', function () {
      Api.request('POST', '/api/admin/repair', {}, function (err) {
        if (err) { UI.toast(Api.errorText(err), 'err'); return; }
        UI.toast(T('admin.repaired'), 'ok');
        App.route();
      });
    });
    var bc = scope.querySelector('#adm-broadcast');
    if (bc) bc.addEventListener('click', function () {
      UI.modal({
        title: T('admin.broadcast'),
        icon: 'bullhorn',
        body: '<div class="field"><label>' + T('admin.broadcastText') + '</label><textarea class="textarea" id="bc-text" maxlength="500"></textarea></div>',
        actions: [
          { label: T('common.cancel'), cls: 'btn-ghost' },
          {
            label: T('admin.send'), cls: 'btn-primary', onClick: function (box) {
              var text = box.querySelector('#bc-text').value.trim();
              if (!text) return false;
              Api.request('POST', '/api/admin/broadcast', { text: text }, function (err, data) {
                if (err) { UI.toast(Api.errorText(err), 'err'); return; }
                UI.toast(T('admin.delivered', data.delivered), 'ok');
                SoundFX.play('success');
              });
            }
          }
        ]
      });
    });
  }

  function adminUsers(body) {
    var q = App.qs('q') || '';
    body.innerHTML = '<div class="card"><div class="card-h">' + UI.icon('users') + ' ' + T('admin.users') + '</div><div class="card-b">' +
      '<form id="adm-user-search" style="display:flex;gap:7px;margin-bottom:10px">' +
      '<input class="input" id="adm-q" type="search" value="' + UI.attr(q) + '" placeholder="' + UI.attr(T('people.search')) + '">' +
      '<button class="btn btn-primary" type="submit">' + T('common.search') + '</button></form>' +
      '<div id="adm-users-list">' + UI.spinner() + '</div></div></div>';
    document.getElementById('adm-user-search').addEventListener('submit', function (e) {
      e.preventDefault();
      var v = document.getElementById('adm-q').value.trim();
      App.go('#/admin/users' + (v ? '?q=' + encodeURIComponent(v) : ''));
    });
    loadAdminUsers(q, 0, false);
  }

  function loadAdminUsers(q, offset, append) {
    var box = document.getElementById('adm-users-list');
    Api.request('GET', '/api/admin/users?limit=40&offset=' + offset + (q ? '&q=' + encodeURIComponent(q) : ''), null, function (err, data) {
      if (err) { box.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>'; return; }
      var users = data.users || [];
      var html = '<div class="table-wrap"><table class="table"><thead><tr><th>#</th><th>' + T('auth.username') + '</th><th>' + T('admin.role') + '</th><th>' + T('profile.posts') + '</th><th>' + T('profile.friends') + '</th><th>' + T('profile.joined') + '</th><th></th></tr></thead><tbody>';
      for (var i = 0; i < users.length; i++) {
        var u = users[i];
        html += '<tr><td>' + u.id + '</td><td><div class="user-cell">' + UI.avatar(u, 22) +
          '<a href="#/u/' + encodeURIComponent(u.username) + '">' + UI.esc(u.displayName) + '</a>' +
          (u.banned ? ' <span class="badge err">' + UI.icon('ban') + '</span>' : '') +
          (u.deleted ? ' <span class="badge">deleted</span>' : '') + '</div></td>' +
          '<td>' + UI.esc(u.role) + '</td><td>' + u.posts + '</td><td>' + u.friends + '</td><td>' + UI.formatDate(u.created) + '</td>' +
          '<td><div class="actions"><button class="btn btn-xs adm-user" data-id="' + u.id + '">' + T('common.edit') + '</button></div></td></tr>';
      }
      html += '</tbody></table></div>';
      if (data.hasMore) html += '<button class="load-more" id="adm-users-more" style="margin-top:8px">' + T('people.loadMore') + '</button>';
      if (append) box.insertAdjacentHTML('beforeend', html);
      else box.innerHTML = html;
      var btns = box.querySelectorAll('.adm-user');
      for (var j = 0; j < btns.length; j++) {
        btns[j].addEventListener('click', function () { adminUserModal(this.getAttribute('data-id')); });
      }
      var more = document.getElementById('adm-users-more');
      if (more) more.addEventListener('click', function () {
        more.parentNode.removeChild(more);
        loadAdminUsers(q, offset + users.length, true);
      });
    });
  }

  function adminUserModal(id) {
    Api.request('GET', '/api/admin/users?limit=1&offset=0', null, function () { });
    // the list endpoint does not have a single-user getter; use the row we know from the table
    var row = null;
    var rows = document.querySelectorAll('.adm-user');
    for (var i = 0; i < rows.length; i++) if (rows[i].getAttribute('data-id') === String(id)) row = rows[i];
    var tr = row ? row.parentNode.parentNode : null;
    var name = tr ? tr.querySelector('.user-cell a').textContent : ('#' + id);
    UI.modal({
      title: T('admin.users') + ': ' + name,
      icon: 'user-shield',
      body: '<div class="field"><label>' + T('admin.banReason') + '</label><input class="input" id="au-reason"></div>' +
        '<div class="field"><label>' + T('admin.days') + '</label><input class="input" id="au-days" type="number" min="0" value="7"></div>' +
        '<div class="field"><label>' + T('admin.role') + '</label><select class="select" id="au-role"><option value="user">user</option><option value="moderator">moderator</option><option value="admin">admin</option></select></div>' +
        '<div style="display:flex;gap:6px;flex-wrap:wrap">' +
        '<button class="btn btn-danger btn-sm" id="au-ban">' + T('admin.ban') + '</button>' +
        '<button class="btn btn-ok btn-sm" id="au-unban">' + T('admin.unban') + '</button>' +
        '<button class="btn btn-sm" id="au-role">' + T('admin.role') + '</button>' +
        '<button class="btn btn-sm" id="au-warn">' + T('admin.warn') + '</button>' +
        '<button class="btn btn-sm" id="au-reset">' + T('admin.reset') + '</button>' +
        '<button class="btn btn-danger btn-sm" id="au-delete">' + T('admin.delete') + '</button>' +
        '</div>',
      actions: [{ label: T('common.close'), cls: 'btn-ghost' }]
    });
    var box = document.querySelector('.modal');
    function send(action, extra) {
      var payload = { id: Number(id), action: action };
      if (extra) for (var k in extra) if (extra.hasOwnProperty(k)) payload[k] = extra[k];
      Api.request('POST', '/api/admin/user', payload, function (err, data) {
        if (err) { UI.toast(Api.errorText(err), 'err'); return; }
        if (data.recoveryCode) {
          UI.modal({
            title: T('admin.recovery'),
            icon: 'key',
            body: '<p class="muted">' + T('admin.recoveryHint') + '</p><div class="input mono" style="text-align:center;font-size:14px;user-select:all">' + UI.esc(data.recoveryCode) + '</div>',
            actions: [{ label: T('common.close'), cls: 'btn-primary' }]
          });
        } else {
          UI.toast(T('admin.saved'), 'ok');
        }
        SoundFX.play('success');
        App.route();
      });
    }
    box.querySelector('#au-ban').addEventListener('click', function () {
      send('ban', { reason: box.querySelector('#au-reason').value, days: Number(box.querySelector('#au-days').value) || 0 });
    });
    box.querySelector('#au-unban').addEventListener('click', function () { send('unban'); });
    box.querySelector('#au-role').addEventListener('click', function () { send('role', { role: box.querySelector('#au-role').value }); });
    box.querySelector('#au-warn').addEventListener('click', function () { send('warn', { reason: box.querySelector('#au-reason').value }); });
    box.querySelector('#au-reset').addEventListener('click', function () { send('reset'); });
    box.querySelector('#au-delete').addEventListener('click', function () {
      UI.confirm(T('admin.delete'), T('admin.confirmDelete'), T('common.delete'), function () { send('delete'); }, true);
    });
  }

  function adminReports(body) {
    body.innerHTML = '<div class="card"><div class="card-h">' + UI.icon('flag') + ' ' + T('admin.reports') + '</div><div class="card-b flush" id="adm-reports">' + UI.spinner() + '</div></div>';
    Api.request('GET', '/api/admin/reports?status=open', null, function (err, data) {
      if (err) { document.getElementById('adm-reports').innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>'; return; }
      document.getElementById('adm-reports').innerHTML = reportsHtml(data.reports || []);
      bindReportActions(document);
    });
  }

  function reportsHtml(reports) {
    if (!reports.length) return UI.empty('flag', T('admin.noReports'));
    var h = '';
    for (var i = 0; i < reports.length; i++) {
      var r = reports[i];
      var target = r.post ? ('<a href="#/post/' + r.post.id + '">' + UI.esc((r.post.body || '').substring(0, 120)) + '</a>') :
        r.user ? ('<a href="#/u/' + encodeURIComponent(r.user.username) + '">' + UI.esc(r.user.displayName) + '</a>') : ('#' + r.target);
      h += '<div class="notif"><span class="type">' + UI.icon('flag') + '</span><div class="notif-main">' +
        '<span class="who">#' + r.id + ' ' + UI.esc(r.type) + '</span> ' + target +
        '<div class="tiny muted" style="margin-top:3px">' + UI.esc(r.reason) + ' · ' + UI.timeAgo(r.created) + ' · ' + UI.esc(r.reporter ? r.reporter.displayName : '') + '</div>' +
        (r.status === 'open' ? '<div style="margin-top:5px"><button class="btn btn-xs btn-ok adm-close-report" data-id="' + r.id + '">' + T('admin.reportClose') + '</button></div>' : '<div class="tiny muted">closed</div>') +
        '</div></div>';
    }
    return h;
  }

  function bindReportActions(scope) {
    var bs = (scope || document).querySelectorAll('.adm-close-report');
    for (var i = 0; i < bs.length; i++) {
      if (bs[i].getAttribute('data-bound')) continue;
      bs[i].setAttribute('data-bound', '1');
      bs[i].addEventListener('click', function () {
        var id = this.getAttribute('data-id');
        UI.modal({
          title: T('admin.reportClose'),
          icon: 'flag',
          body: '<div class="field"><label>' + T('admin.note') + '</label><input class="input" id="rep-note"></div>',
          actions: [
            { label: T('common.cancel'), cls: 'btn-ghost' },
            {
              label: T('admin.close'), cls: 'btn-primary', onClick: function (box) {
                Api.request('POST', '/api/admin/report', { id: Number(id), note: box.querySelector('#rep-note').value }, function (err) {
                  if (err) { UI.toast(Api.errorText(err), 'err'); return; }
                  UI.toast(T('admin.saved'), 'ok');
                  App.route();
                });
              }
            }
          ]
        });
      });
    }
  }

  function adminLogs(body) {
    body.innerHTML = '<div class="card"><div class="card-h">' + UI.icon('terminal') + ' ' + T('admin.logs') +
      '<span class="right"><select class="select" id="log-level" style="width:auto;padding:2px 22px 2px 6px;font-size:11px"><option value="">' + T('common.all') + '</option><option value="error">E</option><option value="warn">W</option><option value="info">I</option><option value="debug">D</option></select> <button class="btn btn-xs" id="log-refresh">' + T('admin.refresh') + '</button></span></div>' +
      '<div class="card-b"><div class="log-box" id="log-box">' + UI.spinner() + '</div></div></div>';
    var level = '';
    document.getElementById('log-level').addEventListener('change', function () { level = this.value; loadLogs(0, level, true); });
    document.getElementById('log-refresh').addEventListener('click', function () { loadLogs(0, level, true); });
    loadLogs(0, level, true);
    state.adminLogs.timer = setInterval(function () {
      if (App.state.route !== 'admin') return;
      loadLogs(state.adminLogs.since || 0, level, false);
    }, 5000);
  }

  function loadLogs(since, level, reset) {
    var box = document.getElementById('log-box');
    if (!box) return;
    Api.request('GET', '/api/admin/logs?limit=200&since=' + (since || 0) + (level ? '&level=' + level : ''), null, function (err, data) {
      if (err) { if (reset) box.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>'; return; }
      var entries = data.entries || [];
      state.adminLogs.since = data.lastSeq || 0;
      if (reset) box.innerHTML = '';
      var html = '';
      for (var i = 0; i < entries.length; i++) {
        var e = entries[i];
        var lv = e.level || 'I';
        html += '<div class="log-line ' + UI.attr(lv) + '"><span class="lv">' + UI.esc(lv) + '</span> ' + UI.esc(e.time) + ' [' + UI.esc(e.category) + '] ' + UI.esc(e.message) + '</div>';
      }
      box.insertAdjacentHTML('beforeend', html);
      box.scrollTop = box.scrollHeight;
    });
  }

  function logHtml(entries) {
    if (!entries.length) return '<div class="tiny muted">' + T('common.none') + '</div>';
    var h = '<div class="log-box">';
    for (var i = 0; i < entries.length; i++) {
      var e = entries[i];
      h += '<div class="log-line ' + UI.attr(e.level) + '"><span class="lv">' + UI.esc(e.level) + '</span> ' + UI.esc(e.time) + ' [' + UI.esc(e.category) + '] ' + UI.esc(e.message) + '</div>';
    }
    return h + '</div>';
  }

  // ---------------------------------------------------------------- search

  views.search = function (main, r) {
    var q = App.qs('q') || '';
    main.innerHTML = spinnerCard();
    if (!q) { main.innerHTML = '<div class="card"><div class="card-b">' + UI.empty('magnifying-glass', T('search.title')) + '</div></div>'; return; }
    Api.request('GET', '/api/search?q=' + encodeURIComponent(q), null, function (err, data) {
      if (err) { main.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>'; return; }
      var users = data.users || [];
      var posts = data.posts || [];
      var html = '<div class="card"><div class="card-h">' + UI.icon('magnifying-glass') + ' ' + T('search.resultsFor', q) + '</div><div class="card-b">';
      if (!users.length && !posts.length) html += UI.empty('magnifying-glass', T('search.empty'));
      else {
        if (users.length) {
          html += '<div class="section-title">' + UI.icon('users') + ' ' + T('search.users') + '</div><div class="user-grid" style="margin-bottom:12px">';
          for (var i = 0; i < users.length; i++) html += actionCard(users[i]);
          html += '</div>';
        }
        if (posts.length) {
          html += '<div class="section-title">' + UI.icon('comments') + ' ' + T('search.posts') + '</div>';
          for (var j = 0; j < posts.length; j++) html += App.postHtml(posts[j]);
        }
      }
      html += '</div></div>';
      main.innerHTML = html;
      App.bindPosts(main);
      App.bindFriendActions(main);
    });
  };

  // start once every view is registered
  App.init();
})();
