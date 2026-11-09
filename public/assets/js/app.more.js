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

  var EMOJI = ['😀', '😄', '😁', '😆', '😅', '😂', '🙂', '😉', '😊', '😍', '😘', '😗', '😎', '🤔', '😐', '😴',
    '😢', '😭', '😤', '😡', '🤯', '😱', '🤗', '🤫', '🙄', '😬', '🤐', '😷', '🤒', '🥳', '🥺', '😇',
    '👍', '👎', '👌', '✌️', '🤞', '🤝', '👏', '🙌', '🙏', '💪', '👀', '🧠', '❤️', '🧡', '💛', '💚',
    '💙', '💜', '🖤', '💔', '💯', '🔥', '✨', '🎉', '🎁', '☕', '🍕', '🍺', '🌙', '☀️', '⚡', '💤',
    '🐱', '🐶', '🦊', '🐻', '🐼', '🦄', '🌈', '⭐', '🚀', '💻', '🎮', '🎧', '📷', '📌', '✅', '❌'];

  var IMG_RE = /^(https?:\/\/[^\s]+\.(?:png|jpe?g|gif|webp|avif))(\?\S*)?$/i;
  var VID_RE = /^(https?:\/\/[^\s]+\.(?:mp4|webm|ogv))(\?\S*)?$/i;

  function msgPreview(m) {
    var body = String((m && m.body) || '').replace(/\s+/g, ' ').trim();
    if (body) return body.substring(0, 60);
    if (m && m.image) return T('messages.photo');
    return '';
  }

  function dayLabel(iso) {
    var d = new Date(iso);
    if (isNaN(d.getTime())) return '';
    var now = new Date();
    var start = function (x) { return new Date(x.getFullYear(), x.getMonth(), x.getDate()).getTime(); };
    var diff = Math.round((start(now) - start(d)) / 86400000);
    if (diff === 0) return T('messages.today');
    if (diff === 1) return T('messages.yesterday');
    return UI.formatDate(iso);
  }

  function msgBodyHtml(text) {
    var raw = String(text || '').trim();
    if (!raw) return '';
    if (IMG_RE.test(raw)) {
      return '<img class="msg-img" src="' + UI.attr(raw) + '" alt="" loading="lazy">';
    }
    if (VID_RE.test(raw)) {
      return '<video class="msg-video" controls preload="metadata" src="' + UI.attr(raw) + '"></video>';
    }
    return UI.linkify(text).replace(/\n/g, '<br>');
  }

  function bubbleHtml(m, other) {
    var mine = !!m.mine;
    var u = mine ? state.user : other;
    var quote = '';
    if (m.replyTo) {
      var q = m.replyTo;
      var qname = q.mine ? T('common.you') : UI.esc(other.displayName || other.username);
      quote = '<div class="msg-quote"><b>' + qname + '</b><span>' +
        UI.esc(q.body || (q.image ? T('messages.photo') : '')) + '</span></div>';
    }
    var img = UI.safeUrl(m.image);
    var tick = mine ? '<span class="tick' + (m.read ? ' read' : '') + '" title="' + UI.attr(m.read ? T('messages.read') : T('messages.sent')) + '">' +
      UI.icon(m.read ? 'check-double' : 'check') + '</span>' : '';
    return '<div class="bubble-row' + (mine ? ' mine' : '') + '" data-id="' + m.id + '">' + UI.avatar(u, 26) +
      '<div class="bubble">' + quote +
      (img ? '<img class="msg-img" src="' + UI.attr(img) + '" alt="" loading="lazy">' : '') +
      (m.body ? '<div class="msg-text">' + msgBodyHtml(m.body) + '</div>' : '') +
      '<div class="msg-foot"><span class="time">' + UI.formatTime(m.created) + '</span>' + tick +
      '<button type="button" class="msg-act msg-reply" data-id="' + m.id + '" title="' + UI.attr(T('messages.reply')) + '">' + UI.icon('reply') + '</button>' +
      (mine ? '<button type="button" class="msg-act msg-del" data-id="' + m.id + '" title="' + UI.attr(T('common.delete')) + '">' + UI.icon('xmark') + '</button>' : '') +
      '</div></div></div>';
  }

  views.im = function (main, r) {
    var withId = Number(r.parts[1] || 0);
    if (state.im.timer) { clearInterval(state.im.timer); state.im.timer = null; }
    state.im.withId = withId;
    state.im.last = 0;
    state.im.replyTo = 0;
    state.im.image = '';
    state.im.typingSent = 0;
    main.innerHTML = '<div class="card"><div class="card-b center" style="padding:26px">' + UI.spinner() + '</div></div>';
    Api.request('GET', '/api/messages/threads', null, function (err, data) {
      if (err) { main.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>'; return; }
      var threads = data.threads || [];
      var html = '<div class="card"><div class="card-h">' + UI.icon('envelope') + ' ' + T('messages.title') +
        '<span class="right"><a href="#/people">' + UI.icon('user-plus') + ' ' + T('profile.message') + '</a></span></div>';
      html += '<div class="msg-layout"><div class="threads" id="threads">';
      if (!threads.length) html += '<div class="empty" style="padding:24px 10px">' + UI.icon('inbox') + '<b>' + T('messages.noThreads') + '</b></div>';
      for (var i = 0; i < threads.length; i++) html += threadHtml(threads[i], withId);
      html += '</div><div class="chat" id="chat">';
      if (!withId) html += '<div class="chat-empty">' + UI.icon('comments') + ' ' + T('messages.select') + '</div>';
      else html += '<div class="chat-empty">' + UI.spinner() + '</div>';
      html += '</div></div></div>';
      main.innerHTML = html;
      bindThreads(main);
      if (withId) loadConversation(withId, false);
      // cheap safety net; the live loop does the real work
      state.im.timer = setInterval(function () {
        if (App.state.route !== 'im' || !state.im.withId) return;
        if (document.hidden) return;
        if (!(state.site && state.site.liveNotifications)) loadConversation(state.im.withId, true);
        refreshThreads();
      }, 8000);
    });
  };

  function bindThreads(scope) {
    var ts = (scope || document).querySelectorAll('.thread');
    for (var j = 0; j < ts.length; j++) {
      if (ts[j].getAttribute('data-bound')) continue;
      ts[j].setAttribute('data-bound', '1');
      ts[j].addEventListener('click', function () { App.go('#/im/' + this.getAttribute('data-user')); });
    }
  }

  function threadHtml(t, activeId) {
    var o = t.other || {};
    var last = t.last || {};
    var preview = t.typing ? T('messages.typingShort') : msgPreview(last);
    var icon = !last.body && last.image ? UI.icon('image') + ' ' : '';
    return '<div class="thread' + (Number(activeId) === Number(o.id) ? ' active' : '') + '" data-user="' + o.id + '">' +
      UI.avatar(o, 34) + '<div class="thread-main"><div class="name">' + UI.esc(o.displayName) + '</div>' +
      '<div class="last' + (t.typing ? ' typing' : '') + '">' + icon + UI.esc(preview) + '</div></div>' +
      '<div class="thread-side"><span class="thread-time">' + (last.created ? UI.timeAgo(last.created) : '') + '</span>' +
      (t.unread ? '<span class="unread">' + (t.unread > 99 ? '99+' : t.unread) + '</span>' : '') + '</div></div>';
  }

  function chatComposerHtml() {
    return '<form class="chat-form" id="chat-form">' +
      '<div class="chat-reply hidden" id="chat-reply"><div class="chat-reply-body"><b id="chat-reply-name"></b><span id="chat-reply-text"></span></div>' +
      '<button type="button" class="icon-btn" id="chat-reply-x">' + UI.icon('xmark') + '</button></div>' +
      '<div class="chat-attach hidden" id="chat-attach"><img id="chat-attach-img" alt="">' +
      '<button type="button" class="icon-btn" id="chat-attach-x">' + UI.icon('xmark') + '</button></div>' +
      '<div class="chat-row">' +
      '<button type="button" class="chat-btn" id="chat-file-btn" title="' + UI.attr(T('messages.attach')) + '">' + UI.icon('image') + '</button>' +
      '<input type="file" id="chat-file" class="hidden" accept="image/png,image/jpeg,image/gif,image/webp">' +
      '<button type="button" class="chat-btn" id="chat-emoji-btn" title="' + UI.attr(T('messages.emoji')) + '">' + UI.icon('face-smile') + '</button>' +
      '<textarea id="chat-text" rows="1" maxlength="' + (state.site.maxMessageLength || 4000) + '" placeholder="' + UI.attr(T('messages.placeholder')) + '"></textarea>' +
      '<button class="chat-send" type="submit" id="chat-send" title="' + UI.attr(T('common.send')) + '">' + UI.icon('paper-plane') + '</button>' +
      '</div>' +
      '<div class="emoji-pop hidden" id="chat-emoji"></div>' +
      '</form>';
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
      var oldBody = document.getElementById('chat-body');
      var atBottom = true;
      if (oldBody) atBottom = (oldBody.scrollHeight - oldBody.scrollTop - oldBody.clientHeight) < 60;

      var body = '<div class="chat-head">' + UI.avatar(other, 34) +
        '<div class="chat-head-main"><div class="name">' + UI.esc(other.displayName) + '</div>' +
        '<div class="tiny muted" id="chat-presence">' + (other.online ? T('profile.online') : '@' + UI.esc(other.username)) + '</div></div>' +
        '<a class="btn btn-sm right" href="#/profile/' + other.id + '">' + T('nav.profile') + '</a></div>';
      body += '<div class="chat-body" id="chat-body">';
      if (data.hasMore) body += '<div class="center" style="padding:6px"><button type="button" class="btn btn-xs" id="chat-older">' + T('messages.loadOlder') + '</button></div>';
      if (!messages.length) body += '<div class="chat-empty">' + T('messages.empty') + '</div>';
      var lastDay = '';
      for (var i = 0; i < messages.length; i++) {
        var day = dayLabel(messages[i].created);
        if (day && day !== lastDay) { body += '<div class="chat-day">' + UI.esc(day) + '</div>'; lastDay = day; }
        body += bubbleHtml(messages[i], other);
      }
      body += '</div>';
      body += chatComposerHtml();
      chat.innerHTML = body;
      chat.setAttribute('data-last', lastId);
      state.im.last = lastId;
      state.im.other = other;

      var newBody = document.getElementById('chat-body');
      if (newBody && (atBottom || !silent)) newBody.scrollTop = newBody.scrollHeight;
      var newText = document.getElementById('chat-text');
      if (newText && typed) newText.value = typed;
      if (newText && hadFocus) { try { newText.focus(); } catch (e) { } }
      if (newText) autoGrow(newText);

      bindChat(withId, other);
      if (!silent) refreshThreads();
      startMessageLoop(withId);
    });
  }

  function autoGrow(ta) {
    ta.style.height = 'auto';
    ta.style.height = Math.min(140, ta.scrollHeight) + 'px';
  }

  function bindChat(withId, other) {
    var form = document.getElementById('chat-form');
    if (!form) return;
    var ta = document.getElementById('chat-text');
    var file = document.getElementById('chat-file');
    var attach = document.getElementById('chat-attach');
    var attachImg = document.getElementById('chat-attach-img');
    var emojiPop = document.getElementById('chat-emoji');
    var replyBar = document.getElementById('chat-reply');
    var replyName = document.getElementById('chat-reply-name');
    var replyText = document.getElementById('chat-reply-text');
    var sendBtn = document.getElementById('chat-send');
    state.im.image = '';

    // older messages
    var older = document.getElementById('chat-older');
    if (older) older.addEventListener('click', function () {
      var body = document.getElementById('chat-body');
      if (!body || !state.im.last) return;
      older.disabled = true;
      Api.request('GET', '/api/messages?with=' + withId + '&limit=40&before=' + state.im.last, null, function (e2, d2) {
        if (e2) { older.disabled = false; UI.toast(Api.errorText(e2), 'err'); return; }
        var older2 = (d2.messages || []).slice().reverse();
        var html = '';
        for (var i = 0; i < older2.length; i++) html += bubbleHtml(older2[i], other);
        var keep = body.scrollHeight;
        body.insertAdjacentHTML('afterbegin', html);
        body.scrollTop = body.scrollHeight - keep;
        older.disabled = false;
        if (!d2.hasMore && older.parentNode) older.parentNode.removeChild(older);
        bindBubbles(body);
      });
    });

    // emoji picker
    var emojiHtml = '';
    for (var i = 0; i < EMOJI.length; i++) {
      emojiHtml += '<button type="button" class="emoji-item" data-emoji="' + EMOJI[i] + '">' + EMOJI[i] + '</button>';
    }
    emojiPop.innerHTML = emojiHtml;

    document.getElementById('chat-emoji-btn').addEventListener('click', function (e) {
      e.stopPropagation();
      var open = emojiPop.className.indexOf('hidden') < 0;
      emojiPop.className = open ? 'emoji-pop hidden' : 'emoji-pop';
    });
    var items = emojiPop.querySelectorAll('.emoji-item');
    for (var k = 0; k < items.length; k++) {
      items[k].addEventListener('mousedown', function (e) {
        e.preventDefault();
        insertAtCursor(ta, this.getAttribute('data-emoji'));
        emojiPop.className = 'emoji-pop hidden';
      });
    }

    // attachments
    document.getElementById('chat-file-btn').addEventListener('click', function () { file.click(); });
    file.addEventListener('change', function () {
      var f = file.files && file.files[0];
      if (!f) return;
      UI.fileToDataUrl(f, (state.site.maxMessageImageKB || 2048) * 1024, function (err, dataUrl) {
        if (err) {
          UI.toast(err === 'too-large' ? T('messages.imageTooBig', state.site.maxMessageImageKB || 2048) : T('errors.generic'), 'err');
          file.value = '';
          return;
        }
        state.im.image = dataUrl;
        attachImg.src = dataUrl;
        attach.className = 'chat-attach';
        ta.focus();
      });
    });
    document.getElementById('chat-attach-x').addEventListener('click', function () {
      state.im.image = '';
      file.value = '';
      attach.className = 'chat-attach hidden';
      attachImg.removeAttribute('src');
    });

    // reply bar
    function clearReply() {
      state.im.replyTo = 0;
      replyBar.className = 'chat-reply hidden';
    }
    document.getElementById('chat-reply-x').addEventListener('click', clearReply);

    // typing + enter to send
    ta.addEventListener('input', function () {
      autoGrow(ta);
      var now = Date.now();
      if (now - (state.im.typingSent || 0) > 3000) {
        state.im.typingSent = now;
        Api.request('POST', '/api/messages/typing', { to: withId }, function () { });
      }
    });
    ta.addEventListener('keydown', function (e) {
      var key = e.keyCode || e.which;
      if (key === 13 && !e.shiftKey) {
        e.preventDefault();
        submit();
      }
    });

    function submit() {
      var text = ta.value.trim();
      if (!text && !state.im.image) { ta.focus(); return; }
      sendBtn.disabled = true;
      Api.request('POST', '/api/messages/send', {
        to: withId,
        body: text,
        image: state.im.image || '',
        replyTo: state.im.replyTo || 0
      }, function (err, data) {
        sendBtn.disabled = false;
        if (err) { UI.toast(Api.errorText(err), 'err'); return; }
        ta.value = '';
        autoGrow(ta);
        state.im.image = '';
        file.value = '';
        attach.className = 'chat-attach hidden';
        clearReply();
        appendMessages([data.message], other, true);
        SoundFX.play('message');
        refreshThreads();
      });
    }

    form.addEventListener('submit', function (e) { e.preventDefault(); submit(); });
    bindBubbles(document.getElementById('chat-body'));
  }

  function insertAtCursor(ta, text) {
    if (!ta) return;
    var start = ta.selectionStart || 0;
    var end = ta.selectionEnd || 0;
    ta.value = ta.value.substring(0, start) + text + ta.value.substring(end);
    var pos = start + text.length;
    try { ta.setSelectionRange(pos, pos); } catch (e) { }
    ta.focus();
    autoGrow(ta);
  }

  function appendMessages(list, other, mineNow) {
    var body = document.getElementById('chat-body');
    if (!body || !list || !list.length) return;
    var atBottom = (body.scrollHeight - body.scrollTop - body.clientHeight) < 90;
    var existing = {};
    var nodes = body.querySelectorAll('.bubble-row');
    for (var e = 0; e < nodes.length; e++) existing[nodes[e].getAttribute('data-id')] = 1;
    var html = '';
    var lastDay = '';
    var prev = body.querySelectorAll('.chat-day');
    if (prev.length) lastDay = prev[prev.length - 1].textContent;
    for (var i = 0; i < list.length; i++) {
      if (existing[String(list[i].id)]) continue;
      var day = dayLabel(list[i].created);
      if (day && day !== lastDay) { html += '<div class="chat-day">' + UI.esc(day) + '</div>'; lastDay = day; }
      html += bubbleHtml(list[i], other);
    }
    if (!html) return;
    body.insertAdjacentHTML('beforeend', html);
    if (state.im.last < list[list.length - 1].id) state.im.last = list[list.length - 1].id;
    var chat = document.getElementById('chat');
    if (chat) chat.setAttribute('data-last', String(state.im.last));
    if (atBottom || mineNow) body.scrollTop = body.scrollHeight;
    bindBubbles(body);
  }

  function bindBubbles(scope) {
    var replies = (scope || document).querySelectorAll('.msg-reply');
    for (var r = 0; r < replies.length; r++) {
      if (replies[r].getAttribute('data-bound')) continue;
      replies[r].setAttribute('data-bound', '1');
      replies[r].addEventListener('click', function (e) {
        e.preventDefault();
        var row = this;
        while (row && row.className.indexOf('bubble-row') < 0) row = row.parentNode;
        if (!row) return;
        var textEl = row.querySelector('.msg-text');
        var imgEl = row.querySelector('.msg-img');
        var preview = textEl ? textEl.textContent : (imgEl ? T('messages.photo') : '');
        state.im.replyTo = Number(row.getAttribute('data-id'));
        var bar = document.getElementById('chat-reply');
        var name = document.getElementById('chat-reply-name');
        var txt = document.getElementById('chat-reply-text');
        var mine = row.className.indexOf('mine') >= 0;
        var o = state.im.other || {};
        if (name) name.textContent = mine ? T('common.you') : (o.displayName || o.username || '');
        if (txt) txt.textContent = String(preview).substring(0, 80);
        if (bar) bar.className = 'chat-reply';
        var ta = document.getElementById('chat-text');
        if (ta) ta.focus();
      });
    }

    var dels = (scope || document).querySelectorAll('.msg-del');
    for (var i = 0; i < dels.length; i++) {
      if (dels[i].getAttribute('data-bound')) continue;
      dels[i].setAttribute('data-bound', '1');
      dels[i].addEventListener('click', function (e) {
        e.preventDefault();
        var id = this.getAttribute('data-id');
        var row = this;
        while (row && row.className.indexOf('bubble-row') < 0) row = row.parentNode;
        UI.confirm(T('common.delete'), T('messages.deleteConfirm'), T('common.delete'), function () {
          Api.request('POST', '/api/messages/delete', { id: Number(id) }, function (err) {
            if (err) { UI.toast(Api.errorText(err), 'err'); return; }
            if (row && row.parentNode) row.parentNode.removeChild(row);
            refreshThreads();
          });
        }, true);
      });
    }
    // clicking a quote scrolls to the quoted message
    var quotes = (scope || document).querySelectorAll('.msg-quote');
    for (var q = 0; q < quotes.length; q++) {
      if (quotes[q].getAttribute('data-bound')) continue;
      quotes[q].setAttribute('data-bound', '1');
      quotes[q].addEventListener('click', function () {
        var row = this;
        while (row && row.className.indexOf('bubble-row') < 0) row = row.parentNode;
        var id = row ? row.getAttribute('data-id') : '';
        var text = this.querySelector('span');
        if (!text) return;
        var target = null;
        var rows = document.querySelectorAll('.bubble-row');
        for (var i = 0; i < rows.length; i++) {
          if (rows[i] !== row && rows[i].querySelector('.msg-text, .msg-img') &&
              rows[i].innerText.indexOf(text.textContent) >= 0) { target = rows[i]; break; }
        }
        if (target && target.scrollIntoView) { try { target.scrollIntoView({ block: 'center', behavior: 'smooth' }); } catch (e) { } }
      });
    }
  }

  function setPresence(typing) {
    var el = document.getElementById('chat-presence');
    if (!el) return;
    el.innerHTML = typing ? '<span class="typing-dots"><i></i><i></i><i></i></span> ' + T('messages.typing') : '';
    if (!typing) {
      var o = state.im.other || {};
      el.textContent = o.online ? T('profile.online') : '@' + (o.username || '');
    }
  }

  function markTicks(read) {
    if (!read) return;
    var ticks = document.querySelectorAll('.bubble-row.mine .tick');
    for (var i = 0; i < ticks.length; i++) ticks[i].className = 'tick read';
  }

  /// Long poll the open conversation: new messages, typing and read receipts.
  function startMessageLoop(withId) {
    if (!(state.site && state.site.liveNotifications)) return;
    var stop = false;

    function pump() {
      if (stop) return;
      if (App.state.route !== 'im' || state.im.withId !== withId) { stop = true; return; }
      if (document.hidden) { setTimeout(pump, 2500); return; }
      var after = state.im.last || 0;
      Api.request('GET', '/api/messages/wait?with=' + withId + '&after=' + after, null, function (err, data) {
        if (App.state.route !== 'im' || state.im.withId !== withId) { stop = true; return; }
        if (err) { setTimeout(pump, 4000); return; }
        if (data.messages && data.messages.length) {
          appendMessages(data.messages, state.im.other || {}, false);
          SoundFX.play('message');
          refreshThreads();
        }
        setPresence(!!data.typing);
        markTicks(!!data.read);
        if (typeof data.unread === 'number') App.setBadge('badge-msg', data.unread);
        pump();
      });
    }
    pump();
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
      bindThreads(box);
      if (typeof data.unread === 'number') App.setBadge('badge-msg', data.unread);
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
        (g.canManage ? '<button class="btn" id="group-edit">' + UI.icon('pen') + ' ' + T('groups.edit') + '</button>' : '') +
        '<button class="btn" id="group-toggle">' + (g.isMember ? (g.isOwner ? UI.icon('crown') + ' ' + T('groups.owner') : UI.icon('right-from-bracket') + ' ' + T('groups.leave')) : UI.icon('plus') + ' ' + T('groups.join')) + '</button>' +
        '</div><div class="card-b">';
      html += '<div class="feed-tabs"><a class="tab2 active" href="#/group/' + g.id + '">' + T('groups.posts') + '</a></div>';
      if (g.isMember) html += App.composerHtml(0);
      html += '<div id="group-posts"></div><div id="group-more"></div>';
      html += '</div></div>';

      // members: owners get a "remove" button on everyone but themselves
      var members = data.members || [];
      var ownerId = (g.owner && g.owner.id) || 0;
      html += '<div class="card"><div class="card-h">' + UI.icon('users') + ' ' + T('groups.members') +
        '<span class="right tiny muted">' + g.members + '</span></div><div class="card-b"><div class="user-grid">';
      for (var i = 0; i < members.length; i++) {
        var m = members[i];
        if (g.canManage && Number(m.id) !== Number(ownerId)) {
          html += '<div class="user-card"><a href="#/u/' + encodeURIComponent(m.username) + '">' + UI.avatar(m, 52) + '</a>' +
            '<a class="name" href="#/u/' + encodeURIComponent(m.username) + '">' + UI.esc(m.displayName) + '</a>' +
            '<div class="sub">' + (m.online ? T('profile.online') : '@' + UI.esc(m.username)) + '</div>' +
            '<button class="btn btn-sm btn-danger g-kick" data-id="' + m.id + '">' + T('groups.kick') + '</button></div>';
        } else {
          html += App.userCard(m);
        }
      }
      html += '</div></div></div>';
      main.innerHTML = html;
      if (g.isMember) {
        // group posts go through /api/post with the group id
        App.bindComposer(0, g.id);
      }
      var ge = document.getElementById('group-edit');
      if (ge) ge.addEventListener('click', function () { groupEditModal(g); });

      var kicks = main.querySelectorAll('.g-kick');
      for (var k = 0; k < kicks.length; k++) {
        kicks[k].addEventListener('click', function () {
          var uid = this.getAttribute('data-id');
          UI.confirm(T('groups.kick'), T('groups.kickConfirm'), T('groups.kick'), function () {
            Api.request('POST', '/api/group/kick', { id: g.id, user: Number(uid) }, function (e2) {
              if (e2) { UI.toast(Api.errorText(e2), 'err'); return; }
              UI.toast(T('groups.kicked'), 'ok');
              SoundFX.play('success');
              App.route();
            });
          }, true);
        });
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

  function groupEditModal(g) {
    var avatar = UI.safeUrl(g.avatar);
    var body = '<div class="field"><label>' + T('groups.name') + '</label>' +
      '<input class="input" id="ge-name" maxlength="80" value="' + UI.attr(g.name) + '"></div>' +
      '<div class="field"><label>' + T('groups.description') + '</label>' +
      '<textarea class="textarea" id="ge-desc" rows="4" maxlength="2000">' + UI.esc(g.description || '') + '</textarea></div>' +
      '<div class="field"><label>' + T('groups.visibility') + '</label><select class="select" id="ge-vis">' +
      '<option value="public"' + (g.visibility === 'public' ? ' selected' : '') + '>' + T('groups.public') + '</option>' +
      '<option value="private"' + (g.visibility === 'private' ? ' selected' : '') + '>' + T('groups.private') + '</option>' +
      '</select></div>' +
      '<div class="field"><label>' + T('groups.avatar') + '</label>' +
      '<div style="display:flex;gap:10px;align-items:center">' +
      UI.avatar({ id: g.id, displayName: g.name, avatar: g.avatar }, 56, false) +
      '<input type="file" id="ge-file" accept="image/png,image/jpeg,image/gif,image/webp">' +
      '</div><div class="hint">' + T('settings.avatarHint', state.site.maxAvatarKB || 512) + '</div></div>' +
      '<div style="display:flex;gap:6px;justify-content:flex-end">' +
      '<button type="button" class="btn btn-ghost" id="ge-cancel">' + T('common.cancel') + '</button>' +
      '<button type="button" class="btn btn-primary" id="ge-save">' + UI.icon('floppy-disk') + ' ' + T('common.save') + '</button>' +
      '</div>';

    UI.modal({ title: T('groups.edit'), icon: 'people-group', wide: true, body: body, actions: [] });
    var boxes = document.querySelectorAll('.modal');
    var box = boxes[boxes.length - 1];
    if (!box) return;

    function close() {
      var scrim = box.parentNode;
      if (scrim && scrim.parentNode) scrim.parentNode.removeChild(scrim);
    }

    box.querySelector('#ge-cancel').addEventListener('click', close);
    box.querySelector('#ge-save').addEventListener('click', function () {
      var btn = this;
      btn.disabled = true;
      var file = box.querySelector('#ge-file');
      var f = file && file.files && file.files[0];

      function saveMeta(done) {
        Api.request('POST', '/api/group/edit', {
          id: g.id,
          name: box.querySelector('#ge-name').value.trim(),
          description: box.querySelector('#ge-desc').value,
          visibility: box.querySelector('#ge-vis').value
        }, function (err) {
          if (err) { btn.disabled = false; UI.toast(Api.errorText(err), 'err'); return; }
          done();
        });
      }

      function saveAvatar(done) {
        if (!f) { done(); return; }
        UI.fileToDataUrl(f, (state.site.maxAvatarKB || 512) * 1024, function (e2, dataUrl) {
          if (e2) {
            btn.disabled = false;
            UI.toast(e2 === 'too-large' ? T('settings.avatarHint', state.site.maxAvatarKB) : T('errors.generic'), 'err');
            return;
          }
          Api.request('POST', '/api/group/avatar', { id: g.id, data: dataUrl }, function (e3) {
            if (e3) { btn.disabled = false; UI.toast(Api.errorText(e3), 'err'); return; }
            done();
          });
        });
      }

      saveMeta(function () {
        saveAvatar(function () {
          btn.disabled = false;
          close();
          UI.toast(T('groups.saved'), 'ok');
          SoundFX.play('success');
          App.route();
        });
      });
    });
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
    var filter = App.qs('f') || 'all';
    if (filter !== 'unread' && filter !== 'mentions') filter = 'all';
    main.innerHTML = spinnerCard();
    Api.request('GET', '/api/notifications?limit=60', null, function (err, data) {
      if (err) { main.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>'; return; }
      var all = data.notifications || [];
      var list = [];
      for (var i = 0; i < all.length; i++) {
        var n = all[i];
        if (filter === 'unread' && n.read) continue;
        if (filter === 'mentions' && n.type !== 'mention') continue;
        list.push(n);
      }

      function ntab(key, label, count) {
        return '<a class="tab2' + (filter === key ? ' active' : '') + '" href="#/notifications' +
          (key === 'all' ? '' : '?f=' + key) + '">' + UI.esc(label) +
          (count ? ' <span class="count" style="margin-left:4px">' + (count > 99 ? '99+' : count) + '</span>' : '') + '</a>';
      }

      var html = '<div class="card"><div class="card-h">' + UI.icon('bell') + ' ' + T('notifications.title') +
        '<span class="right"><button class="btn btn-xs" id="notif-read">' + T('notifications.markAll') + '</button></span></div>' +
        '<div class="card-b" style="padding:8px 10px 0"><div class="feed-tabs" style="margin:0">' +
        ntab('all', T('notifications.all')) +
        ntab('unread', T('notifications.unreadTab'), data.unread) +
        ntab('mentions', T('notifications.mentions')) +
        '</div></div><div class="card-b flush">';
      if (!list.length) {
        html += UI.empty('bell', filter === 'unread' ? T('notifications.noUnread') : T('notifications.empty'),
          filter === 'mentions' ? T('notifications.noMentions') : '');
      } else {
        for (var j = 0; j < list.length; j++) {
          var item = list[j];
          var a = item.actor || {};
          var ic = item.type === 'like' ? 'heart' : item.type === 'comment' ? 'comment' : item.type === 'friend' ? 'user-plus'
            : item.type === 'message' ? 'envelope' : item.type === 'admin' ? 'bullhorn' : item.type === 'badge' ? 'trophy'
            : item.type === 'mention' ? 'at' : 'bell';
          html += '<a class="notif' + (item.read ? '' : ' unread') + '" data-id="' + item.id + '" href="' + UI.attr(item.link || '#/notifications') + '">' +
            UI.avatar(a, 32) + '<div class="notif-main"><span class="who">' + UI.esc(a.displayName || a.username || '?') + '</span> ' + UI.esc(item.text) +
            '<div class="when">' + UI.timeAgo(item.created) + '</div></div><span class="type">' + UI.icon(ic) + '</span></a>';
        }
      }
      html += '</div></div>';
      main.innerHTML = html;

      var b = document.getElementById('notif-read');
      if (b) b.addEventListener('click', function () {
        Api.request('POST', '/api/notifications/read', { all: true }, function (err2, d2) {
          if (err2) { UI.toast(Api.errorText(err2), 'err'); return; }
          UI.toast(T('notifications.marked'), 'ok');
          if (d2) App.setUnreadLocal(d2.unread);
          App.route();
        });
      });

      // opening a notification marks it read
      var items = main.querySelectorAll('.notif.unread');
      for (var k = 0; k < items.length; k++) {
        items[k].addEventListener('click', function () {
          var id = Number(this.getAttribute('data-id'));
          Api.request('POST', '/api/notifications/read', { id: id }, function (err3, d3) {
            if (err3 || !d3) return;
            App.setUnreadLocal(d3.unread);
          });
        });
      }
    });
  };

  // -------------------------------------------------------------- bookmarks

  views.bookmarks = function (main) {
    var next = 0;
    main.innerHTML = '<div class="card"><div class="card-h">' + UI.icon('bookmark') + ' ' + T('nav.bookmarks') +
      '<span class="right tiny muted" id="bm-total"></span></div>' +
      '<div class="card-b" id="bm-list"><div class="center" style="padding:22px">' + UI.spinner() + '</div></div></div>' +
      '<div id="bm-more"></div>';
    var list = document.getElementById('bm-list');
    var moreBox = document.getElementById('bm-more');
    var totalBox = document.getElementById('bm-total');

    function load(more) {
      var url = '/api/bookmarks?limit=20' + (more && next ? '&before=' + encodeURIComponent(next) : '');
      if (more) moreBox.innerHTML = '<div class="center" style="padding:10px">' + UI.spinner() + '</div>';
      Api.request('GET', url, null, function (err, data) {
        if (err) {
          list.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>';
          moreBox.innerHTML = '';
          return;
        }
        var posts = data.posts || [];
        if (totalBox) totalBox.textContent = T('bookmarks.count', data.total || 0);
        if (!more) list.innerHTML = '';
        if (!posts.length) {
          if (!more) list.innerHTML = UI.empty('bookmark', T('bookmarks.empty'), T('bookmarks.emptyHint'));
          moreBox.innerHTML = '';
          return;
        }
        var html = '';
        for (var i = 0; i < posts.length; i++) html += App.postHtml(posts[i]);
        list.insertAdjacentHTML('beforeend', html);
        App.bindPosts(list);
        next = data.next || 0;
        moreBox.innerHTML = data.hasMore
          ? '<button type="button" class="load-more" id="bm-load">' + T('feed.loadMore') + '</button>'
          : '<div class="center tiny muted" style="padding:10px">' + T('feed.noMore') + '</div>';
        var b = document.getElementById('bm-load');
        if (b) b.addEventListener('click', function () { load(true); });
      });
    }

    load(false);
  };

  // ----------------------------------------------------------------- photos

  views.photos = function (main, r) {
    var id = r.parts[1] || (state.user && state.user.id) || 0;
    var next = 0;
    main.innerHTML = '<div class="card"><div class="card-h">' + UI.icon('images') + ' <span id="ph-title">' + T('photos.title') + '</span>' +
      '<span class="right tiny muted" id="ph-total"></span></div>' +
      '<div class="card-b"><div class="photo-grid wide" id="ph-grid"><div class="center" style="padding:22px;grid-column:1/-1">' + UI.spinner() + '</div></div></div></div>' +
      '<div id="ph-more"></div>';
    var grid = document.getElementById('ph-grid');
    var moreBox = document.getElementById('ph-more');

    function bindCells() {
      var cells = grid.querySelectorAll('.photo-cell');
      for (var i = 0; i < cells.length; i++) {
        if (cells[i].getAttribute('data-bound')) continue;
        cells[i].setAttribute('data-bound', '1');
        cells[i].addEventListener('click', function () { UI.imageModal(this.getAttribute('data-full')); });
      }
    }

    function load(more) {
      var url = '/api/photos?user=' + encodeURIComponent(id) + '&limit=24' + (more && next ? '&before=' + encodeURIComponent(next) : '');
      if (more) moreBox.innerHTML = '<div class="center" style="padding:10px">' + UI.spinner() + '</div>';
      Api.request('GET', url, null, function (err, data) {
        if (err) {
          grid.innerHTML = '<div class="alert err" style="grid-column:1/-1">' + UI.esc(Api.errorText(err)) + '</div>';
          moreBox.innerHTML = '';
          return;
        }
        var photos = data.photos || [];
        if (data.user) {
          var title = document.getElementById('ph-title');
          if (title) title.textContent = T('photos.of', data.user.displayName || data.user.username);
        }
        if (!more) grid.innerHTML = '';
        if (!photos.length) {
          if (!more) grid.innerHTML = '<div style="grid-column:1/-1">' + UI.empty('images', T('photos.empty'), T('photos.emptyHint')) + '</div>';
          moreBox.innerHTML = '';
          return;
        }
        var html = '';
        for (var i = 0; i < photos.length; i++) {
          var src = UI.safeUrl(photos[i].image);
          if (!src) continue;
          html += '<button type="button" class="photo-cell" data-full="' + UI.attr(src) + '" title="' + UI.attr(UI.timeAgo(photos[i].created)) + '">' +
            '<img src="' + UI.attr(src) + '" alt="" loading="lazy"></button>';
        }
        grid.insertAdjacentHTML('beforeend', html);
        bindCells();
        next = data.next || 0;
        moreBox.innerHTML = data.hasMore
          ? '<button type="button" class="load-more" id="ph-load">' + T('feed.loadMore') + '</button>'
          : '';
        var b = document.getElementById('ph-load');
        if (b) b.addEventListener('click', function () { load(true); });
      });
    }

    load(false);
  };

  // ------------------------------------------------------------------- tags

  views.tag = function (main, r) {
    var tag = decodeURIComponent(r.parts[1] || '');
    var next = 0;
    main.innerHTML = '<div class="card"><div class="card-h">' + UI.icon('hashtag') + ' <span id="tag-title">#' + UI.esc(tag) + '</span>' +
      '<span class="right"><a href="#/feed">' + UI.icon('house') + ' ' + T('nav.feed') + '</a></span></div>' +
      '<div class="card-b" id="tag-list"><div class="center" style="padding:22px">' + UI.spinner() + '</div></div></div>' +
      '<div id="tag-more"></div>';
    var list = document.getElementById('tag-list');
    var moreBox = document.getElementById('tag-more');

    function load(more) {
      var url = '/api/tag?tag=' + encodeURIComponent(tag) + '&limit=20' + (more && next ? '&before=' + encodeURIComponent(next) : '');
      if (more) moreBox.innerHTML = '<div class="center" style="padding:10px">' + UI.spinner() + '</div>';
      Api.request('GET', url, null, function (err, data) {
        if (err) {
          list.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>';
          moreBox.innerHTML = '';
          return;
        }
        var posts = data.posts || [];
        if (!more) list.innerHTML = '';
        if (!posts.length) {
          if (!more) list.innerHTML = UI.empty('hashtag', T('tag.empty'), T('tag.emptyHint'));
          moreBox.innerHTML = '';
          return;
        }
        var html = '';
        for (var i = 0; i < posts.length; i++) html += App.postHtml(posts[i]);
        list.insertAdjacentHTML('beforeend', html);
        App.bindPosts(list);
        next = data.next || 0;
        moreBox.innerHTML = data.hasMore
          ? '<button type="button" class="load-more" id="tag-load">' + T('feed.loadMore') + '</button>'
          : '';
        var b = document.getElementById('tag-load');
        if (b) b.addEventListener('click', function () { load(true); });
      });
    }

    load(false);
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

      // notification preferences: unchecked = muted on the server
      var mutedTypes = String(u.notifyMute || '').split(',');
      var notifTypes = [
        ['like', 'notify.like'], ['comment', 'notify.comment'], ['friend', 'notify.friend'],
        ['message', 'notify.message'], ['wall', 'notify.wall'], ['group', 'notify.group'],
        ['mention', 'notify.mention'], ['badge', 'notify.badge']
      ];
      var notifHtml = '';
      for (var nti = 0; nti < notifTypes.length; nti++) {
        var nt = notifTypes[nti][0];
        notifHtml += '<label class="check"><input type="checkbox" data-notify-type="' + nt + '"' +
          (mutedTypes.indexOf(nt) < 0 ? ' checked' : '') + '> <span>' + T(notifTypes[nti][1]) + '</span></label>';
      }
      var desktopOn = false;
      try {
        desktopOn = localStorage.getItem('social.desktop') === '1' &&
          !!window.Notification && Notification.permission === 'granted';
      } catch (e) { }

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

      html += '<div class="settings-block"><h3>' + T('settings.interests') + '</h3>' +
        '<div class="hint" style="margin-bottom:8px">' + T('settings.interestsHint') + '</div>' +
        '<div class="field"><label>' + UI.icon('gamepad') + ' ' + T('profile.favGames') + '</label><input class="input" id="s-favgames" maxlength="200" value="' + UI.attr(u.favGames || '') + '"></div>' +
        '<div class="field"><label>' + UI.icon('film') + ' ' + T('profile.favMovies') + '</label><input class="input" id="s-favmovies" maxlength="200" value="' + UI.attr(u.favMovies || '') + '"></div>' +
        '<div class="field"><label>' + UI.icon('music') + ' ' + T('profile.favMusic') + '</label><input class="input" id="s-favmusic" maxlength="200" value="' + UI.attr(u.favMusic || '') + '"></div>' +
        '<div class="field"><label>' + UI.icon('book') + ' ' + T('profile.favBooks') + '</label><input class="input" id="s-favbooks" maxlength="200" value="' + UI.attr(u.favBooks || '') + '"></div>' +
        '<div class="field"><label>' + UI.icon('face-smile') + ' ' + T('profile.mood') + '</label><select class="select" id="s-mood">' +
        '<option value=""' + (!u.mood ? ' selected' : '') + '>' + T('common.none') + '</option>' +
        '<option value="happy"' + (u.mood === 'happy' ? ' selected' : '') + '>' + T('mood.happy') + '</option>' +
        '<option value="sad"' + (u.mood === 'sad' ? ' selected' : '') + '>' + T('mood.sad') + '</option>' +
        '<option value="neutral"' + (u.mood === 'neutral' ? ' selected' : '') + '>' + T('mood.neutral') + '</option>' +
        '</select></div>' +
        '<button class="btn btn-primary" id="save-interests">' + UI.icon('floppy-disk') + ' ' + T('settings.save') + '</button></div>';

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

      html += '<div class="settings-block"><h3>' + T('settings.notifications') + '</h3>' +
        '<div class="hint" style="margin-bottom:6px">' + T('settings.notificationsHint') + '</div>' +
        '<div class="sound-events">' + notifHtml + '</div>' +
        '<label class="check" style="margin-top:10px"><input type="checkbox" id="s-desktop"' + (desktopOn ? ' checked' : '') + '> <span>' + T('settings.desktopNotify') + '</span></label>' +
        '<div class="hint">' + T('settings.desktopHint') + '</div></div>';

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
      function saveProfile(btn) {
        if (btn) btn.disabled = true;
        Api.request('POST', '/api/profile', {
          displayName: App.val('s-name'),
          status: App.val('s-status'),
          bio: App.val('s-bio'),
          city: App.val('s-city'),
          country: App.val('s-country'),
          birthday: App.val('s-birthday'),
          gender: App.val('s-gender'),
          website: App.val('s-website'),
          email: App.val('s-email'),
          favGames: App.val('s-favgames'),
          favMovies: App.val('s-favmovies'),
          favMusic: App.val('s-favmusic'),
          favBooks: App.val('s-favbooks'),
          mood: App.val('s-mood')
        }, function (e2, d2) {
          if (btn) btn.disabled = false;
          if (e2) { UI.toast(Api.errorText(e2), 'err'); return; }
          state.user = d2.user;
          UI.toast(T('profile.saved'), 'ok');
          SoundFX.play('success');
          App.renderShell();
          App.route();
        });
      }
      var sp = document.getElementById('save-profile');
      if (sp) sp.addEventListener('click', function () { saveProfile(sp); });
      var sib = document.getElementById('save-interests');
      if (sib) sib.addEventListener('click', function () { saveProfile(sib); });

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

      // notification types: unchecked boxes are muted on the server
      var nts = document.querySelectorAll('[data-notify-type]');
      function saveNotifyPrefs() {
        var mute = [];
        for (var q = 0; q < nts.length; q++) {
          if (!nts[q].checked) mute.push(nts[q].getAttribute('data-notify-type'));
        }
        Api.request('POST', '/api/settings', { notifyMute: mute }, function (e2, d2) {
          if (e2) { UI.toast(Api.errorText(e2), 'err'); return; }
          if (d2 && d2.user) state.user = d2.user;
        });
      }
      for (var ni = 0; ni < nts.length; ni++) nts[ni].addEventListener('change', saveNotifyPrefs);

      var db = document.getElementById('s-desktop');
      if (db) db.addEventListener('change', function () {
        if (!db.checked) {
          try { localStorage.setItem('social.desktop', '0'); } catch (e) { }
          return;
        }
        if (!window.Notification) {
          db.checked = false;
          UI.toast(T('settings.desktopUnsupported'), 'err');
          return;
        }
        Notification.requestPermission(function (p) {
          if (p === 'granted') {
            try { localStorage.setItem('social.desktop', '1'); } catch (e) { }
            UI.toast(T('settings.desktopOn'), 'ok');
          } else {
            db.checked = false;
            UI.toast(T('settings.desktopDenied'), 'err');
          }
        });
      });

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
      adminTab('ai', 'robot', T('admin.ai'), sub) +
      adminTab('reports', 'flag', T('admin.reports'), sub) +
      adminTab('settings', 'sliders', T('admin.settings'), sub) +
      adminTab('logs', 'terminal', T('admin.logs'), sub) +
      '</div>';
    main.innerHTML = tabs + '<div id="admin-body">' + spinnerCard() + '</div>';
    var body = document.getElementById('admin-body');
    if (sub === 'users') adminUsers(body);
    else if (sub === 'ai') adminAi(body);
    else if (sub === 'settings') adminSettings(body);
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
    body.innerHTML = '<div class="card"><div class="card-h">' + UI.icon('users') + ' ' + T('admin.users') +
      '<span class="right"><button class="btn btn-xs" id="adm-badges-recalc">' + UI.icon('trophy') + ' ' + T('admin.badgesRecalc') + '</button></span></div><div class="card-b">' +
      '<form id="adm-user-search" style="display:flex;gap:7px;margin-bottom:10px">' +
      '<input class="input" id="adm-q" type="search" value="' + UI.attr(q) + '" placeholder="' + UI.attr(T('people.search')) + '">' +
      '<button class="btn btn-primary" type="submit">' + T('common.search') + '</button></form>' +
      '<div id="adm-users-list">' + UI.spinner() + '</div></div></div>';
    document.getElementById('adm-user-search').addEventListener('submit', function (e) {
      e.preventDefault();
      var v = document.getElementById('adm-q').value.trim();
      App.go('#/admin/users' + (v ? '?q=' + encodeURIComponent(v) : ''));
    });
    var recalc = document.getElementById('adm-badges-recalc');
    if (recalc) recalc.addEventListener('click', function () {
      recalc.disabled = true;
      Api.request('POST', '/api/admin/badges/recalc', {}, function (err, data) {
        recalc.disabled = false;
        if (err) { UI.toast(Api.errorText(err), 'err'); return; }
        UI.toast(T('admin.badgesRecalcDone', (data && data.granted) || 0), 'ok');
        SoundFX.play('success');
      });
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
    Api.request('GET', '/api/admin/user?id=' + encodeURIComponent(id), null, function (err, data) {
      if (err) { UI.toast(Api.errorText(err), 'err'); return; }
      var u = data.user || {};
      var canAdmin = data.canAdmin !== false;
      var personas = ['', 'friendly', 'troll', 'philosopher', 'gamer', 'musician', 'coder', 'doomer', 'hype'];

      var personaOpts = '';
      for (var i = 0; i < personas.length; i++) {
        var p = personas[i];
        personaOpts += '<option value="' + p + '"' + (u.persona === p ? ' selected' : '') + '>' +
          (p ? UI.esc(p) : T('admin.aiNone')) + '</option>';
      }
      var moodOpts = '';
      var moods = [['', T('admin.aiMoodNone')], ['happy', T('admin.aiHappy')], ['sad', T('admin.aiSad')], ['neutral', T('admin.aiNeutral')]];
      for (var m = 0; m < moods.length; m++) {
        moodOpts += '<option value="' + moods[m][0] + '"' + (u.mood === moods[m][0] ? ' selected' : '') + '>' + UI.esc(moods[m][1]) + '</option>';
      }

      var body = '';
      body += '<div style="display:flex;gap:10px;align-items:center;margin-bottom:12px">' +
        UI.avatar(u, 48) +
        '<div style="min-width:0"><div style="font-weight:700">' + UI.esc(u.displayName) + ' ' + UI.roleBadge(u.role) + '</div>' +
        '<div class="tiny muted">@' + UI.esc(u.username) + ' · #' + u.id + '</div>' +
        '<div class="tiny muted">' + T('admin.aiPosts') + ' ' + u.posts + ' · ' + T('profile.friends') + ' ' + u.friends +
        ' · ' + T('admin.sessions') + ' ' + (data.sessions || 0) + '</div></div></div>';

      if (u.banned) {
        body += '<div class="alert err">' + UI.icon('ban') + ' ' + UI.esc(u.banReason || T('admin.ban')) +
          (u.bannedUntil ? ' · ' + T('admin.until') + ' ' + UI.formatDate(u.bannedUntil, true) : '') + '</div>';
      }

      body += '<div class="field"><label>' + T('auth.displayName') + '</label><input class="input" id="au-name" maxlength="40" value="' + UI.attr(u.displayName) + '"></div>';
      body += '<div class="field"><label>' + T('settings.status') + '</label><input class="input" id="au-status" maxlength="140" value="' + UI.attr(u.status) + '"></div>';
      body += '<div class="field"><label>' + T('settings.email') + '</label><input class="input" id="au-email" value="' + UI.attr(u.email) + '"></div>';
      body += '<div class="inline-fields"><div class="field"><label>' + T('admin.aiPersona') + '</label><select class="select" id="au-persona">' + personaOpts + '</select></div>' +
        '<div class="field"><label>' + T('admin.aiMood') + '</label><select class="select" id="au-mood">' + moodOpts + '</select></div></div>';
      body += '<button class="btn btn-primary btn-sm" id="au-save">' + UI.icon('floppy-disk') + ' ' + T('common.save') + '</button>';

      if (canAdmin) {
        body += '<hr>';
        body += '<div class="field"><label>' + T('admin.role') + '</label><select class="select" id="au-role">' +
          '<option value="user"' + (u.role === 'user' ? ' selected' : '') + '>user</option>' +
          '<option value="moderator"' + (u.role === 'moderator' ? ' selected' : '') + '>moderator</option>' +
          '<option value="admin"' + (u.role === 'admin' ? ' selected' : '') + '>admin</option></select></div>';

        // quick role buttons; the policy decides which of them are legal
        var roleLocked = data.allowRoleChange === false;
        var selfBlocked = data.isSelf && data.allowSelfDemote === false;
        var lastBlocked = data.protectLastAdmin !== false && data.admins <= 1;
        var canGrantAdmin = !roleLocked && u.role !== 'admin';
        var canRevokeAdmin = !roleLocked && u.role === 'admin' && !selfBlocked && !lastBlocked;
        var canGrantMod = !roleLocked && u.role === 'user';
        var canRevokeMod = !roleLocked && u.role === 'moderator';

        body += '<div style="display:flex;gap:6px;flex-wrap:wrap;margin-bottom:10px">' +
          '<button class="btn btn-sm' + (canGrantAdmin ? ' btn-primary' : '') + '" id="au-grant-admin"' +
            (canGrantAdmin ? '' : ' disabled') + '>' + UI.icon('user-shield') + ' ' + T('admin.makeAdmin') + '</button>' +
          '<button class="btn btn-sm' + (canRevokeAdmin ? ' btn-danger' : '') + '" id="au-revoke-admin"' +
            (canRevokeAdmin ? '' : ' disabled') + '>' + UI.icon('user-slash') + ' ' + T('admin.revokeAdmin') + '</button>' +
          '<button class="btn btn-sm" id="au-grant-mod"' + (canGrantMod ? '' : ' disabled') + '>' + UI.icon('gavel') + ' ' + T('admin.makeMod') + '</button>' +
          '<button class="btn btn-sm" id="au-revoke-mod"' + (canRevokeMod ? '' : ' disabled') + '>' + UI.icon('gavel') + ' ' + T('admin.revokeMod') + '</button>' +
          '</div>';
        if (roleLocked) body += '<div class="hint">' + T('admin.roleLocked') + '</div>';
        else if (selfBlocked) body += '<div class="hint">' + T('admin.selfDemoteBlocked') + '</div>';
        else if (lastBlocked && u.role === 'admin') body += '<div class="hint">' + T('admin.lastAdminBlocked') + '</div>';

        body += '<div class="inline-fields"><div class="field"><label>' + T('admin.banReason') + '</label><input class="input" id="au-reason"></div>' +
          '<div class="field"><label>' + T('admin.days') + '</label><input class="input" id="au-days" type="number" min="0" value="7"></div></div>';
        body += '<div style="display:flex;gap:6px;flex-wrap:wrap">' +
          '<button class="btn btn-sm" id="au-role-btn"' + (selfBlocked ? ' disabled' : '') + '>' + UI.icon('user-shield') + ' ' + T('admin.setRole') + '</button>' +
          '<button class="btn btn-sm" id="au-warn">' + UI.icon('triangle-exclamation') + ' ' + T('admin.warn') + '</button>' +
          '<button class="btn btn-sm" id="au-reset">' + UI.icon('key') + ' ' + T('admin.reset') + '</button>' +
          (u.banned
            ? '<button class="btn btn-ok btn-sm" id="au-unban">' + UI.icon('unlock') + ' ' + T('admin.unban') + '</button>'
            : '<button class="btn btn-danger btn-sm" id="au-ban">' + UI.icon('ban') + ' ' + T('admin.ban') + '</button>') +
          '</div>';
        body += '<hr>';
        body += '<div style="display:flex;gap:6px;flex-wrap:wrap">' +
          '<button class="btn btn-danger btn-sm" id="au-purge">' + UI.icon('trash') + ' ' + T('admin.deleteForever') + '</button>' +
          '</div><div class="hint">' + T('admin.deleteForeverHint') + '</div>';

        // achievements: auto ones are granted by the server, manual ones only here
        var cat = (state.site && state.site.badges) || [];
        if (cat.length) {
          var owned = {};
          var parts = String(u.badges || '').split(',');
          for (var pi = 0; pi < parts.length; pi++) {
            var pid = parts[pi].replace(/^\s+|\s+$/g, '');
            if (pid) owned[pid] = true;
          }
          body += '<hr><div class="field"><label>' + UI.icon('trophy') + ' ' + T('admin.badges') + '</label><div class="badge-picker">';
          for (var bi = 0; bi < cat.length; bi++) {
            body += '<label class="check"><input type="checkbox" class="au-badge" value="' + UI.attr(cat[bi].id) + '"' +
              (owned[cat[bi].id] ? ' checked' : '') + '> <span>' + UI.icon(cat[bi].icon) + ' ' +
              UI.esc(T('badge.' + cat[bi].id)) +
              (cat[bi].manual ? ' <em class="tiny muted">' + T('admin.badgeManual') + '</em>' : '') +
              '</span></label>';
          }
          body += '</div></div>';
        }
      }

      var close = UI.modal({
        title: T('admin.editUser'),
        icon: 'user-pen',
        wide: true,
        body: body,
        actions: [{ label: T('common.close'), cls: 'btn-ghost' }]
      });

      var box = document.querySelector('.modal');
      if (!box) return;

      function send(action, extra, done) {
        var payload = { id: Number(u.id), action: action };
        if (extra) for (var k in extra) if (extra.hasOwnProperty(k)) payload[k] = extra[k];
        Api.request('POST', '/api/admin/user', payload, function (e2, d2) {
          if (e2) { UI.toast(Api.errorText(e2), 'err'); return; }
          if (done) { done(d2); return; }
          UI.toast(T('admin.saved'), 'ok');
          SoundFX.play('success');
          if (close) close();
          App.route();
        });
      }

      box.querySelector('#au-save').addEventListener('click', function () {
        send('edit', {
          displayName: App.val('au-name'),
          status: App.val('au-status'),
          email: App.val('au-email'),
          persona: App.val('au-persona'),
          mood: App.val('au-mood')
        });
      });

      if (!canAdmin) return;

      var roleBtn = box.querySelector('#au-role-btn');
      if (roleBtn) roleBtn.addEventListener('click', function () {
        send('role', { role: App.val('au-role') });
      });

      var ga = box.querySelector('#au-grant-admin');
      if (ga) ga.addEventListener('click', function () { send('grant-admin'); });

      var ra = box.querySelector('#au-revoke-admin');
      if (ra) ra.addEventListener('click', function () {
        UI.confirm(T('admin.revokeAdmin'), T('admin.confirmRevokeAdmin'), T('admin.revokeAdmin'), function () {
          send('revoke-admin');
        }, true);
      });

      var gm = box.querySelector('#au-grant-mod');
      if (gm) gm.addEventListener('click', function () { send('grant-mod'); });

      var rm = box.querySelector('#au-revoke-mod');
      if (rm) rm.addEventListener('click', function () { send('role', { role: 'user' }); });

      var warn = box.querySelector('#au-warn');
      if (warn) warn.addEventListener('click', function () {
        send('warn', { reason: App.val('au-reason') });
      });

      var reset = box.querySelector('#au-reset');
      if (reset) reset.addEventListener('click', function () {
        send('reset', {}, function (d2) {
          if (!d2 || !d2.recoveryCode) { UI.toast(T('admin.saved'), 'ok'); return; }
          UI.modal({
            title: T('admin.recovery'),
            icon: 'key',
            body: '<p class="muted">' + T('admin.recoveryHint') + '</p>' +
              '<div class="input mono" style="text-align:center;font-size:14px;user-select:all">' + UI.esc(d2.recoveryCode) + '</div>',
            actions: [{ label: T('common.close'), cls: 'btn-primary' }]
          });
        });
      });

      var ban = box.querySelector('#au-ban');
      if (ban) ban.addEventListener('click', function () {
        send('ban', { reason: App.val('au-reason'), days: Number(App.val('au-days')) || 0 });
      });

      var unban = box.querySelector('#au-unban');
      if (unban) unban.addEventListener('click', function () { send('unban'); });

      var purge = box.querySelector('#au-purge');
      if (purge) purge.addEventListener('click', function () {
        UI.confirm(T('admin.deleteForever'), T('admin.confirmPurge'), T('admin.deleteForever'), function () {
          send('purge', {}, function (d2) {
            UI.toast(T('admin.purged', (d2 && d2.records) || 0), 'ok');
            if (close) close();
            App.route();
          });
        }, true);
      });

      var badgeBoxes = box.querySelectorAll('.au-badge');
      for (var bbi = 0; bbi < badgeBoxes.length; bbi++) {
        badgeBoxes[bbi].addEventListener('change', function () {
          var input = this;
          input.disabled = true;
          Api.request('POST', '/api/admin/user', {
            id: Number(u.id), action: 'badge', badge: input.value, on: input.checked
          }, function (e2) {
            input.disabled = false;
            if (e2) { input.checked = !input.checked; UI.toast(Api.errorText(e2), 'err'); return; }
            UI.toast(T('admin.saved'), 'ok');
            SoundFX.play('success');
          });
        });
      }
    });
  }

  // ------------------------------------------------------------------- AI

  function adminAi(body) {
    Api.request('GET', '/api/admin/ai', null, function (err, data) {
      if (err) { body.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>'; return; }
      var c = data.config || {};
      var s = data.status || {};
      var personas = data.personas || [];
      var bots = data.bots || [];

      var personaOpts = function (selected) {
        var h = '<option value=""' + (!selected ? ' selected' : '') + '>' + T('admin.aiNone') + '</option>';
        for (var i = 0; i < personas.length; i++) {
          h += '<option value="' + UI.attr(personas[i]) + '"' + (selected === personas[i] ? ' selected' : '') + '>' + UI.esc(personas[i]) + '</option>';
        }
        return h;
      };
      var moodOpts = function (selected) {
        var moods = [['', T('admin.aiMoodNone')], ['happy', T('admin.aiHappy')], ['sad', T('admin.aiSad')], ['neutral', T('admin.aiNeutral')]];
        var h = '';
        for (var i = 0; i < moods.length; i++) {
          h += '<option value="' + moods[i][0] + '"' + (selected === moods[i][0] ? ' selected' : '') + '>' + UI.esc(moods[i][1]) + '</option>';
        }
        return h;
      };

      var html = '';

      html += '<div class="admin-grid">' +
        stat('robot', s.bots, T('admin.aiBots'), '') +
        stat('wand-magic-sparkles', s.actions, T('admin.aiActions'), '') +
        stat('pen', s.postsLastHour, T('admin.aiPostsHour'), '') +
        stat('comment', s.commentsLastHour, T('admin.aiCommentsHour'), '') +
        stat('heart', s.likesLastHour, T('admin.aiLikesHour'), '') +
        stat('triangle-exclamation', s.failed, T('admin.aiFailed'), s.failed > 0 ? 'warn' : 'ok') +
        '</div>';

      html += '<div class="card"><div class="card-h">' + UI.icon('robot') + ' ' + T('admin.aiTitle') +
        '<span class="right">' + (s.ready ? '<span class="badge online">' + UI.esc(s.providerLabel) + '</span>'
          : '<span class="badge err">' + UI.esc(s.providerLabel) + '</span>') + '</span></div>' +
        '<div class="card-b">' +
        '<div class="tiny muted" style="margin-bottom:8px">' + T('admin.aiLastAction') + ': ' +
        (s.lastAction ? UI.esc(s.lastAction) + ' · ' + UI.timeAgo(s.lastActionAt) : T('common.none')) + '</div>' +
        (s.lastError ? '<div class="alert err" style="margin-bottom:8px">' + UI.esc(s.lastError) + '</div>' : '') +
        '<div class="tiny muted">' + T('admin.aiImported') + ': ' + s.imported + ' · ' + T('admin.aiNext') + ': ' +
        (s.nextRun ? UI.timeAgo(s.nextRun) : '—') + ' · ' + T('admin.aiChain') + ': ' + s.chainPosts + ' ' + T('admin.aiPosts') + '</div>' +
        '</div></div>';

      html += '<div class="card"><div class="card-h">' + UI.icon('sliders') + ' ' + T('admin.aiConfig') + '</div><div class="card-b">';
      html += '<div class="settings-grid">';

      html += '<div class="settings-block"><h3>' + T('admin.aiProvider') + '</h3>' +
        '<div class="field"><label>' + T('admin.aiProvider') + '</label><select class="select" id="ai-provider">' +
        '<option value="off"' + (c.provider === 'off' ? ' selected' : '') + '>' + T('admin.aiOff') + '</option>' +
        '<option value="markov"' + (c.provider === 'markov' ? ' selected' : '') + '>' + T('admin.aiMarkov') + '</option>' +
        '<option value="openai"' + (c.provider === 'openai' ? ' selected' : '') + '>' + T('admin.aiOpenAI') + '</option>' +
        '</select></div>' +
        '<label class="check" style="margin-bottom:8px"><input type="checkbox" id="ai-enabled"' + (c.enabled ? ' checked' : '') + '> <span>' + T('admin.aiEnabled') + '</span></label>' +
        '<div class="field"><label>' + T('admin.aiEndpoint') + '</label><input class="input" id="ai-endpoint" value="' + UI.attr(c.endpoint) + '" placeholder="http://localhost:1234/v1/chat/completions"></div>' +
        '<div class="field"><label>' + T('admin.aiModel') + '</label><input class="input" id="ai-model" value="' + UI.attr(c.model) + '"></div>' +
        '<div class="field"><label>' + T('admin.aiKey') + '</label><input class="input" id="ai-key" type="password" placeholder="' + UI.attr(c.hasKey ? T('admin.aiKeySet') : '') + '" autocomplete="off"></div>' +
        '<div class="hint">' + T('admin.aiKeyHint') + '</div>' +
        '</div>';

      html += '<div class="settings-block"><h3>' + T('admin.aiBehaviour') + '</h3>' +
        '<div class="inline-fields">' +
        '<div class="field"><label>' + T('admin.aiInterval') + '</label><input class="input" id="ai-interval" type="number" min="30" value="' + c.intervalSec + '"></div>' +
        '<div class="field"><label>' + T('admin.aiMaxTokens') + '</label><input class="input" id="ai-tokens" type="number" min="16" value="' + c.maxTokens + '"></div></div>' +
        '<div class="inline-fields">' +
        '<div class="field"><label>' + T('admin.aiTemp') + '</label><input class="input" id="ai-temp" type="number" step="0.05" min="0" max="2" value="' + c.temperature + '"></div>' +
        '<div class="field"><label>' + T('admin.aiHumanTarget') + '</label><input class="input" id="ai-human" type="number" min="0" max="100" value="' + c.humanTargetPercent + '"></div></div>' +
        '<div class="sound-events" style="margin:6px 0 10px">' +
        '<label class="check"><input type="checkbox" id="ai-posts"' + (c.allowPosts ? ' checked' : '') + '> <span>' + T('admin.aiAllowPosts') + '</span></label>' +
        '<label class="check"><input type="checkbox" id="ai-comments"' + (c.allowComments ? ' checked' : '') + '> <span>' + T('admin.aiAllowComments') + '</span></label>' +
        '<label class="check"><input type="checkbox" id="ai-likes"' + (c.allowLikes ? ' checked' : '') + '> <span>' + T('admin.aiAllowLikes') + '</span></label>' +
        '<label class="check"><input type="checkbox" id="ai-messages"' + (c.allowMessages ? ' checked' : '') + '> <span>' + T('admin.aiAllowMessages') + '</span></label>' +
        '</div>' +
        '<div class="inline-fields">' +
        '<div class="field"><label>' + T('admin.aiPostsHour') + '</label><input class="input" id="ai-cap-posts" type="number" min="0" value="' + c.maxPostsPerHour + '"></div>' +
        '<div class="field"><label>' + T('admin.aiCommentsHour') + '</label><input class="input" id="ai-cap-comments" type="number" min="0" value="' + c.maxCommentsPerHour + '"></div></div>' +
        '<div class="inline-fields">' +
        '<div class="field"><label>' + T('admin.aiLikesHour') + '</label><input class="input" id="ai-cap-likes" type="number" min="0" value="' + c.maxLikesPerHour + '"></div>' +
        '<div class="field"><label>' + T('admin.aiMessagesHour') + '</label><input class="input" id="ai-cap-messages" type="number" min="0" value="' + c.maxMessagesPerHour + '"></div></div>' +
        '</div>';
      html += '</div>';

      html += '<div style="display:flex;gap:6px;flex-wrap:wrap;margin-top:10px">' +
        '<button class="btn btn-primary" id="ai-save">' + UI.icon('floppy-disk') + ' ' + T('common.save') + '</button>' +
        '<button class="btn" id="ai-test">' + UI.icon('plug-circle-check') + ' ' + T('admin.aiTest') + '</button>' +
        '<button class="btn" id="ai-step">' + UI.icon('forward-step') + ' ' + T('admin.aiStep') + '</button>' +
        '</div>';
      html += '</div></div>';

      html += '<div class="card"><div class="card-h">' + UI.icon('users-gear') + ' ' + T('admin.aiBots') +
        '<span class="right tiny muted">' + bots.length + '</span></div><div class="card-b flush">' +
        '<div class="table-wrap" style="max-height:520px"><table class="table"><thead><tr>' +
        '<th>' + T('auth.username') + '</th><th>' + T('admin.aiPersona') + '</th><th>' + T('admin.aiMood') + '</th>' +
        '<th>' + T('admin.aiStatusCol') + '</th><th></th></tr></thead><tbody>';
      if (!bots.length) {
        html += '<tr><td colspan="5"><div class="empty" style="padding:20px">' + UI.icon('robot') +
          '<b>' + T('admin.aiNoBots') + '</b><span>' + T('admin.aiNoBotsHint') + '</span></div></td></tr>';
      }
      for (var i = 0; i < bots.length; i++) {
        var b = bots[i];
        html += '<tr><td><div class="user-cell">' + UI.avatar(b, 22) +
          '<a href="#/u/' + encodeURIComponent(b.username) + '">' + UI.esc(b.displayName) + '</a></div></td>' +
          '<td><select class="select ai-bot-persona" data-id="' + b.id + '" style="padding:2px 20px 2px 6px;font-size:11px">' + personaOpts(b.persona) + '</select></td>' +
          '<td><select class="select ai-bot-mood" data-id="' + b.id + '" style="padding:2px 20px 2px 6px;font-size:11px">' + moodOpts(b.mood) + '</select></td>' +
          '<td><input class="input ai-bot-status" data-id="' + b.id + '" value="' + UI.attr(b.status) + '" style="font-size:11px;padding:3px 6px"></td>' +
          '<td><button class="btn btn-xs ai-bot-save" data-id="' + b.id + '">' + T('common.save') + '</button></td></tr>';
      }
      html += '</tbody></table></div></div></div>';

      body.innerHTML = html;

      var save = document.getElementById('ai-save');
      if (save) save.addEventListener('click', function () {
        var patch = {
          provider: App.val('ai-provider'),
          enabled: document.getElementById('ai-enabled').checked,
          endpoint: App.val('ai-endpoint'),
          model: App.val('ai-model'),
          intervalSec: Number(App.val('ai-interval')) || 180,
          maxTokens: Number(App.val('ai-tokens')) || 1200,
          temperature: Number(App.val('ai-temp')),
          humanTargetPercent: Number(App.val('ai-human')),
          allowPosts: document.getElementById('ai-posts').checked,
          allowComments: document.getElementById('ai-comments').checked,
          allowLikes: document.getElementById('ai-likes').checked,
          allowMessages: document.getElementById('ai-messages').checked,
          maxPostsPerHour: Number(App.val('ai-cap-posts')),
          maxCommentsPerHour: Number(App.val('ai-cap-comments')),
          maxLikesPerHour: Number(App.val('ai-cap-likes')),
          maxMessagesPerHour: Number(App.val('ai-cap-messages'))
        };
        var key = App.val('ai-key');
        if (key) patch.apiKey = key;
        save.disabled = true;
        Api.request('POST', '/api/admin/ai/config', patch, function (e2) {
          save.disabled = false;
          if (e2) { UI.toast(Api.errorText(e2), 'err'); return; }
          UI.toast(T('settings.saved'), 'ok');
          SoundFX.play('success');
          App.route();
        });
      });

      var test = document.getElementById('ai-test');
      if (test) test.addEventListener('click', function () {
        test.disabled = true;
        test.innerHTML = UI.spinner() + ' ' + T('admin.aiTesting');
        Api.request('POST', '/api/admin/ai/test', {}, function (e2, d2) {
          test.disabled = false;
          test.innerHTML = UI.icon('plug-circle-check') + ' ' + T('admin.aiTest');
          if (e2) { UI.toast(T('admin.aiTestFail') + ': ' + (e2.message || Api.errorText(e2)), 'err'); return; }
          UI.toast(T('admin.aiTestOk') + ' · ' + d2.latencyMs + 'ms', 'ok');
          SoundFX.play('success');
        });
      });

      var step = document.getElementById('ai-step');
      if (step) step.addEventListener('click', function () {
        step.disabled = true;
        step.innerHTML = UI.spinner() + ' ' + T('admin.aiStepping');
        Api.request('POST', '/api/admin/ai/step', {}, function (e2, d2) {
          step.disabled = false;
          step.innerHTML = UI.icon('forward-step') + ' ' + T('admin.aiStep');
          if (e2) { UI.toast(Api.errorText(e2), 'err'); return; }
          UI.toast(d2.result, 'info');
          App.route();
        });
      });

      var saves = body.querySelectorAll('.ai-bot-save');
      for (var k = 0; k < saves.length; k++) {
        saves[k].addEventListener('click', function () {
          var id = this.getAttribute('data-id');
          var persona = body.querySelector('.ai-bot-persona[data-id="' + id + '"]');
          var mood = body.querySelector('.ai-bot-mood[data-id="' + id + '"]');
          var status = body.querySelector('.ai-bot-status[data-id="' + id + '"]');
          var btn = this;
          btn.disabled = true;
          Api.request('POST', '/api/admin/ai/persona', {
            id: Number(id),
            persona: persona ? persona.value : '',
            mood: mood ? mood.value : '',
            status: status ? status.value : ''
          }, function (e2) {
            btn.disabled = false;
            if (e2) { UI.toast(Api.errorText(e2), 'err'); return; }
            UI.toast(T('settings.saved'), 'ok');
          });
        });
      }
    });
  }

  // -------------------------------------------------------------- settings

  function adminSettings(body) {
    Api.request('GET', '/api/admin/settings', null, function (err, data) {
      if (err) { body.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>'; return; }
      var s = data.site || {};
      var sec = data.security || {};
      var lim = data.limits || {};
      var srv = data.server || {};
      var ui = data.ui || {};
      var lg = data.logging || {};
      var st = data.storage || {};

      var field = function (id, label, value, type, extra) {
        return '<div class="field"><label>' + UI.esc(label) + '</label>' +
          '<input class="input" id="' + id + '" type="' + (type || 'text') + '" value="' + UI.attr(value) + '"' +
          (extra || '') + '></div>';
      };
      var check = function (id, label, on) {
        return '<label class="check" style="margin-bottom:6px"><input type="checkbox" id="' + id + '"' +
          (on ? ' checked' : '') + '> <span>' + UI.esc(label) + '</span></label>';
      };
      var select = function (id, label, options, selected) {
        var h = '<div class="field"><label>' + UI.esc(label) + '</label><select class="select" id="' + id + '">';
        for (var i = 0; i < options.length; i++) {
          var o = options[i];
          h += '<option value="' + UI.attr(o[0]) + '"' + (selected === o[0] ? ' selected' : '') + '>' + UI.esc(o[1]) + '</option>';
        }
        return h + '</select></div>';
      };

      var html = '<div class="card"><div class="card-h">' + UI.icon('sliders') + ' ' + T('admin.settings') +
        '<span class="right tiny muted">' + UI.esc(data.configPath || '') + '</span></div><div class="card-b">';
      html += '<div class="settings-grid">';

      // site
      html += '<div class="settings-block"><h3>' + T('admin.settingsSite') + '</h3>' +
        field('cfg-name', T('admin.siteName'), s.name) +
        field('cfg-short', T('admin.shortName'), s.shortName, 'text', ' maxlength="3"') +
        field('cfg-tagline', T('admin.tagline'), s.tagline) +
        field('cfg-desc', T('admin.description'), s.description) +
        field('cfg-base', T('admin.baseUrl'), s.baseUrl) +
        field('cfg-footer', T('admin.footer'), s.footer) +
        field('cfg-support', T('admin.supportEmail'), s.supportEmail, 'email') +
        '<div class="field"><label>' + T('admin.motd') + '</label><textarea class="textarea" id="cfg-motd" rows="3">' + UI.esc(s.motd) + '</textarea></div>' +
        select('cfg-lang', T('settings.language'), [['ru', 'Русский'], ['en', 'English'], ['uk', 'Українська'], ['lt', 'Lietuvių']], s.defaultLanguage) +
        select('cfg-theme', T('settings.theme'), [['light', T('theme.light')], ['dark', T('theme.dark')], ['oled', T('theme.oled')]], s.defaultTheme) +
        check('cfg-registration', T('admin.registrationOpen'), s.registration) +
        check('cfg-invite', T('admin.inviteOnly'), s.inviteOnly) +
        check('cfg-firstadmin', T('admin.firstUserAdmin'), s.firstUserAdmin) +
        '</div>';

      // security
      html += '<div class="settings-block"><h3>' + T('admin.settingsSecurity') + '</h3>' +
        check('cfg-captcha', T('admin.captcha'), sec.requireCaptcha) +
        check('cfg-encrypt', T('admin.encryption'), sec.encryptApi) +
        check('cfg-adminprivate', T('admin.adminPrivateOnly'), sec.adminPrivateOnly) +
        check('cfg-hsts', T('admin.hsts'), sec.hsts) +
        check('cfg-external', T('admin.externalMedia'), sec.externalMedia) +
        '<hr>' +
        '<div class="tiny muted" style="margin-bottom:6px">' + T('admin.rolesHint') + '</div>' +
        check('cfg-rolechange', T('admin.allowRoleChange'), sec.allowRoleChange) +
        check('cfg-selfdemote', T('admin.allowSelfDemote'), sec.allowSelfDemote) +
        check('cfg-selfdelete', T('admin.allowSelfDelete'), sec.allowSelfDelete) +
        check('cfg-lastadmin', T('admin.protectLastAdmin'), sec.protectLastAdmin) +
        '<hr>' +
        '<div class="inline-fields">' +
        field('cfg-session', T('admin.sessionHours'), sec.sessionHours, 'number', ' min="1"') +
        field('cfg-failed', T('admin.maxFailedLogins'), sec.maxFailedLoginsPerIp, 'number', ' min="3"') +
        '</div>' +
        field('cfg-lock', T('admin.loginLockSeconds'), sec.loginLockSeconds, 'number', ' min="10"') +
        '</div>';

      // limits
      html += '<div class="settings-block"><h3>' + T('admin.settingsLimits') + '</h3>' +
        '<div class="inline-fields">' +
        field('cfg-rpm', T('admin.requestsPerMinute'), lim.requestsPerMinute, 'number', ' min="10"') +
        field('cfg-regph', T('admin.registrationsPerHour'), lim.registrationsPerHour, 'number', ' min="1"') +
        '</div><div class="inline-fields">' +
        field('cfg-postspm', T('admin.postsPer10Min'), lim.postsPer10Min, 'number', ' min="1"') +
        field('cfg-commentspm', T('admin.commentsPer10Min'), lim.commentsPer10Min, 'number', ' min="1"') +
        '</div>' +
        field('cfg-msgpm', T('admin.messagesPerMin'), lim.messagesPerMin, 'number', ' min="1"') +
        '<div class="inline-fields">' +
        field('cfg-postlen', T('admin.maxPostLength'), lim.maxPostLength, 'number', ' min="200"') +
        field('cfg-commentlen', T('admin.maxCommentLength'), lim.maxCommentLength, 'number', ' min="100"') +
        '</div><div class="inline-fields">' +
        field('cfg-msglen', T('admin.maxMessageLength'), lim.maxMessageLength, 'number', ' min="100"') +
        field('cfg-avatarkb', T('admin.maxAvatarKB'), lim.maxAvatarKB, 'number', ' min="16"') +
        '</div>' +
        field('cfg-imagekb', T('admin.maxPostImageKB'), lim.maxPostImageKB, 'number', ' min="64"') +
        field('cfg-msgimagekb', T('admin.maxMessageImageKB'), lim.maxMessageImageKB, 'number', ' min="64"') +
        field('cfg-editwin', T('admin.editWindow'), lim.editWindowMin, 'number', ' min="0"') +
        '</div>';

      // ui + logging + storage
      html += '<div class="settings-block"><h3>' + T('admin.settingsUi') + '</h3>' +
        '<div class="inline-fields">' +
        field('cfg-refresh', T('admin.refreshMs'), ui.refreshMs, 'number', ' min="1000"') +
        field('cfg-online', T('admin.onlineWindow'), ui.onlineWindowSec, 'number', ' min="30"') +
        '</div>' +
        field('cfg-themes', T('admin.allowThemes'), ui.allowThemes) +
        check('cfg-sounds', T('admin.allowSounds'), ui.allowSounds) +
        check('cfg-live', T('admin.liveNotifications'), ui.liveNotifications) +
        field('cfg-notifywait', T('admin.notifyWait'), ui.notifyWaitSec, 'number', ' min="1" max="25"') +
        '<div style="margin-top:6px"><button class="btn btn-sm" id="cfg-notify-test">' + UI.icon('bell') + ' ' + T('admin.notifyTest') + '</button></div>' +
        '<hr><h3 style="margin-top:10px">' + T('admin.settingsLogging') + '</h3>' +
        select('cfg-loglevel', T('admin.logLevel'), [['Error', 'Error'], ['Warn', 'Warn'], ['Info', 'Info'], ['Debug', 'Debug'], ['Verbose', 'Verbose']], lg.level) +
        check('cfg-apitrace', T('admin.apiTrace'), lg.apiTrace) +
        '<div class="inline-fields">' +
        field('cfg-logmb', T('admin.logMaxFileMB'), lg.maxFileMB, 'number', ' min="1"') +
        field('cfg-logdays', T('admin.logRetention'), lg.retentionDays, 'number', ' min="0"') +
        '</div>' +
        '<hr><h3 style="margin-top:10px">' + T('admin.settingsStorage') + '</h3>' +
        '<div class="inline-fields">' +
        field('cfg-backupmin', T('admin.backupInterval'), st.backupIntervalMin, 'number', ' min="0"') +
        field('cfg-backupkeep', T('admin.backupKeep'), st.backupKeep, 'number', ' min="1"') +
        '</div>' +
        field('cfg-debounce', T('admin.saveDebounce'), st.saveDebounceMs, 'number', ' min="100"') +
        check('cfg-readonly', T('admin.readOnlyOnError'), st.readOnlyOnError) +
        '</div>';

      // server (read only info + cookie + base path)
      html += '<div class="settings-block"><h3>' + T('admin.settingsServer') + '</h3>' +
        '<div class="tiny muted" style="margin-bottom:8px">' + T('admin.serverReadOnly') + '</div>' +
        '<div class="info-table" style="font-size:12px;margin-bottom:10px">' +
        '<div style="display:flex;justify-content:space-between;padding:3px 0"><span class="muted">' + T('admin.port') + '</span><b>' + srv.port + '</b></div>' +
        '<div style="display:flex;justify-content:space-between;padding:3px 0"><span class="muted">' + T('admin.adminPort') + '</span><b>' + srv.adminPort + '</b></div>' +
        '<div style="display:flex;justify-content:space-between;padding:3px 0"><span class="muted">' + T('admin.bind') + '</span><b>' + UI.esc(srv.bind) + '</b></div>' +
        '</div>' +
        field('cfg-basepath', T('admin.publicBasePath'), srv.publicBasePath) +
        select('cfg-cookie', T('admin.cookieSecure'), [['auto', T('admin.cookieAuto')], ['true', T('common.on')], ['false', T('common.off')]], srv.cookieSecure) +
        '</div>';

      html += '</div>';

      html += '<div style="display:flex;gap:6px;flex-wrap:wrap;margin-top:12px">' +
        '<button class="btn btn-primary" id="cfg-save">' + UI.icon('floppy-disk') + ' ' + T('common.save') + '</button>' +
        '<button class="btn" id="cfg-reload">' + UI.icon('rotate') + ' ' + T('admin.reload') + '</button>' +
        '<button class="btn" id="cfg-backup">' + UI.icon('box-archive') + ' ' + T('admin.backup') + '</button>' +
        '</div>';
      html += '</div></div>';

      body.innerHTML = html;

      var save = document.getElementById('cfg-save');
      save.addEventListener('click', function () {
        var payload = {
          site: {
            name: App.val('cfg-name'), shortName: App.val('cfg-short'),
            tagline: App.val('cfg-tagline'), description: App.val('cfg-desc'),
            baseUrl: App.val('cfg-base'), footer: App.val('cfg-footer'),
            supportEmail: App.val('cfg-support'), motd: App.val('cfg-motd'),
            defaultLanguage: App.val('cfg-lang'), defaultTheme: App.val('cfg-theme'),
            registration: document.getElementById('cfg-registration').checked,
            inviteOnly: document.getElementById('cfg-invite').checked,
            firstUserAdmin: document.getElementById('cfg-firstadmin').checked
          },
          security: {
            requireCaptcha: document.getElementById('cfg-captcha').checked,
            encryptApi: document.getElementById('cfg-encrypt').checked,
            adminPrivateOnly: document.getElementById('cfg-adminprivate').checked,
            hsts: document.getElementById('cfg-hsts').checked,
            externalMedia: document.getElementById('cfg-external').checked,
            allowRoleChange: document.getElementById('cfg-rolechange').checked,
            allowSelfDemote: document.getElementById('cfg-selfdemote').checked,
            allowSelfDelete: document.getElementById('cfg-selfdelete').checked,
            protectLastAdmin: document.getElementById('cfg-lastadmin').checked,
            sessionHours: Number(App.val('cfg-session')),
            maxFailedLoginsPerIp: Number(App.val('cfg-failed')),
            loginLockSeconds: Number(App.val('cfg-lock'))
          },
          limits: {
            requestsPerMinute: Number(App.val('cfg-rpm')),
            registrationsPerHour: Number(App.val('cfg-regph')),
            postsPer10Min: Number(App.val('cfg-postspm')),
            commentsPer10Min: Number(App.val('cfg-commentspm')),
            messagesPerMin: Number(App.val('cfg-msgpm')),
            maxPostLength: Number(App.val('cfg-postlen')),
            maxCommentLength: Number(App.val('cfg-commentlen')),
            maxMessageLength: Number(App.val('cfg-msglen')),
            maxMessageImageKB: Number(App.val('cfg-msgimagekb')),
            maxAvatarKB: Number(App.val('cfg-avatarkb')),
            maxPostImageKB: Number(App.val('cfg-imagekb')),
            editWindowMin: Number(App.val('cfg-editwin'))
          },
          ui: {
            refreshMs: Number(App.val('cfg-refresh')),
            onlineWindowSec: Number(App.val('cfg-online')),
            allowThemes: App.val('cfg-themes'),
            allowSounds: document.getElementById('cfg-sounds').checked,
            liveNotifications: document.getElementById('cfg-live').checked,
            notifyWaitSec: Number(App.val('cfg-notifywait'))
          },
          logging: {
            level: App.val('cfg-loglevel'),
            apiTrace: document.getElementById('cfg-apitrace').checked,
            maxFileMB: Number(App.val('cfg-logmb')),
            retentionDays: Number(App.val('cfg-logdays'))
          },
          storage: {
            backupIntervalMin: Number(App.val('cfg-backupmin')),
            backupKeep: Number(App.val('cfg-backupkeep')),
            saveDebounceMs: Number(App.val('cfg-debounce')),
            readOnlyOnError: document.getElementById('cfg-readonly').checked
          },
          server: {
            publicBasePath: App.val('cfg-basepath'),
            cookieSecure: App.val('cfg-cookie')
          }
        };
        save.disabled = true;
        Api.request('POST', '/api/admin/settings/save', payload, function (e2, d2) {
          save.disabled = false;
          if (e2) { UI.toast(Api.errorText(e2), 'err'); return; }
          UI.toast(T('settings.saved'), 'ok');
          SoundFX.play('success');
          if (d2 && d2.warnings && d2.warnings.length) {
            UI.modal({
              title: T('admin.settings'),
              icon: 'circle-info',
              body: '<ul style="margin:0;padding-left:18px;font-size:12.5px">' +
                d2.warnings.map(function (w) { return '<li>' + UI.esc(w) + '</li>'; }).join('') + '</ul>',
              actions: [{ label: T('common.close'), cls: 'btn-primary' }]
            });
          }
          App.renderShell();
          App.route();
        });
      });

      document.getElementById('cfg-reload').addEventListener('click', function () {
        Api.request('POST', '/api/admin/reload', {}, function (e2) {
          if (e2) { UI.toast(Api.errorText(e2), 'err'); return; }
          UI.toast(T('admin.reloaded'), 'ok');
          App.route();
        });
      });

      document.getElementById('cfg-backup').addEventListener('click', function () {
        Api.request('POST', '/api/admin/backup', {}, function (e2, d2) {
          if (e2) { UI.toast(Api.errorText(e2), 'err'); return; }
          UI.toast(T('admin.backupDone') + ': ' + d2.path, 'ok');
        });
      });

      var ntBtn = document.getElementById('cfg-notify-test');
      if (ntBtn) ntBtn.addEventListener('click', function () {
        ntBtn.disabled = true;
        Api.request('POST', '/api/admin/notify-test', {}, function (e2) {
          ntBtn.disabled = false;
          if (e2) { UI.toast(Api.errorText(e2), 'err'); return; }
          UI.toast(T('admin.notifyTestSent'), 'ok');
        });
      });
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
