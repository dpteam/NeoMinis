/* =========================================================================
   TransferUI - sending a file straight from one browser to another.

   The server never stores the file: it holds the chunks in memory while the
   transfer is alive and forgets them the moment it ends, so nothing lands in
   data/media and nothing survives a restart. The two sides pair with an
   8-digit code, the receiver has to accept before a byte moves, and the
   receiver checks the SHA-256 the sender announced.

   One transfer is active per conversation at a time, which is enough to be
   useful and keeps the panel readable.
   ========================================================================= */
var TransferUI = (function () {
  'use strict';

  var active = null;     // the transfer being sent or received right now
  var offers = 0;        // how many offers wait for this user
  var timer = null;
  var withId = 0;
  var working = false;   // chunk loop is running, do not start a second one

  function T(k, a, b) { return I18N.t(k, a, b); }
  function byId(id) { return document.getElementById(id); }
  function site() { return App.state.site || {}; }

  function enabled() {
    return !!(site().transfersEnabled);
  }

  function maxBytes() {
    return (Number(site().maxTransferMB) || 32) * 1024 * 1024;
  }

  function fmt(bytes) {
    return UI.bytes(bytes || 0);
  }

  function pct(done, total) {
    if (!total) return 0;
    var p = Math.round((done / total) * 100);
    return p < 0 ? 0 : p > 100 ? 100 : p;
  }

  // ------------------------------------------------------------------ panel

  /// The bar above the composer. Rebuilt from `active`, so a chat refresh in
  /// the middle of an upload does not lose the progress.
  function panelHtml() {
    if (!enabled()) return '';
    var a = active;

    if (a && a.dir === 'out') {
      if (a.state === 'done') {
        return box('ok', 'circle-check', T('xfer.delivered', a.name), '');
      }
      if (a.state === 'cancelled' || a.state === 'expired') {
        return box('err', 'circle-exclamation', T('xfer.failedOut', a.name), '');
      }
      var body = '<div class="xfer-main"><b>' + UI.icon('paper-plane') + ' ' + UI.esc(a.name) + '</b>' +
        '<span class="tiny muted">' + fmt(a.size) + '</span></div>' +
        '<div class="xfer-code">' + T('xfer.code') + ' <b>' + UI.esc(a.code) + '</b></div>';
      if (a.state === 'offered' || a.state === 'claimed') {
        body += '<div class="xfer-hint tiny muted">' +
          (a.state === 'offered' ? T('xfer.waitingCode') : T('xfer.waitingAccept')) + '</div>';
      } else {
        body += '<div class="xfer-bar"><i style="width:' + pct(a.sent, a.total) + '%"></i></div>' +
          '<div class="xfer-hint tiny muted">' + T('xfer.sending', String(pct(a.sent, a.total))) + '</div>';
      }
      body += '<button type="button" class="btn btn-xs btn-ghost" id="xfer-cancel">' + T('common.cancel') + '</button>';
      return '<div class="xfer-panel out" id="xfer-panel">' + body + '</div>';
    }

    if (a && a.dir === 'in') {
      if (a.state === 'done') {
        return box('ok', 'circle-check', T('xfer.received', a.name), '');
      }
      if (a.state === 'cancelled' || a.state === 'expired') {
        return box('err', 'circle-exclamation', T('xfer.failedIn', a.name), '');
      }
      var html = '<div class="xfer-main"><b>' + UI.icon('file-arrow-down') + ' ' + UI.esc(a.name) + '</b>' +
        '<span class="tiny muted">' + fmt(a.size) + '</span></div>';
      if (a.state === 'offered') {
        html += '<div class="xfer-actions">' +
          '<button type="button" class="btn btn-xs btn-primary" id="xfer-accept">' + T('xfer.accept') + '</button>' +
          '<button type="button" class="btn btn-xs btn-danger" id="xfer-decline">' + T('xfer.decline') + '</button>' +
          '</div><div class="xfer-hint tiny muted">' + T('xfer.acceptHint') + '</div>';
      } else if (a.state === 'claimed') {
        html += '<div class="xfer-actions">' +
          '<button type="button" class="btn btn-xs btn-primary" id="xfer-accept">' + T('xfer.accept') + '</button>' +
          '<button type="button" class="btn btn-xs btn-danger" id="xfer-decline">' + T('xfer.decline') + '</button>' +
          '</div>';
      } else {
        html += '<div class="xfer-bar"><i style="width:' + pct(a.sent, a.total) + '%"></i></div>' +
          '<div class="xfer-hint tiny muted">' + T('xfer.receiving', String(pct(a.sent, a.total))) + '</div>';
      }
      return '<div class="xfer-panel in" id="xfer-panel">' + html + '</div>';
    }

    // idle: offer the two ways in, plus a code box
    var idle = '<div class="xfer-row">' +
      '<button type="button" class="btn btn-sm" id="xfer-pick">' + UI.icon('paper-plane') + ' ' +
      T('xfer.send') + '</button>' +
      '<input class="input xfer-code-input" id="xfer-code-input" maxlength="9" inputmode="numeric" placeholder="' +
      UI.attr(T('xfer.codePlaceholder')) + '">' +
      '<button type="button" class="btn btn-sm" id="xfer-claim">' + T('xfer.claim') + '</button>' +
      '</div>' +
      '<input type="file" id="xfer-file" class="hidden">' +
      (offers ? '<div class="xfer-hint tiny muted">' + T('xfer.offers', String(offers)) + '</div>' : '');
    return '<div class="xfer-panel" id="xfer-panel">' + idle + '</div>';
  }

  function box(kind, icon, text, extra) {
    return '<div class="xfer-panel ' + kind + '" id="xfer-panel">' +
      '<div class="xfer-main">' + UI.icon(icon) + ' ' + UI.esc(text) + '</div>' + (extra || '') + '</div>';
  }

  /// Repaints only the panel, so the message list and the composer stay put.
  function paint() {
    var old = byId('xfer-panel');
    if (!old) return;
    var holder = document.createElement('div');
    holder.innerHTML = panelHtml();
    var fresh = holder.firstChild;
    if (fresh) old.parentNode.replaceChild(fresh, old);
    bindPanel();
  }

  // ------------------------------------------------------------------ send

  function pickFile() {
    var input = byId('xfer-file');
    if (!input) return;
    input.value = '';
    input.click();
  }

  function startSend(file) {
    if (!file) return;
    if (file.size > maxBytes()) {
      UI.toast(T('xfer.tooBig', String(site().maxTransferMB || 32)), 'err');
      return;
    }
    var reader = new FileReader();
    reader.onload = function () {
      var bytes = new Uint8Array(reader.result);
      var digest = '';
      // sha256Hex() hashes a *string*; a file is bytes, so hash them directly
      try { digest = DptCrypto.bytesToHex(DptCrypto.sha256(bytes)); } catch (e) { digest = ''; }
      Api.request('POST', '/api/transfer/start', {
        to: withId, name: file.name, size: file.size, sha256: digest
      }, function (err, data) {
        if (err) { UI.toast(Api.errorText(err), 'err'); return; }
        var t = data && data.transfer;
        if (!t) return;
        active = {
          id: t.id, dir: 'out', name: t.fileName, size: t.size, code: t.code,
          state: t.state, sent: 0, total: t.size, file: file, sha: digest
        };
        SoundFX.play('success');
        paint();
        startPoll();
      });
    };
    reader.onerror = function () { UI.toast(T('errors.generic'), 'err'); };
    reader.readAsArrayBuffer(file);
  }

  /// Pushes the file in chunks, one request at a time, updating the bar.
  function uploadChunks() {
    if (!active || active.dir !== 'out' || active.state !== 'active' || working) return;
    working = true;
    var chunkSize = (Number(site().transferChunkKB) || 256) * 1024;
    var total = active.total;
    var offset = 0;

    function step() {
      if (!active || active.id == null || active.dir !== 'out') { working = false; return; }
      if (offset >= total) {
        working = false;
        active.sent = total;
        paint();
        return;
      }
      var end = Math.min(total, offset + chunkSize);
      var slice = active.file.slice(offset, end);
      var fr = new FileReader();
      fr.onload = function () {
        var b64 = DptCrypto.bytesToBase64(new Uint8Array(fr.result));
        Api.request('POST', '/api/transfer/chunk', {
          id: active.id, index: Math.floor(offset / chunkSize), data: b64
        }, function (err) {
          if (err) {
            working = false;
            UI.toast(Api.errorText(err), 'err');
            return;
          }
          offset = end;
          active.sent = offset;
          paint();
          step();
        });
      };
      fr.onerror = function () { working = false; UI.toast(T('errors.generic'), 'err'); };
      fr.readAsArrayBuffer(slice);
    }
    step();
  }

  // ---------------------------------------------------------------- receive

  function claim(code) {
    code = String(code || '').replace(/[^0-9]/g, '');
    if (code.length !== 8) { UI.toast(T('xfer.badCode'), 'err'); return; }
    Api.request('POST', '/api/transfer/claim', { code: code }, function (err, data) {
      if (err) { UI.toast(Api.errorText(err), 'err'); return; }
      adopt(data && data.transfer, 'in');
    });
  }

  function adopt(t, dir) {
    if (!t) return;
    active = {
      id: t.id, dir: dir || (t.mine ? 'out' : 'in'), name: t.fileName, size: t.size,
      code: t.code || (active && active.id === t.id ? active.code : ''),
      state: t.state, sent: 0, total: t.size,
      sha: t.sha256, chunkCount: t.chunkCount
    };
    paint();
    startPoll();
    if (active.state === 'active' && active.dir === 'out') uploadChunks();
    if (active.state === 'active' && active.dir === 'in') downloadChunks();
  }

  function accept() {
    if (!active) return;
    Api.request('POST', '/api/transfer/accept', { id: active.id }, function (err, data) {
      if (err) { UI.toast(Api.errorText(err), 'err'); return; }
      adopt(data && data.transfer, 'in');
    });
  }

  function decline() {
    if (!active) return;
    Api.request('POST', '/api/transfer/decline', { id: active.id }, function (err, data) {
      if (err) { UI.toast(Api.errorText(err), 'err'); return; }
      adopt(data && data.transfer, 'in');
      stopPoll();
    });
  }

  function cancel() {
    if (!active) return;
    Api.request('POST', '/api/transfer/cancel', { id: active.id }, function (err, data) {
      if (err) { UI.toast(Api.errorText(err), 'err'); return; }
      adopt(data && data.transfer, active.dir);
      stopPoll();
    });
  }

  /// Pulls the chunks the sender has put down, checks the hash, then saves.
  function downloadChunks() {
    if (!active || active.dir !== 'in' || active.state !== 'active' || working) return;
    working = true;
    var id = active.id;
    var count = active.chunkCount || 1;
    var parts = [];
    var index = 0;
    var received = 0;

    function step() {
      if (!active || active.id !== id) { working = false; return; }
      if (index >= count) { finishDownload(parts, id); return; }
      Api.request('GET', '/api/transfer/chunk?id=' + id + '&index=' + index, null, function (err, data) {
        if (err) {
          working = false;
          // a chunk that is not there yet is normal: the sender is still going
          if (err.code === 'no-chunk') { setTimeout(function () { working = false; downloadChunks(); }, 700); return; }
          UI.toast(Api.errorText(err), 'err');
          return;
        }
        try { parts.push(DptCrypto.base64ToBytes(data.data)); }
        catch (e) { working = false; UI.toast(T('errors.generic'), 'err'); return; }
        received += (parts[parts.length - 1] || []).length;
        active.sent = received;
        paint();
        index++;
        step();
      });
    }
    step();
  }

  function finishDownload(parts, id) {
    working = false;
    var total = 0;
    for (var i = 0; i < parts.length; i++) total += parts[i].length;
    var all = new Uint8Array(total);
    var off = 0;
    for (var j = 0; j < parts.length; j++) { all.set(parts[j], off); off += parts[j].length; }

    var ok = true;
    var digest = '';
    try { digest = DptCrypto.bytesToHex(DptCrypto.sha256(all)); } catch (e) { digest = ''; }
    if (active.sha && digest && digest !== active.sha) ok = false;

    Api.request('POST', '/api/transfer/finish', {
      id: id, ok: ok, error: ok ? '' : 'hash mismatch'
    }, function (err, data) {
      if (err) { UI.toast(Api.errorText(err), 'err'); return; }
      adopt(data && data.transfer, 'in');
      stopPoll();
      if (!ok) { UI.toast(T('xfer.hashBad'), 'err'); return; }
      saveFile(active ? active.name : 'file', all);
      SoundFX.play('success');
    });
  }

  function saveFile(name, bytes) {
    try {
      var blob = new Blob([bytes], { type: 'application/octet-stream' });
      var url = URL.createObjectURL(blob);
      var a = document.createElement('a');
      a.href = url;
      a.download = name || 'file';
      document.body.appendChild(a);
      a.click();
      setTimeout(function () {
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
      }, 1500);
    } catch (e) { UI.toast(T('errors.generic'), 'err'); }
  }

  // ------------------------------------------------------------------- poll

  /// Follows the transfer the panel is showing. The sender needs to learn that
  /// the receiver accepted, and the receiver needs to learn that the sender
  /// cancelled or that the offer expired.
  function pollOnce() {
    if (!enabled()) { stopPoll(); return; }
    if (App.state.route !== 'im') { stopPoll(); return; }
    Api.request('GET', '/api/transfers', null, function (err, data) {
      if (err) return;
      offers = Number(data.offers || 0);
      var list = data.transfers || [];
      var mine = null;
      for (var i = 0; i < list.length; i++) {
        var t = list[i];
        if (active && t.id === active.id) { mine = t; break; }
      }
      if (!mine) {
        // the one we were following is gone from the list: forget it
        if (active && active.state !== 'done') {
          active.state = 'expired';
          paint();
        }
        if (!active) paint();
        return;
      }
      if (mine.state !== active.state) {
        var was = active.state;
        active.state = mine.state;
        active.chunkCount = mine.chunkCount || active.chunkCount;
        active.sha = mine.sha256 || active.sha;
        if (mine.state === 'active' && was !== 'active') {
          if (active.dir === 'out') uploadChunks(); else downloadChunks();
        }
        paint();
        if (mine.state === 'done' || mine.state === 'cancelled' || mine.state === 'expired') stopPoll();
      } else if (!active.code && mine.code) {
        active.code = mine.code;
        paint();
      }
      if (!active) paint();
    });
  }

  function startPoll() {
    if (timer) return;
    timer = setInterval(pollOnce, 2500);
  }

  function stopPoll() {
    if (timer) { clearInterval(timer); timer = null; }
  }

  // ------------------------------------------------------------------ bind

  function bindPanel() {
    var pick = byId('xfer-pick');
    if (pick) pick.addEventListener('click', pickFile);

    var input = byId('xfer-file');
    if (input) input.addEventListener('change', function () {
      startSend(input.files && input.files[0]);
    });

    var claimBtn = byId('xfer-claim');
    if (claimBtn) claimBtn.addEventListener('click', function () {
      var box = byId('xfer-code-input');
      claim(box ? box.value : '');
    });

    var codeBox = byId('xfer-code-input');
    if (codeBox) {
      codeBox.addEventListener('keydown', function (e) {
        if (e.keyCode === 13) { e.preventDefault(); claim(codeBox.value); }
      });
      // 1234-5678 as it is read out loud
      codeBox.addEventListener('input', function () {
        var digits = this.value.replace(/[^0-9]/g, '').slice(0, 8);
        this.value = digits.length > 4 ? digits.slice(0, 4) + '-' + digits.slice(4) : digits;
      });
    }

    var acc = byId('xfer-accept');
    if (acc) acc.addEventListener('click', accept);
    var dec = byId('xfer-decline');
    if (dec) dec.addEventListener('click', decline);
    var can = byId('xfer-cancel');
    if (can) can.addEventListener('click', cancel);
  }

  /// Called whenever a conversation is rendered.
  function attach(peerId) {
    withId = peerId;
    if (!active || active.state === 'done' || active.state === 'cancelled' || active.state === 'expired') {
      // keep a finished one on screen only until the chat is left
      if (active && active.state !== 'done') active = null;
    }
    bindPanel();
    startPoll();
    pollOnce();
  }

  /// Leaving the conversation: drop the panel state, keep the payload safe.
  function detach() {
    stopPoll();
    working = false;
  }

  return {
    panelHtml: panelHtml,
    attach: attach,
    detach: detach,
    paint: paint,
    enabled: enabled
  };
})();
