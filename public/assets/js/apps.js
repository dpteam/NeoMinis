/* =========================================================================
   AppsUI - the catalogue, the consent screen and the developer panel.

   An app is a third-party thing that asks for a few named permissions. The user
   ticks the ones they agree to, the server hands back a one-time code, and the
   app trades that code (plus its own secret) for a token. Nothing here ever sees
   a token: the code is the only thing that crosses the page.
   ========================================================================= */
var AppsUI = (function () {
  'use strict';

  var SCOPE_KEYS = {
    'profile': 'apps.scopeProfile',
    'wall.post': 'apps.scopeWallPost',
    'messages.read': 'apps.scopeMessages',
    'storage': 'apps.scopeStorage',
    'score': 'apps.scopeScore'
  };

  function T(k, a) { return I18N.t(k, a); }
  function byId(id) { return document.getElementById(id); }

  function scopeLabel(scope) {
    var key = SCOPE_KEYS[scope];
    return key ? T(key) : scope;
  }

  function scopeHint(scope) {
    var key = SCOPE_KEYS[scope];
    return key ? T(key + 'Hint') : '';
  }

  function typeIcon(type) {
    if (type === 'html') return 'window-maximize';
    if (type === 'swf') return 'gamepad';
    return 'globe';
  }

  // ------------------------------------------------------------- catalogue

  App.views.apps = function (main) {
    main.innerHTML =
      '<div class="card"><div class="card-h">' + UI.icon('puzzle-piece') + ' ' + T('apps.title') +
      '<span class="right"><button type="button" class="btn btn-xs" id="apps-new">' +
      UI.icon('plus') + ' ' + T('apps.new') + '</button></span></div>' +
      '<div class="card-b"><div class="tiny muted" style="margin-bottom:10px">' + T('apps.hint') + '</div>' +
      '<div id="apps-list"><div class="center" style="padding:18px">' + UI.spinner() + '</div></div></div></div>' +
      '<div class="card"><div class="card-h">' + UI.icon('key') + ' ' + T('apps.installed') + '</div>' +
      '<div class="card-b" id="apps-grants"><div class="center" style="padding:14px">' + UI.spinner() + '</div></div></div>' +
      '<div class="card"><div class="card-h">' + UI.icon('code') + ' ' + T('apps.mine') + '</div>' +
      '<div class="card-b" id="apps-own"><div class="center" style="padding:14px">' + UI.spinner() + '</div></div></div>';

    var state = { apps: [], grants: [], scopes: [], canHtml: false };

    function load() {
      Api.request('GET', '/api/apps/mine', null, function (err, data) {
        if (err) {
          var box = byId('apps-list');
          if (box) box.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>';
          return;
        }
        state.apps = (data && data.apps) || [];
        state.grants = (data && data.grants) || [];
        state.scopes = (data && data.scopes) || [];
        state.canHtml = !!(data && data.canPublishHtml);
        // the catalogue is what everybody may install, so fetch it separately
        Api.request('GET', '/api/apps', null, function (e2, d2) {
          var published = (e2 || !d2) ? [] : (d2.apps || []);
          renderCatalogue(published);
          renderGrants();
          renderOwn();
        });
      });
    }

    function isInstalled(appId) {
      for (var i = 0; i < state.grants.length; i++) if (state.grants[i].appId === appId) return state.grants[i];
      return null;
    }

    function renderCatalogue(published) {
      var box = byId('apps-list');
      if (!box) return;
      if (!published.length) {
        box.innerHTML = UI.empty('puzzle-piece', T('apps.empty'), T('apps.emptyHint'));
        return;
      }
      var html = '<div class="app-grid">';
      for (var i = 0; i < published.length; i++) {
        var a = published[i];
        var grant = isInstalled(a.id);
        html += '<div class="app-card">' +
          '<div class="app-head">' + UI.icon(typeIcon(a.type)) + ' <b>' + UI.esc(a.name) + '</b></div>' +
          '<div class="app-desc tiny muted">' + UI.esc(a.description || '') + '</div>' +
          '<div class="app-meta tiny muted">' + T('apps.by', a.ownerName) +
          (a.installs ? ' · ' + T('apps.installs', String(a.installs)) : '') + '</div>' +
          '<div class="app-actions">' +
          (grant
            ? '<span class="badge ok">' + UI.icon('check') + ' ' + T('apps.granted') + '</span>' +
              '<button type="button" class="btn btn-xs btn-danger app-revoke" data-id="' + a.id + '">' +
              T('apps.revoke') + '</button>'
            : '<button type="button" class="btn btn-xs btn-primary app-install" data-id="' + a.id + '">' +
              T('apps.install') + '</button>') +
          (a.url && (grant || a.mine)
            ? '<a class="btn btn-xs" href="' + UI.attr(UI.safeUrl(a.url)) + '" target="_blank" rel="noopener noreferrer">' +
              T('apps.open') + '</a>'
            : '') +
          '</div></div>';
      }
      box.innerHTML = html + '</div>';

      var installs = box.querySelectorAll('.app-install');
      for (var k = 0; k < installs.length; k++) {
        installs[k].addEventListener('click', function () {
          var app = findApp(published, this.getAttribute('data-id'));
          if (app) consentModal(app, load);
        });
      }
      var revokes = box.querySelectorAll('.app-revoke');
      for (var m = 0; m < revokes.length; m++) {
        revokes[m].addEventListener('click', function () {
          var id = Number(this.getAttribute('data-id'));
          UI.confirm(T('apps.revoke'), T('apps.revokeConfirm'), T('apps.revoke'), function () {
            Api.request('POST', '/api/apps/revoke', { id: id }, function (err) {
              if (err) { UI.toast(Api.errorText(err), 'err'); return; }
              UI.toast(T('apps.revoked'), 'ok');
              load();
            });
          }, true);
        });
      }
    }

    function findApp(list, id) {
      for (var i = 0; i < list.length; i++) if (String(list[i].id) === String(id)) return list[i];
      return null;
    }

    function renderGrants() {
      var box = byId('apps-grants');
      if (!box) return;
      if (!state.grants.length) {
        box.innerHTML = '<div class="tiny muted">' + T('apps.noGrants') + '</div>';
        return;
      }
      var html = '';
      for (var i = 0; i < state.grants.length; i++) {
        var g = state.grants[i];
        var scopes = String(g.scopes || '').split(',').filter(function (s) { return s.length > 0; });
        var chips = '';
        for (var k = 0; k < scopes.length; k++)
          chips += '<span class="scope-chip">' + UI.esc(scopeLabel(scopes[k])) + '</span>';
        html += '<div class="app-grant-row">' + UI.icon(typeIcon(g.type)) +
          '<div class="app-grant-main"><b>' + UI.esc(g.name) + '</b><div class="scope-chips">' + chips + '</div>' +
          '<div class="tiny muted">' + (g.lastUsed ? T('apps.lastUsed', UI.timeAgo(g.lastUsed)) : T('apps.neverUsed')) +
          '</div></div>' +
          '<button type="button" class="btn btn-xs btn-danger app-revoke" data-id="' + g.appId + '">' +
          T('apps.revoke') + '</button></div>';
      }
      box.innerHTML = html;
      var revokes = box.querySelectorAll('.app-revoke');
      for (var r = 0; r < revokes.length; r++) {
        revokes[r].addEventListener('click', function () {
          var id = Number(this.getAttribute('data-id'));
          UI.confirm(T('apps.revoke'), T('apps.revokeConfirm'), T('apps.revoke'), function () {
            Api.request('POST', '/api/apps/revoke', { id: id }, function (err) {
              if (err) { UI.toast(Api.errorText(err), 'err'); return; }
              UI.toast(T('apps.revoked'), 'ok');
              load();
            });
          }, true);
        });
      }
    }

    function renderOwn() {
      var box = byId('apps-own');
      if (!box) return;
      var html = '';
      if (!state.apps.length) {
        html += '<div class="tiny muted" style="margin-bottom:8px">' + T('apps.noOwn') + '</div>';
      } else {
        for (var i = 0; i < state.apps.length; i++) {
          var a = state.apps[i];
          html += '<div class="app-own-row">' + UI.icon(typeIcon(a.type)) +
            '<div class="app-grant-main"><b>' + UI.esc(a.name) + '</b>' +
            (a.published ? ' <span class="badge ok">' + T('apps.published') + '</span>'
                         : ' <span class="badge">' + T('apps.draft') + '</span>') +
            '<div class="tiny muted mono">' + UI.esc(a.clientId) + '</div>' +
            '<div class="tiny muted">' + T('apps.installs', String(a.installs || 0)) + '</div></div>' +
            '<button type="button" class="btn btn-xs app-edit" data-id="' + a.id + '">' +
            T('common.edit') + '</button></div>';
        }
      }
      box.innerHTML = html;
      var edits = box.querySelectorAll('.app-edit');
      for (var k = 0; k < edits.length; k++) {
        edits[k].addEventListener('click', function () {
          var app = findApp(state.apps, this.getAttribute('data-id'));
          if (app) editModal(app, load);
        });
      }
    }

    var newBtn = byId('apps-new');
    if (newBtn) newBtn.addEventListener('click', function () { registerModal(load); });

    load();
  };

  // --------------------------------------------------------------- consent

  /// The screen that matters: what exactly is being handed over, in plain words.
  function consentModal(app, done) {
    var scopes = String(app.scopes || '').split(',').filter(function (s) { return s.length > 0; });
    var body = '<div class="tiny muted" style="margin-bottom:8px">' + T('apps.consentIntro', app.name) + '</div>';
    if (!scopes.length) {
      body += '<div class="alert info">' + T('apps.noScopes') + '</div>';
    } else {
      for (var i = 0; i < scopes.length; i++) {
        body += '<label class="check" style="margin-bottom:6px"><input type="checkbox" class="scope-box" value="' +
          UI.attr(scopes[i]) + '" checked> <span><b>' + UI.esc(scopeLabel(scopes[i])) + '</b>' +
          '<br><span class="tiny muted">' + UI.esc(scopeHint(scopes[i])) + '</span></span></label>';
      }
    }
    body += '<div class="hint">' + T('apps.consentHint') + '</div>';

    UI.modal({
      title: T('apps.consentTitle', app.name),
      icon: 'key',
      body: body,
      actions: [
        { label: T('common.cancel'), cls: 'btn-ghost' },
        {
          label: T('apps.allow'), cls: 'btn-primary', keepOpen: true,
          onClick: function (box, close) {
            var picked = [];
            var boxes = box.querySelectorAll('.scope-box');
            for (var i = 0; i < boxes.length; i++) if (boxes[i].checked) picked.push(boxes[i].value);
            if (!picked.length) { UI.toast(T('apps.pickOne'), 'err'); return false; }

            Api.request('POST', '/api/apps/authorize', { id: app.id, scopes: picked }, function (err, data) {
              if (err) { UI.toast(Api.errorText(err), 'err'); return; }
              if (close) close();
              codeModal(app, data && data.code);
              if (done) done();
            });
            return false;
          }
        }
      ]
    });
  }

  /// The one-time code is the only thing that crosses the page; show it big.
  function codeModal(app, code) {
    UI.modal({
      title: T('apps.codeTitle'),
      icon: 'key',
      body: '<p class="muted" style="margin-top:0">' + T('apps.codeHint', app.name) + '</p>' +
        '<div class="input mono" style="font-size:14px;text-align:center;user-select:all">' +
        UI.esc(code || '') + '</div>' +
        '<div class="hint">' + T('apps.codeExpiry') + '</div>',
      actions: [
        {
          label: T('auth.copy'), cls: 'btn-ghost',
          onClick: function () { UI.copy(code || ''); UI.toast(T('auth.copied'), 'ok'); return false; }
        },
        { label: T('common.close'), cls: 'btn-primary' }
      ]
    });
  }

  // ------------------------------------------------------------ developer

  function scopeBoxes(selected) {
    var scopes = ['profile', 'wall.post', 'messages.read', 'storage', 'score'];
    var html = '<div class="field"><label>' + T('apps.permissions') + '</label><div class="badge-picker">';
    for (var i = 0; i < scopes.length; i++) {
      var on = selected.indexOf(scopes[i]) >= 0;
      html += '<label class="check"><input type="checkbox" class="scope-pick" value="' + scopes[i] + '"' +
        (on ? ' checked' : '') + '> <span>' + UI.esc(scopeLabel(scopes[i])) + '</span></label>';
    }
    return html + '</div></div>';
  }

  function registerModal(done) {
    var body = '<div class="field"><label>' + T('apps.name') + '</label>' +
      '<input class="input" id="app-name" maxlength="60"></div>' +
      '<div class="field"><label>' + T('apps.description') + '</label>' +
      '<textarea class="textarea" id="app-desc" rows="3" maxlength="500"></textarea></div>' +
      '<div class="field"><label>' + T('apps.type') + '</label><select class="select" id="app-type">' +
      '<option value="external">' + T('apps.typeExternal') + '</option>' +
      '<option value="swf">' + T('apps.typeSwf') + '</option>' +
      '<option value="html">' + T('apps.typeHtml') + '</option></select>' +
      '<div class="hint">' + T('apps.typeHint') + '</div></div>' +
      '<div class="field"><label>' + T('apps.url') + '</label>' +
      '<input class="input" id="app-url" placeholder="https://example.com/app"></div>' +
      scopeBoxes([]);

    UI.modal({
      title: T('apps.new'),
      icon: 'puzzle-piece',
      wide: true,
      body: body,
      actions: [
        { label: T('common.cancel'), cls: 'btn-ghost' },
        {
          label: T('apps.register'), cls: 'btn-primary', keepOpen: true,
          onClick: function (box, close) {
            var picked = [];
            var boxes = box.querySelectorAll('.scope-pick');
            for (var i = 0; i < boxes.length; i++) if (boxes[i].checked) picked.push(boxes[i].value);
            Api.request('POST', '/api/apps/register', {
              name: byId('app-name').value,
              description: byId('app-desc').value,
              type: byId('app-type').value,
              url: byId('app-url').value,
              scopes: picked
            }, function (err, data) {
              if (err) { UI.toast(Api.errorText(err), 'err'); return; }
              if (close) close();
              secretModal(data && data.app, data && data.secret);
              if (done) done();
            });
            return false;
          }
        }
      ]
    });
  }

  /// The secret is shown exactly once. Say so, loudly, because it is true.
  function secretModal(app, secret) {
    UI.modal({
      title: T('apps.secretTitle'),
      icon: 'key',
      body: '<p class="muted" style="margin-top:0">' + T('apps.secretHint') + '</p>' +
        '<div class="field"><label>' + T('apps.clientId') + '</label>' +
        '<div class="input mono" style="user-select:all">' + UI.esc(app ? app.clientId : '') + '</div></div>' +
        '<div class="field"><label>' + T('apps.clientSecret') + '</label>' +
        '<div class="input mono" style="user-select:all">' + UI.esc(secret || '') + '</div></div>' +
        '<div class="hint">' + T('apps.secretWarn') + '</div>',
      actions: [
        {
          label: T('auth.copy'), cls: 'btn-ghost',
          onClick: function () {
            UI.copy((app ? app.clientId : '') + ':' + (secret || ''));
            UI.toast(T('auth.copied'), 'ok');
            return false;
          }
        },
        { label: T('common.close'), cls: 'btn-primary' }
      ]
    });
  }

  function editModal(app, done) {
    var body = '<div class="field"><label>' + T('apps.name') + '</label>' +
      '<input class="input" id="app-name" maxlength="60" value="' + UI.attr(app.name) + '"></div>' +
      '<div class="field"><label>' + T('apps.description') + '</label>' +
      '<textarea class="textarea" id="app-desc" rows="3" maxlength="500">' + UI.esc(app.description || '') + '</textarea></div>' +
      '<div class="field"><label>' + T('apps.url') + '</label>' +
      '<input class="input" id="app-url" value="' + UI.attr(app.url || '') + '"></div>' +
      scopeBoxes(String(app.scopes || '').split(',').filter(function (s) { return s.length > 0; })) +
      '<label class="check" style="margin-top:8px"><input type="checkbox" id="app-pub"' +
      (app.published ? ' checked' : '') + '> <span>' + T('apps.publish') + '</span></label>';

    UI.modal({
      title: T('apps.edit'),
      icon: 'pen',
      wide: true,
      body: body,
      actions: [
        { label: T('common.cancel'), cls: 'btn-ghost' },
        {
          label: T('common.delete'), cls: 'btn-danger',
          onClick: function (box, close) {
            UI.confirm(T('common.delete'), T('apps.deleteConfirm'), T('common.delete'), function () {
              Api.request('POST', '/api/apps/delete', { id: app.id }, function (err, data) {
                if (err) { UI.toast(Api.errorText(err), 'err'); return; }
                UI.toast(T('apps.deleted', String((data && data.records) || 0)), 'ok');
                if (close) close();
                if (done) done();
              });
            }, true);
            return false;
          }
        },
        {
          label: T('common.save'), cls: 'btn-primary', keepOpen: true,
          onClick: function (box, close) {
            var picked = [];
            var boxes = box.querySelectorAll('.scope-pick');
            for (var i = 0; i < boxes.length; i++) if (boxes[i].checked) picked.push(boxes[i].value);
            Api.request('POST', '/api/apps/update', {
              id: app.id,
              name: byId('app-name').value,
              description: byId('app-desc').value,
              url: byId('app-url').value,
              scopes: picked,
              published: byId('app-pub').checked
            }, function (err) {
              if (err) { UI.toast(Api.errorText(err), 'err'); return; }
              UI.toast(T('common.saved'), 'ok');
              if (close) close();
              if (done) done();
            });
            return false;
          }
        }
      ]
    });
  }

  return { scopeLabel: scopeLabel, consentModal: consentModal };
})();
