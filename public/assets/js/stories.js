/* =========================================================================
   StoriesUI - the circles above the feed and the full-screen viewer.

   A story is a photo or a short clip with an optional caption, and it expires
   after the configured number of hours (24 by default). The bar shows one
   circle per author with a ring that goes grey once everything is watched; the
   viewer walks through an author's stories with progress bars at the top, and
   the author can open the "seen by" list on their own story.
   ========================================================================= */
var StoriesUI = (function () {
  'use strict';

  var groups = [];          // [{ user, mine, unseen, count, stories: [] }]
  var flat = [];            // [{ group, index }] in viewing order
  var pos = -1;             // current position in flat
  var timer = null;
  var openedAt = 0;

  function T(k, a, b) { return I18N.t(k, a, b); }
  function byId(id) { return document.getElementById(id); }

  function enabled() {
    return !(App.state.site && App.state.site.storiesEnabled === false);
  }

  // ------------------------------------------------------------------ tray

  /// <summary>Renders the bar into #stories-tray; safe to call when it is missing.</summary>
  function render() {
    var box = byId('stories-tray');
    if (!box) return;
    if (!enabled()) { box.innerHTML = ''; return; }

    Api.request('GET', '/api/stories', null, function (err, data) {
      if (!box.parentNode) return;
      if (err) { box.innerHTML = ''; return; }
      groups = (data && data.groups) || [];
      var canPost = !data || data.canPost !== false;

      // own stories: one tile that both plays them and offers "+"
      var own = null;
      for (var o = 0; o < groups.length; o++) if (groups[o].mine) own = groups[o];

      var html = '<div class="stories-bar">';
      if (canPost || own) {
        html += '<button type="button" class="story-add' + (own ? ' has-story' : '') +
          (own && own.unseen ? ' unseen' : '') + '" id="story-add" title="' +
          UI.attr(own ? T('stories.your') : T('stories.add')) + '">' +
          '<span class="story-ring">' + UI.avatar(App.state.user, 54) + '</span>' +
          '<span class="story-add-badge" id="story-add-badge">' + UI.icon('plus') + '</span>' +
          '<span class="story-add-name">' + (own ? T('stories.you') : T('stories.your')) + '</span></button>';
      }
      for (var i = 0; i < groups.length; i++) {
        var g = groups[i];
        if (g.mine) continue;
        var u = g.user || {};
        html += '<button type="button" class="story-circle' + (g.unseen ? ' unseen' : '') +
          '" data-author="' + (u.id || 0) + '">' +
          '<span class="story-ring">' + UI.avatar(u, 54) + '</span>' +
          '<span class="story-name">' + UI.esc(u.displayName || u.username || '?') + '</span>' +
          '</button>';
      }
      html += '</div>';
      box.innerHTML = html;

      var add = byId('story-add');
      if (add) add.addEventListener('click', function () {
        if (own) openFor(Number((App.state.user || {}).id));
        else pickAndPost();
      });
      var badge = byId('story-add-badge');
      if (badge) badge.addEventListener('click', function (e) {
        e.stopPropagation();
        pickAndPost();
      });

      var circles = box.querySelectorAll('.story-circle');
      for (var c = 0; c < circles.length; c++) {
        circles[c].addEventListener('click', function () {
          openFor(Number(this.getAttribute('data-author')));
        });
      }
    });
  }

  // ---------------------------------------------------------------- upload

  function pickAndPost() {
    var maxPhoto = Number((App.state.site || {}).maxStoryKB) || 4096;
    var maxClip = Number((App.state.site || {}).maxStoryVideoKB) || 8192;
    var maxCaption = Number((App.state.site || {}).maxStoryCaption) || 200;

    var body = '<div class="field"><label>' + T('stories.file') + '</label>' +
      '<input class="input" type="file" id="story-file" accept="image/png,image/jpeg,image/gif,image/webp,video/mp4,video/webm">' +
      '<div class="hint">' + T('stories.fileHint', String(maxPhoto), String(maxClip)) + '</div></div>' +
      '<div class="field"><label>' + T('stories.caption') + '</label>' +
      '<input class="input" id="story-caption" maxlength="' + maxCaption + '" placeholder="' +
      UI.attr(T('stories.captionHint')) + '"></div>' +
      '<div id="story-preview" class="story-preview"></div>';

    UI.modal({
      title: T('stories.add'),
      icon: 'circle-plus',
      body: body,
      actions: [
        { label: T('common.cancel'), cls: 'btn-ghost' },
        {
          label: T('stories.publish'), cls: 'btn-primary', icon: 'paper-plane', keepOpen: true,
          onClick: function (box, close) {
            var input = byId('story-file');
            var file = input && input.files && input.files[0];
            if (!file) { UI.toast(T('stories.pickFile'), 'err'); return false; }
            var isVideo = /^video\//.test(file.type);
            var limit = isVideo ? maxClip : maxPhoto;
            if (file.size > limit * 1024) {
              UI.toast(T('stories.tooBig', String(limit)), 'err');
              return false;
            }
            var caption = (byId('story-caption') || {}).value || '';

            var reader = new FileReader();
            reader.onload = function () {
              var payload = { caption: caption };
              payload[isVideo ? 'video' : 'image'] = reader.result;
              Api.request('POST', '/api/story/add', payload, function (err, data) {
                if (err) { UI.toast(Api.errorText(err), 'err'); return; }
                if (close) close();
                UI.toast(T('stories.published'), 'ok');
                SoundFX.play('success');
                render();
                openFor(Number((App.state.user || {}).id), data && data.story ? data.story.id : 0);
              });
            };
            reader.onerror = function () { UI.toast(T('stories.readFail'), 'err'); };
            reader.readAsDataURL(file);
            return false;
          }
        }
      ]
    });

    var file = byId('story-file');
    if (file) file.addEventListener('change', function () {
      var f = file.files && file.files[0];
      var preview = byId('story-preview');
      if (!f || !preview) return;
      var url = URL.createObjectURL(f);
      preview.innerHTML = /^video\//.test(f.type)
        ? '<video src="' + UI.attr(url) + '" muted playsinline></video>'
        : '<img src="' + UI.attr(url) + '" alt="">';
    });
  }

  // ---------------------------------------------------------------- viewer

  function openFor(authorId, storyId) {
    var start = -1;
    flat = [];
    for (var i = 0; i < groups.length; i++) {
      var g = groups[i];
      if (Number((g.user || {}).id) !== Number(authorId)) continue;
      for (var k = 0; k < g.stories.length; k++) {
        flat.push({ group: g, story: g.stories[k] });
        if (storyId && Number(g.stories[k].id) === Number(storyId)) start = flat.length - 1;
      }
      if (start < 0) start = flat.length - g.stories.length;
      break;
    }
    if (!flat.length) { UI.toast(T('stories.empty'), 'info'); return; }
    pos = start < 0 ? 0 : start;
    ensureOverlay();
    show();
  }

  function ensureOverlay() {
    if (byId('story-overlay')) return;
    var o = document.createElement('div');
    o.id = 'story-overlay';
    o.className = 'story-overlay';
    o.innerHTML =
      '<div class="story-frame">' +
      '<div class="story-progress" id="story-progress"></div>' +
      '<div class="story-head">' +
      '<span class="story-head-user" id="story-head"></span>' +
      '<span class="story-head-time tiny" id="story-head-time"></span>' +
      '<button type="button" class="story-x" id="story-close" title="' + UI.attr(T('common.close')) + '">' +
      UI.icon('xmark') + '</button>' +
      '</div>' +
      '<div class="story-media" id="story-media"></div>' +
      '<div class="story-caption" id="story-caption-text"></div>' +
      '<div class="story-foot" id="story-foot"></div>' +
      '<button type="button" class="story-nav prev" id="story-prev" aria-label="prev">' + UI.icon('chevron-left') + '</button>' +
      '<button type="button" class="story-nav next" id="story-next" aria-label="next">' + UI.icon('chevron-right') + '</button>' +
      '</div>';
    document.body.appendChild(o);

    byId('story-close').addEventListener('click', close);
    byId('story-prev').addEventListener('click', function (e) { e.stopPropagation(); step(-1); });
    byId('story-next').addEventListener('click', function (e) { e.stopPropagation(); step(1); });
    o.addEventListener('click', function (e) {
      if (e.target === o) close();
    });
    document.addEventListener('keydown', onKey);
  }

  function onKey(e) {
    if (!byId('story-overlay') || !byId('story-overlay').className.match(/open/)) return;
    if (e.key === 'Escape') { close(); return; }
    if (e.key === 'ArrowLeft') { step(-1); return; }
    if (e.key === 'ArrowRight' || e.key === ' ') { e.preventDefault(); step(1); }
  }

  function show() {
    var item = flat[pos];
    if (!item) { close(); return; }
    var s = item.story;
    var u = item.group.user || {};

    var overlay = byId('story-overlay');
    overlay.className = 'story-overlay open';

    var head = byId('story-head');
    if (head) {
      head.innerHTML = '<a href="#/profile/' + (u.id || 0) + '">' + UI.avatar(u, 30) + '</a>' +
        '<a class="story-head-name" href="#/profile/' + (u.id || 0) + '">' +
        UI.esc(u.displayName || u.username || '?') + '</a>';
    }
    var time = byId('story-head-time');
    if (time) time.textContent = UI.timeAgo(s.created);

    // progress bars: done, current, upcoming
    var prog = byId('story-progress');
    if (prog) {
      var html = '';
      for (var i = 0; i < item.group.stories.length; i++) {
        var cls = i < indexInGroup() ? 'done' : i === indexInGroup() ? 'now' : '';
        html += '<span class="story-seg ' + cls + '"><i></i></span>';
      }
      prog.innerHTML = html;
    }

    var media = byId('story-media');
    if (media) {
      var src = UI.safeUrl(s.video || s.image);
      if (s.video && src) {
        media.innerHTML = '<video id="story-video" src="' + UI.attr(src) + '" playsinline autoplay></video>';
        var v = byId('story-video');
        if (v) {
          v.addEventListener('loadedmetadata', function () { armTimer(Math.min(30, v.duration || 15)); });
          v.addEventListener('ended', function () { step(1); });
          v.play().catch(function () { armTimer(15); });
        }
      } else if (src) {
        media.innerHTML = '<img src="' + UI.attr(src) + '" alt="">';
        armTimer(7);
      } else {
        media.innerHTML = '';
        armTimer(5);
      }
    }

    var cap = byId('story-caption-text');
    if (cap) {
      cap.textContent = s.caption || '';
      cap.style.display = s.caption ? '' : 'none';
    }

    var foot = byId('story-foot');
    if (foot) {
      if (item.group.mine) {
        foot.innerHTML = '<button type="button" class="btn btn-sm" id="story-views">' +
          UI.icon('eye') + ' ' + T('stories.views', String(s.views || 0)) + '</button>' +
          '<button type="button" class="btn btn-sm btn-danger" id="story-remove">' +
          UI.icon('trash') + ' ' + T('common.delete') + '</button>';
        var views = byId('story-views');
        if (views) views.addEventListener('click', function () { showViewers(s); });
        var del = byId('story-remove');
        if (del) del.addEventListener('click', function () {
          Api.request('POST', '/api/story/delete', { id: s.id }, function (err) {
            if (err) { UI.toast(Api.errorText(err), 'err'); return; }
            UI.toast(T('stories.deleted'), 'ok');
            close();
            render();
          });
        });
      } else {
        foot.innerHTML = '';
      }
    }

    // tell the server it was seen (the author's own views are not counted)
    if (!item.group.mine && !s.seen) {
      Api.request('POST', '/api/story/seen', { id: s.id }, function (err) {
        if (!err) {
          s.seen = true;
          for (var i = 0; i < groups.length; i++)
            if (groups[i] === item.group) groups[i].unseen = false;
          var circle = document.querySelector('.story-circle[data-author="' + (u.id || 0) + '"]');
          if (circle) circle.className = circle.className.replace(' unseen', '');
        }
      });
    }
  }

  function indexInGroup() {
    var item = flat[pos];
    if (!item) return 0;
    for (var i = 0; i < item.group.stories.length; i++)
      if (item.group.stories[i] === item.story) return i;
    return 0;
  }

  function armTimer(seconds) {
    if (timer) clearTimeout(timer);
    openedAt = Date.now();
    timer = setTimeout(function () { step(1); }, Math.max(2, seconds) * 1000);
  }

  function step(delta) {
    if (!byId('story-overlay')) return;
    if (timer) { clearTimeout(timer); timer = null; }
    var next = pos + delta;
    if (next < 0) { next = 0; }
    if (next >= flat.length) {
      // move on to the next author's stories, if any
      var cur = flat[pos] && flat[pos].group;
      var nextGroup = null;
      for (var i = 0; i < groups.length; i++) {
        if (groups[i] === cur) { nextGroup = groups[i + 1] || null; break; }
      }
      if (nextGroup && nextGroup.stories.length) {
        flat = [];
        for (var k = 0; k < nextGroup.stories.length; k++) flat.push({ group: nextGroup, story: nextGroup.stories[k] });
        pos = 0;
        show();
        return;
      }
      close();
      return;
    }
    pos = next;
    show();
  }

  function showViewers(s) {
    Api.request('GET', '/api/story/viewers?id=' + s.id, null, function (err, data) {
      if (err) { UI.toast(Api.errorText(err), 'err'); return; }
      var list = (data && data.viewers) || [];
      var body;
      if (!list.length) {
        body = UI.empty('eye', T('stories.noViews'), T('stories.noViewsHint'));
      } else {
        body = '<div class="user-grid">';
        for (var i = 0; i < list.length; i++) body += App.userCard(list[i]);
        body += '</div>';
      }
      UI.modal({
        title: T('stories.viewers'),
        icon: 'eye',
        wide: true,
        body: body,
        actions: [{ label: T('common.close'), cls: 'btn-ghost' }]
      });
    });
  }

  function close() {
    if (timer) { clearTimeout(timer); timer = null; }
    var o = byId('story-overlay');
    if (o) {
      o.className = 'story-overlay';
      var media = byId('story-media');
      if (media) media.innerHTML = '';
    }
    var v = byId('story-video');
    if (v) { try { v.pause(); } catch (e) { } }
    pos = -1;
    render();
  }

  return { render: render, open: openFor, close: close };
})();
