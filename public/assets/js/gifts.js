/* =========================================================================
   GiftUI - the gift shop. Gifts are not uploaded files: the server keeps a
   small catalogue of keys, each with an icon and a colour, and every gift is
   just a row in gifts.json. Everything is rendered from that catalogue, so a
   gift never breaks when a file goes missing.
   ========================================================================= */
var GiftUI = (function () {
  'use strict';

  function T(k, a, b) { return I18N.t(k, a, b); }
  function byId(id) { return document.getElementById(id); }

  function catalog() {
    var list = (App.state.site && App.state.site.gifts) || [];
    return list.length ? list : [{ key: 'heart', icon: 'heart', color: '#e0547a' }];
  }

  function kind(key) {
    var list = catalog();
    for (var i = 0; i < list.length; i++) if (list[i].key === key) return list[i];
    return { key: key || 'gift', icon: 'gift', color: '#7c8cff' };
  }

  function giftName(key) {
    var text = T('gift.' + key);
    return text && text.indexOf('gift.') !== 0 ? text : key;
  }

  function senderName(g) {
    if (g.anonymous) return T('gifts.anonName');
    var f = g.from || {};
    return f.displayName || f.username || '?';
  }

  // ---------------------------------------------------------------- tiles

  /// <summary>One gift as a coloured square with the icon on it.</summary>
  function tileHtml(g, showSender, extraClass) {
    var k = kind(g.kind);
    var tip = giftName(g.kind) + (showSender ? ' — ' + senderName(g) : '');
    if (g.message) tip += ': ' + g.message;
    return '<div class="gift-tile' + (extraClass ? ' ' + extraClass : '') + '"' +
      ' style="--gift-color:' + UI.attr(k.color) + '" title="' + UI.attr(tip) + '">' +
      '<span class="gift-icon">' + UI.icon(k.icon) + '</span>' +
      '<span class="gift-label">' + UI.esc(giftName(g.kind)) + '</span>' +
      '</div>';
  }

  function emptyBox(icon, title, hint) {
    return UI.empty(icon, title, hint);
  }

  // --------------------------------------------------------------- profile

  /// <summary>The "Gifts" card on somebody's wall. Returns '' when there is nothing to show.</summary>
  function cardHtml(u, data) {
    if (!App.state.site || App.state.site.giftsEnabled === false) return '';
    var gifts = (data && data.gifts) || [];
    var total = (data && data.giftCount) || 0;
    if (!gifts.length && !total && !(data && data.canGift)) return '';

    var html = '<div class="card"><div class="card-h">' + UI.icon('gift') + ' ' + T('gifts.title') +
      (total > gifts.length ? '<span class="right"><a href="#/gifts?user=' + u.id + '">' +
        T('gifts.allCount', String(total)) + '</a></span>' : '') +
      '</div><div class="card-b">';

    if (gifts.length) {
      html += '<div class="gift-grid">';
      for (var i = 0; i < gifts.length; i++) html += tileHtml(gifts[i], true);
      html += '</div>';
    } else {
      html += '<div class="tiny muted" style="margin-bottom:8px">' + T('gifts.none') + '</div>';
    }

    if (data && data.canGift) {
      html += '<button type="button" class="btn btn-primary gift-send" data-user="' + u.id + '">' +
        UI.icon('gift') + ' ' + T('gifts.send') + '</button>';
    }
    html += '</div></div>';
    return html;
  }

  function bind(main) {
    var buttons = main.querySelectorAll('.gift-send');
    for (var i = 0; i < buttons.length; i++) {
      buttons[i].addEventListener('click', function () {
        openSend({ id: Number(this.getAttribute('data-user')) });
      });
    }
  }

  // ------------------------------------------------------------- send modal

  function openSend(user) {
    if (!user || !user.id) return;
    if (App.state.site && App.state.site.giftsEnabled === false) {
      UI.toast(T('gifts.off'), 'err');
      return;
    }

    var list = catalog();
    var picked = list[0].key;
    var body = '<div class="gift-picker" id="gift-picker">';
    for (var i = 0; i < list.length; i++) {
      var k = list[i];
      body += '<button type="button" class="gift-pick' + (i === 0 ? ' active' : '') + '"' +
        ' data-gift="' + UI.attr(k.key) + '" style="--gift-color:' + UI.attr(k.color) + '"' +
        ' title="' + UI.attr(giftName(k.key)) + '">' + UI.icon(k.icon) + '</button>';
    }
    body += '</div>' +
      '<div class="field"><label>' + T('gifts.message') + '</label>' +
      '<textarea class="input" id="gift-message" rows="2" maxlength="' +
      Number((App.state.site && App.state.site.maxGiftMessage) || 300) + '" placeholder="' +
      UI.attr(T('gifts.messageHint')) + '"></textarea></div>' +
      '<label class="check"><input type="checkbox" id="gift-anon"> <span>' + T('gifts.anonymous') + '</span></label>';

    UI.modal({
      title: T('gifts.send'),
      icon: 'gift',
      body: body,
      actions: [
        { label: T('common.cancel'), cls: 'btn-ghost' },
        {
          label: T('gifts.sendAction'), cls: 'btn-primary', icon: 'gift', keepOpen: true,
          onClick: function (box, done) {
            var message = (byId('gift-message') || {}).value || '';
            var anon = !!(byId('gift-anon') || {}).checked;
            Api.request('POST', '/api/gifts/send', {
              to: user.id, kind: picked, message: message, anonymous: anon
            }, function (err) {
              if (err) { UI.toast(Api.errorText(err), 'err'); return; }
              if (done) done();
              UI.toast(T('gifts.sent'), 'ok');
              SoundFX.play('success');
            });
            return false;
          }
        }
      ]
    });

    var picks = document.querySelectorAll('.gift-pick');
    for (var p = 0; p < picks.length; p++) {
      picks[p].addEventListener('click', function () {
        for (var q = 0; q < picks.length; q++) picks[q].className = 'gift-pick';
        this.className = 'gift-pick active';
        picked = this.getAttribute('data-gift');
      });
    }
  }

  // ------------------------------------------------------------------ page

  App.views.gifts = function (main, r) {
    var tab = App.qs('tab') === 'sent' ? 'sent' : 'received';
    var userParam = Number(App.qs('user') || 0);
    main.innerHTML = '<div class="center" style="padding:20px">' + UI.spinner() + '</div>';

    var url = tab === 'sent' ? '/api/gifts/sent' : '/api/gifts' +
      (userParam && userParam !== Number((App.state.user || {}).id) ? '?user=' + userParam : '');

    Api.request('GET', url, null, function (err, data) {
      if (err) {
        main.innerHTML = '<div class="card"><div class="card-b"><div class="alert err">' +
          UI.esc(Api.errorText(err)) + '</div></div></div>';
        return;
      }
      var gifts = (data && data.gifts) || [];
      var isSelf = !userParam || userParam === Number((App.state.user || {}).id);

      function tabLink(key, label, count) {
        return '<a class="tab2' + (tab === key ? ' active' : '') + '" href="#/gifts' +
          (key === 'sent' ? '?tab=sent' : '') + '">' + UI.esc(label) +
          (count ? ' <span class="count" style="margin-left:4px">' + count + '</span>' : '') + '</a>';
      }

      var html = '<div class="card"><div class="card-h">' + UI.icon('gift') + ' ' + T('gifts.title') +
        '<span class="right"><button type="button" class="btn btn-xs" id="gifts-refresh">' +
        UI.icon('rotate') + ' ' + T('gifts.refresh') + '</button></span></div>' +
        '<div class="card-b" style="padding:8px 10px 0"><div class="feed-tabs" style="margin:0">' +
        tabLink('received', T('gifts.received'), data.unseen || 0) +
        tabLink('sent', T('gifts.sentTab')) +
        '</div></div><div class="card-b flush">';

      if (!gifts.length) {
        html += emptyBox('gift', tab === 'sent' ? T('gifts.noSent') : T('gifts.noReceived'),
          tab === 'sent' ? T('gifts.noSentHint') : T('gifts.noReceivedHint'));
      } else {
        for (var i = 0; i < gifts.length; i++) {
          var g = gifts[i];
          var k = kind(g.kind);
          var person = tab === 'sent' ? (g.to || {}) : (g.from || {});
          var who = tab === 'sent' ? (person.displayName || person.username || '?') : senderName(g);
          var href = person.id ? '#/profile/' + person.id : '#/gifts';
          html += '<div class="gift-row">' +
            '<div class="gift-badge" style="--gift-color:' + UI.attr(k.color) + '">' + UI.icon(k.icon) + '</div>' +
            '<div class="gift-row-main"><div class="gift-row-head"><b>' + UI.esc(giftName(g.kind)) + '</b> ' +
            (tab === 'sent' ? T('gifts.to') : T('gifts.from')) + ' ' +
            (g.anonymous && tab !== 'sent' ? '<span class="muted">' + T('gifts.anonName') + '</span>'
              : '<a href="' + UI.attr(href) + '">' + UI.esc(who) + '</a>') +
            '</div>' +
            (g.message ? '<div class="gift-msg">' + UI.esc(g.message) + '</div>' : '') +
            '<div class="tiny muted">' + UI.timeAgo(g.created) + '</div></div>' +
            (tab === 'received' && isSelf ?
              '<button type="button" class="btn btn-xs btn-ghost gift-hide" data-id="' + g.id + '" title="' +
              UI.attr(T('common.delete')) + '">' + UI.icon('trash') + '</button>' : '') +
            '</div>';
        }
      }
      html += '</div></div>';
      main.innerHTML = html;

      var refresh = byId('gifts-refresh');
      if (refresh) refresh.addEventListener('click', function () { App.views.gifts(main, r); });

      var hides = main.querySelectorAll('.gift-hide');
      for (var h = 0; h < hides.length; h++) {
        hides[h].addEventListener('click', function () {
          var id = Number(this.getAttribute('data-id'));
          Api.request('POST', '/api/gifts/delete', { id: id }, function (e2) {
            if (e2) { UI.toast(Api.errorText(e2), 'err'); return; }
            UI.toast(T('gifts.hidden'), 'ok');
            App.views.gifts(main, r);
          });
        });
      }

      // opening the page means the recipient has seen what is waiting
      if (tab === 'received' && isSelf && (data.unseen || 0) > 0) {
        Api.request('POST', '/api/gifts/seen', { id: 0 }, function () {
          if (App.state.me) App.state.me.unreadGifts = 0;
          var badge = main.querySelector('.tab2 .count');
          if (badge && badge.parentNode) badge.parentNode.removeChild(badge);
        });
      }
    });
  };

  return {
    cardHtml: cardHtml,
    bind: bind,
    open: openSend,
    name: giftName
  };
})();
