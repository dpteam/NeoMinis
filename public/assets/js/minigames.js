/* =========================================================================
   MiniGamesUI - the shelf and the player for the built-in games.

   A game is an app, so the score table, the leaderboard and the play session all
   come from the Applications API. This file only does the three things the
   browser has to do: start a round, frame the game, and submit the score the
   game reports.
   ========================================================================= */
var MiniGamesUI = (function () {
  'use strict';

  var THEME_VARS = ['--accent', '--accent-soft', '--panel', '--panel-2', '--border',
    '--text', '--text-soft', '--muted', '--ok', '--err', '--radius-sm', '--font-display'];
  var SOUNDS = { click: 1, message: 1, success: 1, error: 1, post: 1, like: 1 };

  // The round in progress. One at a time: the modal is modal, and a second game
  // would have to fight the first one for the same score slot.
  var current = null;

  function T(k, a, b) { return I18N.t(k, a, b); }
  function byId(id) { return document.getElementById(id); }

  /// The game's own name and blurb, translated here rather than on the server:
  /// the server sends the English fallback and the dictionary wins when it has one.
  function label(key, fallback) {
    var s = T(key);
    return s === key ? fallback : s;
  }

  function themeVars() {
    var cs = getComputedStyle(document.documentElement);
    var out = {};
    for (var i = 0; i < THEME_VARS.length; i++) {
      var v = cs.getPropertyValue(THEME_VARS[i]);
      if (v && v.trim()) out[THEME_VARS[i]] = v.trim();
    }
    return out;
  }

  // ------------------------------------------------------------------- shelf

  function shelf(box) {
    box.innerHTML = '<div class="card"><div class="card-h">' + UI.icon('gamepad') + ' ' + T('mini.title') +
      '</div><div class="card-b">' +
      '<div class="tiny muted" style="margin-bottom:10px">' + T('mini.hint') + '</div>' +
      '<div id="mini-list"><div class="center" style="padding:18px">' + UI.spinner() + '</div></div>' +
      '</div></div>';

    Api.request('GET', '/api/minigames', null, function (err, data) {
      var list = byId('mini-list');
      if (!list) return;
      if (err) { list.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>'; return; }
      if (data && data.enabled === false) {
        list.innerHTML = UI.empty('gamepad', T('mini.off'), T('mini.offHint'));
        return;
      }
      render(data && data.games || []);
    });
  }

  function render(games) {
    var list = byId('mini-list');
    if (!list) return;
    if (!games.length) {
      list.innerHTML = UI.empty('gamepad', T('mini.none'), '');
      return;
    }
    var html = '<div class="mini-grid">';
    for (var i = 0; i < games.length; i++) {
      var g = games[i];
      var top = g.top || [];
      var rows = '';
      for (var k = 0; k < top.length; k++) {
        rows += '<div class="mini-rank"><span>' + (k + 1) + '.</span><span class="mini-rank-name">' +
          UI.esc(top[k].name) + '</span><b>' + top[k].score + '</b></div>';
      }
      html += '<div class="mini-card" data-id="' + UI.attr(g.id) + '">' +
        '<div class="mini-head">' + UI.icon(g.icon || 'gamepad') + ' <b>' +
        UI.esc(label('mini.' + g.id + '.name', g.name)) + '</b></div>' +
        '<div class="mini-desc tiny muted">' + UI.esc(label('mini.' + g.id + '.desc', g.description)) + '</div>' +
        '<div class="mini-meta tiny muted">' +
        (g.best ? T('mini.yourBest', g.best) : T('mini.noBest')) +
        (g.seconds ? ' · ' + T('mini.about', Math.max(1, Math.round(g.seconds / 60))) : '') +
        '</div>' +
        (rows ? '<div class="mini-ranks">' + rows + '</div>' : '') +
        '<div class="mini-actions"><button type="button" class="btn btn-sm btn-primary mini-play" data-id="' +
        UI.attr(g.id) + '">' + UI.icon('play') + ' ' + T('mini.play') + '</button></div>' +
        '</div>';
    }
    list.innerHTML = html + '</div>';

    var btns = list.querySelectorAll('.mini-play');
    for (var b = 0; b < btns.length; b++) {
      btns[b].addEventListener('click', function () {
        var id = this.getAttribute('data-id');
        for (var n = 0; n < games.length; n++) if (games[n].id === id) { open(games[n]); return; }
      });
    }
  }

  // ------------------------------------------------------------------ player

  /// Opens a round: the server hands out a play token, then the game is framed.
  /// The token is what makes a score count, so it is requested before the game
  /// is even visible - a score that arrives without one is refused.
  function open(game) {
    if (!game.appId) { UI.toast(T('mini.notReady'), 'err'); return; }
    Api.request('POST', '/api/apps/play', { id: game.appId }, function (err, data) {
      if (err) { UI.toast(Api.errorText(err), 'err'); return; }
      showPlayer(game, data && data.playId);
    });
  }

  function showPlayer(game, playId) {
    var lang = I18N.getLang() === 'ru' ? 'ru' : 'en';
    var src = '/play/mini/' + encodeURIComponent(game.id) + '?lang=' + lang;
    var name = label('mini.' + game.id + '.name', game.name);

    var body = '<div class="mini-stage" id="mini-stage">' +
      '<iframe id="mini-frame" title="' + UI.attr(name) + '" sandbox="allow-scripts" ' +
      'referrerpolicy="no-referrer" src="' + UI.attr(src) + '"></iframe>' +
      '</div>' +
      '<div class="mini-result tiny muted" id="mini-result">' +
      (game.best ? T('mini.yourBest', game.best) : '') + '</div>';

    var close = UI.modal({
      title: name,
      icon: game.icon || 'gamepad',
      wide: true,
      body: body,
      actions: [
        { label: T('common.close'), cls: 'btn-ghost' },
        {
          label: T('mini.again'), cls: 'btn-primary', keepOpen: true,
          onClick: function () { replay(game); return false; }
        }
      ],
      onClose: function () {
        window.removeEventListener('message', onMessage);
        var frame = byId('mini-frame');
        if (frame) frame.src = 'about:blank';
        current = null;
      }
    });

    current = { game: game, playId: playId, frame: byId('mini-frame'), sent: false };

    function onMessage(e) {
      var d = e.data;
      if (!d || d.__mini !== true) return;
      var frame = byId('mini-frame');
      if (frame && e.source !== frame.contentWindow) return;

      if (d.type === 'ready' || d.type === 'theme-request') {
        sendTheme();
      } else if (d.type === 'sound') {
        if (SOUNDS[d.name]) SoundFX.play(d.name);
      } else if (d.type === 'over') {
        submit(current, Number(d.score) || 0);
      }
    }

    function sendTheme() {
      var frame = byId('mini-frame');
      if (!frame || !frame.contentWindow) return;
      try { frame.contentWindow.postMessage({ __mini: true, type: 'theme', vars: themeVars() }, '*'); } catch (e) { }
    }

    window.addEventListener('message', onMessage);
    var frameEl = byId('mini-frame');
    if (frameEl) frameEl.addEventListener('load', sendTheme);
  }

  /// A round pays once, so a second "over" from the same frame is ignored.
  function submit(current, score) {
    if (!current || current.sent) return;
    current.sent = true;
    var out = byId('mini-result');
    if (out) out.innerHTML = UI.spinner();

    Api.request('POST', '/api/apps/score',
      { id: current.game.appId, score: score, playId: current.playId }, function (err, data) {
        var line = byId('mini-result');
        if (err) {
          if (line) line.textContent = Api.errorText(err);
          return;
        }
        if (!line) return;
        var rank = (data && data.rank) || 0;
        var improved = !!(data && data.improved);
        line.innerHTML = '<b>' + T('mini.score', score) + '</b> · ' +
          (rank ? T('mini.rank', rank) : '') +
          (improved ? ' · <span class="ok">' + T('mini.record') + '</span>' : '');
        SoundFX.play(improved ? 'success' : 'click');
        // refresh the shelf behind the modal so the new best shows up on close;
        // the modal lives outside the route, so re-rendering the page does not
        // disturb the game that is still on screen
        if (improved) App.route();
      });
  }

  /// A new round needs a new token: the old one was spent by the score.
  function replay(game) {
    Api.request('POST', '/api/apps/play', { id: game.appId }, function (err, data) {
      if (err) { UI.toast(Api.errorText(err), 'err'); return; }
      var frame = byId('mini-frame');
      if (!frame) return;
      var lang = I18N.getLang() === 'ru' ? 'ru' : 'en';
      frame.src = '/play/mini/' + encodeURIComponent(game.id) + '?lang=' + lang + '&r=' + Date.now();
      var out = byId('mini-result');
      if (out) out.textContent = '';
      // the frame's onMessage handler reads `current` from the closure, so the new
      // token has to land there; the simplest honest way is to reload the modal
      current = { game: game, playId: data && data.playId, frame: frame, sent: false };
      setTimeout(function () {
        try { frame.contentWindow.postMessage({ __mini: true, type: 'theme', vars: themeVars() }, '*'); } catch (e) { }
      }, 400);
    });
  }

  var current = null;

  return { shelf: shelf, open: open };
})();
