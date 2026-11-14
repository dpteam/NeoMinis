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
  // Bumped on every open and every close. A callback captured the value it was
  // born with, so a timer or a media event that arrives one tick too late does
  // nothing instead of reopening the story the visitor just closed.
  var epoch = 0;

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
    epoch++;
    ensureOverlay();
    show();
  }

  /// <summary>
  /// True only while the viewer is actually on screen. The overlay element is
  /// reused for the whole session (it is hidden, never removed), so its mere
  /// existence says nothing about whether the visitor is looking at it.
  /// </summary>
  function isOpen() {
    var o = byId('story-overlay');
    return !!o && /(^|\s)open(\s|$)/.test(o.className);
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
    if (!isOpen()) return;
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
          // A clip's bar must follow the clip's own clock, not a fixed CSS
          // animation: a 20-second video used to fill the 7-second default bar
          // and then sit at 100% while the clip kept playing.
          freezeBar(0);
          var mine = epoch;
          v.addEventListener('timeupdate', function () {
            // the element may outlive the close by a tick
            if (mine !== epoch || !isOpen()) return;
            var dur = v.duration;
            if (dur > 0 && isFinite(dur)) freezeBar(v.currentTime / dur);
            // keep the watchdog alive: only a stall or a missing "ended" fires it
            armWatchdog();
          });
          v.addEventListener('ended', function () {
            if (mine !== epoch || !isOpen()) return;
            step(1);
          });
          // Some screen recordings carry no duration at all (Infinity); seeking
          // far ahead makes the browser scan the file and report the real one.
          v.addEventListener('loadedmetadata', function () {
            if (v.duration !== Infinity) return;
            var once = function () {
              v.removeEventListener('timeupdate', once);
              try { v.currentTime = 0; } catch (e) { }
              var again = v.play();
              if (again && again.catch) again.catch(function () { });
            };
            v.addEventListener('timeupdate', once);
            try { v.currentTime = 1e101; } catch (e) { }
          });
          var started = v.play();
          if (started && started.catch) {
            started.catch(function () {
              // autoplay refused: fall back to a fixed bar and a fixed dwell
              armTimer(15);
            });
          }
          // generous first wait: a slow clip may take a while to start
          armWatchdog(15000);
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

  /// <summary>The fill of the segment being watched.</summary>
  function barEl() {
    return document.querySelector('#story-progress .story-seg.now i');
  }

  /// <summary>
  /// Runs the bar over the given number of seconds. Used for photos and for the
  /// autoplay fallback, where there is no media clock to follow.
  /// </summary>
  function animateBar(seconds) {
    var i = barEl();
    if (!i) return;
    i.style.animation = 'none';
    i.style.width = '0%';
    void i.offsetWidth; // restart the animation even if it already ran
    i.style.animation = 'story-fill ' + Math.max(2, seconds) + 's linear forwards';
  }

  /// <summary>
  /// Pins the bar at a ratio between 0 and 1 and stops the animation. A clip
  /// calls this on every timeupdate, so the bar is exactly as far along as the
  /// clip itself, whatever its length, buffering included.
  /// </summary>
  function freezeBar(ratio) {
    var i = barEl();
    if (!i) return;
    if (!isFinite(ratio) || ratio < 0) ratio = 0;
    if (ratio > 1) ratio = 1;
    i.style.animation = 'none';
    i.style.width = (ratio * 100).toFixed(2) + '%';
  }

  /// <summary>
  /// Safety net for a clip: if no timeupdate arrives for a while the video has
  /// stalled or ended without firing "ended", so move on. Every timeupdate
  /// pushes it back, which is why a long clip is never cut short.
  /// </summary>
  function armWatchdog(ms) {
    if (timer) clearTimeout(timer);
    var mine = epoch;
    timer = setTimeout(function () {
      if (mine !== epoch || !isOpen()) return;
      step(1);
    }, ms || 5000);
  }

  function armTimer(seconds) {
    if (timer) clearTimeout(timer);
    var mine = epoch;
    openedAt = Date.now();
    animateBar(seconds);
    timer = setTimeout(function () {
      if (mine !== epoch || !isOpen()) return;
      step(1);
    }, Math.max(2, seconds) * 1000);
  }

  function step(delta) {
    // a timer or a media event that outlived the close must not resurrect it
    if (!isOpen()) return;
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
    // Anything already queued in the event loop belongs to the story that is
    // being closed, so invalidate it before doing anything else.
    epoch++;
    if (timer) { clearTimeout(timer); timer = null; }
    // Pause first: clearing the media box detaches the element, and a detached
    // video can keep playing its audio for a moment.
    var v = byId('story-video');
    if (v) { try { v.pause(); v.removeAttribute('src'); v.load(); } catch (e) { } }
    var o = byId('story-overlay');
    if (o) {
      o.className = 'story-overlay';
      var media = byId('story-media');
      if (media) media.innerHTML = '';
    }
    pos = -1;
    flat = [];
    render();
  }

  return { render: render, open: openFor, close: close };
})();
