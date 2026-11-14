/* =========================================================================
   GamesUI - the Flash shelf. Games are .swf files played by Ruffle, which is
   vendored under /assets/vendor/ruffle and only ever started inside
   /play/<id>: a standalone page embedded here in an iframe with
   sandbox="allow-scripts". That iframe has an opaque origin, so a movie can
   poke at ExternalInterface all it likes and still cannot reach the session,
   the cookies or this document.
   ========================================================================= */
var GamesUI = (function () {
  'use strict';

  function T(k, a, b) { return I18N.t(k, a, b); }
  function byId(id) { return document.getElementById(id); }

  function ready() {
    var site = App.state.site || {};
    return site.gamesEnabled !== false;
  }

  function fmtSize(bytes) {
    var n = Number(bytes) || 0;
    if (n < 1024) return n + ' B';
    if (n < 1024 * 1024) return (n / 1024).toFixed(0) + ' KB';
    return (n / 1024 / 1024).toFixed(1) + ' MB';
  }

  function card(g) {
    var owner = g.owner || {};
    return '<a class="game-card" href="#/game/' + g.id + '">' +
      '<div class="game-cover">' + UI.icon('gamepad') +
      (g.plays > 0 ? '<span class="game-plays">' + UI.icon('play') + ' ' + g.plays + '</span>' : '') +
      '</div>' +
      '<div class="game-meta"><div class="game-title">' + UI.esc(g.title) + '</div>' +
      '<div class="tiny muted">' + UI.esc(owner.displayName || owner.username || '?') +
      (g.likes ? ' · ' + UI.icon('heart') + ' ' + g.likes : '') + '</div></div></a>';
  }

  // ------------------------------------------------------------- shelf page

  App.views.games = function (main, r) {
    if (!ready()) {
      main.innerHTML = '<div class="card"><div class="card-b">' +
        UI.empty('gamepad', T('games.off'), T('games.offHint')) + '</div></div>';
      return;
    }

    var query = App.qs('q') || '';
    main.innerHTML = '<div class="card"><div class="card-h">' + UI.icon('gamepad') + ' ' + T('games.title') +
      '<span class="right"><button type="button" class="btn btn-xs btn-primary" id="game-upload">' +
      UI.icon('upload') + ' ' + T('games.upload') + '</button></span></div>' +
      '<div class="card-b"><div class="inline-fields">' +
      '<div class="field" style="flex:1"><input class="input" id="game-search" placeholder="' +
      UI.attr(T('games.search')) + '" value="' + UI.attr(query) + '"></div>' +
      '<div class="field" style="flex:0 0 auto"><button class="btn" id="game-search-go">' +
      UI.icon('magnifying-glass') + '</button></div>' +
      '</div></div><div class="card-b flush" id="game-list">' +
      '<div class="center" style="padding:20px">' + UI.spinner() + '</div></div></div>';

    function load(before) {
      var url = '/api/games?limit=24' + (query ? '&q=' + encodeURIComponent(query) : '') +
        (before ? '&before=' + before : '');
      Api.request('GET', url, null, function (err, data) {
        var box = byId('game-list');
        if (!box) return;
        if (err) { box.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>'; return; }
        var list = (data && data.games) || [];
        if (!before) {
          if (!list.length) {
            box.innerHTML = UI.empty('gamepad', T('games.empty'), T('games.emptyHint'));
            return;
          }
          box.innerHTML = '<div class="game-grid" id="game-grid"></div>';
        }
        var grid = byId('game-grid');
        var html = '';
        for (var i = 0; i < list.length; i++) html += card(list[i]);
        grid.insertAdjacentHTML('beforeend', html);
        var more = byId('game-more');
        if (more && more.parentNode) more.parentNode.removeChild(more);
        if (data.hasMore) {
          grid.insertAdjacentHTML('afterend', '<button type="button" class="load-more" id="game-more">' +
            T('feed.loadMore') + '</button>');
          var mb = byId('game-more');
          mb.addEventListener('click', function () {
            mb.disabled = true;
            load(data.next);
          });
        }
      });
    }

    byId('game-upload').addEventListener('click', openUpload);
    byId('game-search-go').addEventListener('click', function () {
      var v = (byId('game-search') || {}).value || '';
      location.hash = '#/games' + (v ? '?q=' + encodeURIComponent(v) : '');
    });
    var box = byId('game-search');
    if (box) box.addEventListener('keydown', function (e) {
      if (e.key === 'Enter') { e.preventDefault(); byId('game-search-go').click(); }
    });

    load(0);
  };

  // ----------------------------------------------------------- upload modal

  function openUpload() {
    var maxKb = Number((App.state.site || {}).maxGameKB) || 8192;
    var body = '<div class="field"><label>' + T('games.file') + '</label>' +
      '<input class="input" type="file" id="game-file" accept=".swf">' +
      '<div class="hint">' + T('games.fileHint', String(maxKb)) + '</div></div>' +
      '<div class="field"><label>' + T('games.name') + '</label>' +
      '<input class="input" id="game-name" maxlength="120"></div>' +
      '<div class="field"><label>' + T('games.description') + '</label>' +
      '<textarea class="input" id="game-desc" rows="3" maxlength="1000"></textarea></div>';

    UI.modal({
      title: T('games.upload'),
      icon: 'upload',
      body: body,
      actions: [
        { label: T('common.cancel'), cls: 'btn-ghost' },
        {
          label: T('games.uploadAction'), cls: 'btn-primary', icon: 'upload', keepOpen: true,
          onClick: function (box, done) {
            var input = byId('game-file');
            var file = input && input.files && input.files[0];
            if (!file) { UI.toast(T('games.pickFile'), 'err'); return false; }
            if (file.size > maxKb * 1024) {
              UI.toast(T('games.tooBig', String(maxKb)), 'err');
              return false;
            }
            var name = ((byId('game-name') || {}).value || '').replace(/^\s+|\s+$/g, '');
            if (!name) name = file.name.replace(/\.swf$/i, '');
            var desc = (byId('game-desc') || {}).value || '';

            var reader = new FileReader();
            reader.onload = function () {
              Api.request('POST', '/api/game/upload', {
                data: reader.result, title: name, description: desc
              }, function (err, data) {
                if (err) { UI.toast(Api.errorText(err), 'err'); return; }
                if (done) done();
                UI.toast(T('games.uploaded'), 'ok');
                SoundFX.play('success');
                location.hash = '#/game/' + data.game.id;
              });
            };
            reader.onerror = function () { UI.toast(T('games.readFail'), 'err'); };
            reader.readAsDataURL(file);
            return false;
          }
        }
      ]
    });
  }

  // ------------------------------------------------------------ game page

  App.views.game = function (main, r) {
    var id = Number(r.parts[1] || 0);
    main.innerHTML = '<div class="center" style="padding:20px">' + UI.spinner() + '</div>';
    if (!id) { main.innerHTML = '<div class="alert err">' + T('games.notFound') + '</div>'; return; }

    Api.request('GET', '/api/game?id=' + id, null, function (err, data) {
      if (err) {
        main.innerHTML = '<div class="card"><div class="card-b"><div class="alert err">' +
          UI.esc(Api.errorText(err)) + '</div></div></div>';
        return;
      }
      var g = data.game;
      var owner = g.owner || {};
      var playUrl = '/play/' + g.id;

      var actions = '';
      actions += '<button type="button" class="btn" id="game-like">' + UI.icon('heart') + ' ' +
        '<span id="game-likes">' + g.likes + '</span></button>';
      actions += '<a class="btn" href="' + UI.attr(playUrl) + '" target="_blank" rel="noopener">' +
        UI.icon('up-right-from-square') + ' ' + T('games.newTab') + '</a>';
      if (g.mine) {
        actions += '<button type="button" class="btn" id="game-edit">' + UI.icon('pen') + ' ' +
          T('common.edit') + '</button>';
        actions += '<button type="button" class="btn btn-danger" id="game-delete">' + UI.icon('trash') + ' ' +
          T('common.delete') + '</button>';
      }

      main.innerHTML = '<div class="card"><div class="card-h">' + UI.icon('gamepad') + ' ' +
        UI.esc(g.title) + '<span class="right"><a href="#/games">' + T('games.all') + '</a></span></div>' +
        '<div class="card-b flush"><div class="game-frame-wrap">' +
        (ready()
          ? '<iframe class="game-frame" id="game-frame" src="' + UI.attr(playUrl) + '"' +
            ' sandbox="allow-scripts" referrerpolicy="no-referrer" title="' + UI.attr(g.title) + '"></iframe>'
          : UI.empty('gamepad', T('games.off'), T('games.offHint'))) +
        '</div></div>' +
        '<div class="card-b"><div class="game-actions">' + actions + '</div>' +
        '<div class="tiny muted" style="margin-top:8px">' + T('games.by') + ' ' +
        (owner.id ? '<a href="#/profile/' + owner.id + '">' + UI.esc(owner.displayName || owner.username) + '</a>' : '?') +
        ' · ' + UI.formatDate(g.created) + ' · ' + fmtSize(g.size) +
        ' · ' + UI.icon('play') + ' ' + g.plays +
        (g.liked ? ' · ' + T('games.youLiked') : '') + '</div>' +
        (g.description ? '<div class="game-desc">' + UI.nl2br(UI.esc(g.description)) + '</div>' : '') +
        '</div></div>';

      if (ready()) {
        Api.request('POST', '/api/game/play', { id: g.id }, function () { });
      }

      var likeBtn = byId('game-like');
      if (likeBtn) likeBtn.addEventListener('click', function () {
        Api.request('POST', '/api/game/like', { id: g.id }, function (e2, d2) {
          if (e2) { UI.toast(Api.errorText(e2), 'err'); return; }
          byId('game-likes').textContent = d2.likes;
          likeBtn.className = 'btn' + (d2.liked ? ' btn-primary' : '');
        });
      });

      var editBtn = byId('game-edit');
      if (editBtn) editBtn.addEventListener('click', function () {
        UI.modal({
          title: T('games.edit'),
          icon: 'pen',
          body: '<div class="field"><label>' + T('games.name') + '</label>' +
            '<input class="input" id="ge-name" maxlength="120" value="' + UI.attr(g.title) + '"></div>' +
            '<div class="field"><label>' + T('games.description') + '</label>' +
            '<textarea class="input" id="ge-desc" rows="3" maxlength="1000">' + UI.esc(g.description) + '</textarea></div>',
          actions: [
            { label: T('common.cancel'), cls: 'btn-ghost' },
            {
              label: T('common.save'), cls: 'btn-primary', keepOpen: true,
              onClick: function (box, done) {
                Api.request('POST', '/api/game/edit', {
                  id: g.id,
                  title: (byId('ge-name') || {}).value || '',
                  description: (byId('ge-desc') || {}).value || ''
                }, function (e2) {
                  if (e2) { UI.toast(Api.errorText(e2), 'err'); return; }
                  if (done) done();
                  UI.toast(T('common.saved'), 'ok');
                  App.views.game(main, r);
                });
                return false;
              }
            }
          ]
        });
      });

      var delBtn = byId('game-delete');
      if (delBtn) delBtn.addEventListener('click', function () {
        UI.confirm(T('games.deleteTitle'), T('games.deleteConfirm'), T('common.delete'), function () {
          Api.request('POST', '/api/game/delete', { id: g.id }, function (e2) {
            if (e2) { UI.toast(Api.errorText(e2), 'err'); return; }
            UI.toast(T('games.deleted'), 'ok');
            location.hash = '#/games';
          });
        });
      });
    });
  };

  return { ready: ready };
})();
