/* =========================================================================
   TransferUI - sending a file straight from one browser to another.

   The server never stores the file: it holds the chunks in memory while the
   transfer is alive and forgets them the moment it ends, so nothing lands in
   data/media and nothing survives a restart. The two sides pair with an
   8-digit code, the receiver has to accept before a byte moves, and the
   receiver checks the SHA-256 the sender announced.

   The panel has two independent slots, one outgoing and one incoming, so two
   people can send files to each other at the same time. Only the status area is
   ever repainted; the code box and the send button are built once per
   conversation and never touched, which is what keeps the caret where it is.
   ========================================================================= */
var TransferUI = (function () {
  'use strict';

  var out = null;        // my outgoing transfer
  var inc = null;        // the transfer I am receiving (an offer included)
  var offers = 0;        // unclaimed offers from the person in this conversation
  var timer = null;
  var withId = 0;
  var uploading = false;
  var downloading = false;
  var lastStatus = '';   // what the status area currently shows

  function T(k, a, b) { return I18N.t(k, a, b); }
  function byId(id) { return document.getElementById(id); }
  function site() { return App.state.site || {}; }

  function enabled() { return !!(site().transfersEnabled); }
  function maxBytes() { return (Number(site().maxTransferMB) || 32) * 1024 * 1024; }
  function fmt(bytes) { return UI.bytes(bytes || 0); }

  function pct(done, total) {
    if (!total) return 0;
    var p = Math.round((done / total) * 100);
    return p < 0 ? 0 : p > 100 ? 100 : p;
  }

  // ------------------------------------------------------------------ panel

  /// The bar above the composer. Built once per conversation: the status area is
  /// filled by paint(), everything else stays exactly where it is.
  function panelHtml() {
    if (!enabled()) return '';
    return '<div class="xfer-panel" id="xfer-panel">' +
      '<div class="xfer-status" id="xfer-status"></div>' +
      '<div class="xfer-row">' +
      '<button type="button" class="btn btn-sm" id="xfer-pick">' + UI.icon('paper-plane') + ' ' +
      T('xfer.send') + '</button>' +
      '<input class="input xfer-code-input" id="xfer-code-input" maxlength="9" inputmode="numeric" placeholder="' +
      UI.attr(T('xfer.codePlaceholder')) + '">' +
      '<button type="button" class="btn btn-sm" id="xfer-claim">' + T('xfer.claim') + '</button>' +
      '</div>' +
      '<input type="file" id="xfer-file" class="hidden">' +
      '</div>';
  }

  /// Everything that changes: the incoming slot, then the outgoing one.
  function statusHtml() {
    var html = '';

    if (inc) html += incomingHtml(inc);
    if (out) html += outgoingHtml(out);

    // A code waiting for me matters even while I am sending my own file - that is
    // exactly the two-people-sending-at-once case, and staying silent would make
    // the other person's transfer look like it vanished. Once the offer is open
    // on screen the hint would only be noise, so it hides itself.
    if (offers > 0 && !inc)
      html += '<div class="xfer-hint tiny muted">' + T('xfer.offers', String(offers)) + '</div>';

    return html;
  }

  function outgoingHtml(a) {
    if (a.state === 'done') return line('ok', 'circle-check', T('xfer.delivered', a.name));
    if (a.state === 'cancelled' || a.state === 'expired')
      return line('err', 'circle-exclamation', T('xfer.failedOut', a.name));

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
    return '<div class="xfer-block out">' + body + '</div>';
  }

  function incomingHtml(a) {
    if (a.state === 'done') return line('ok', 'circle-check', T('xfer.received', a.name));
    if (a.state === 'cancelled' || a.state === 'expired' || a.state === 'declined')
      return line('err', 'circle-exclamation', T('xfer.failedIn', a.name));

    var html = '<div class="xfer-main"><b>' + UI.icon('file-arrow-down') + ' ' + UI.esc(a.name) + '</b>' +
      '<span class="tiny muted">' + fmt(a.size) + '</span></div>';
    if (a.state === 'claimed') {
      html += '<div class="xfer-actions">' +
        '<button type="button" class="btn btn-xs btn-primary" id="xfer-accept">' + T('xfer.accept') + '</button>' +
        '<button type="button" class="btn btn-xs btn-danger" id="xfer-decline">' + T('xfer.decline') + '</button>' +
        '</div><div class="xfer-hint tiny muted">' + T('xfer.acceptHint') + '</div>';
    } else {
      html += '<div class="xfer-bar"><i style="width:' + pct(a.sent, a.total) + '%"></i></div>' +
        '<div class="xfer-hint tiny muted">' + T('xfer.receiving', String(pct(a.sent, a.total))) + '</div>';
    }
    return '<div class="xfer-block in">' + html + '</div>';
  }

  function line(kind, icon, text) {
    return '<div class="xfer-block ' + kind + '">' +
      '<div class="xfer-main">' + UI.icon(icon) + ' ' + UI.esc(text) + '</div></div>';
  }

  /// Repaints only the status area. Nothing here can steal focus or eat a
  /// half-typed code, because the input is not part of what gets replaced.
  function paint() {
    var status = byId('xfer-status');
    if (!status) { lastStatus = ''; return; }
    var html = statusHtml();
    if (html === lastStatus) return;
    status.innerHTML = html;
    lastStatus = html;
    bindStatus();
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
    if (out && out.state !== 'done' && out.state !== 'cancelled' && out.state !== 'expired') {
      UI.toast(T('xfer.busy'), 'info');
      return;
    }
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
        out = {
          id: t.id, peer: withId, name: t.fileName, size: t.size, code: t.code,
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
    if (!out || out.state !== 'active' || uploading) return;
    uploading = true;
    var mine = out;
    var chunkSize = (Number(site().transferChunkKB) || 256) * 1024;
    var offset = 0;

    function step() {
      if (out !== mine) { uploading = false; return; }
      if (offset >= mine.total) {
        uploading = false;
        mine.sent = mine.total;
        paint();
        return;
      }
      var end = Math.min(mine.total, offset + chunkSize);
      var slice = mine.file.slice(offset, end);
      var fr = new FileReader();
      fr.onload = function () {
        var b64 = DptCrypto.bytesToBase64(new Uint8Array(fr.result));
        Api.request('POST', '/api/transfer/chunk', {
          id: mine.id, index: Math.floor(offset / chunkSize), data: b64
        }, function (err) {
          if (err) { uploading = false; UI.toast(Api.errorText(err), 'err'); return; }
          offset = end;
          mine.sent = offset;
          paint();
          step();
        });
      };
      fr.onerror = function () { uploading = false; UI.toast(T('errors.generic'), 'err'); };
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
      var box = byId('xfer-code-input');
      if (box) box.value = '';
      adoptIncoming(data && data.transfer);
    });
  }

  function adoptIncoming(t) {
    if (!t) return;
    inc = {
      id: t.id, peer: t.from, name: t.fileName, size: t.size,
      state: t.state, sent: 0, total: t.size,
      sha: t.sha256, chunkCount: t.chunkCount
    };
    paint();
    startPoll();
    if (inc.state === 'active') downloadChunks();
  }

  function accept(id) {
    Api.request('POST', '/api/transfer/accept', { id: id || (inc && inc.id) }, function (err, data) {
      if (err) { UI.toast(Api.errorText(err), 'err'); return; }
      adoptIncoming(data && data.transfer);
    });
  }

  function decline(id) {
    Api.request('POST', '/api/transfer/decline', { id: id || (inc && inc.id) }, function (err, data) {
      if (err) { UI.toast(Api.errorText(err), 'err'); return; }
      adoptIncoming(data && data.transfer);
    });
  }

  function cancel() {
    if (!out) return;
    var id = out.id;
    Api.request('POST', '/api/transfer/cancel', { id: id }, function (err, data) {
      if (err) { UI.toast(Api.errorText(err), 'err'); return; }
      out = {
        id: id, peer: out.peer, name: out.name, size: out.size, code: out.code,
        state: 'cancelled', sent: 0, total: out.size
      };
      paint();
      stopPoll();
    });
  }

  /// Pulls the chunks the sender has put down, checks the hash, then saves.
  function downloadChunks() {
    if (!inc || inc.state !== 'active' || downloading) return;
    downloading = true;
    var mine = inc;
    var count = mine.chunkCount || 1;
    var parts = [];
    var index = 0;
    var received = 0;

    function step() {
      if (inc !== mine) { downloading = false; return; }
      if (index >= count) { finishDownload(mine, parts); return; }
      Api.request('GET', '/api/transfer/chunk?id=' + mine.id + '&index=' + index, null, function (err, data) {
        if (err) {
          // a chunk that is not there yet is normal: the sender is still going
          if (err.code === 'no-chunk') {
            setTimeout(function () {
              if (inc !== mine) { downloading = false; return; }
              downloading = false;
              downloadChunks();
            }, 700);
            return;
          }
          downloading = false;
          UI.toast(Api.errorText(err), 'err');
          return;
        }
        try { parts.push(DptCrypto.base64ToBytes(data.data)); }
        catch (e) { downloading = false; UI.toast(T('errors.generic'), 'err'); return; }
        received += (parts[parts.length - 1] || []).length;
        mine.sent = received;
        paint();
        index++;
        step();
      });
    }
    step();
  }

  function finishDownload(mine, parts) {
    downloading = false;
    var total = 0;
    for (var i = 0; i < parts.length; i++) total += parts[i].length;
    var all = new Uint8Array(total);
    var off = 0;
    for (var j = 0; j < parts.length; j++) { all.set(parts[j], off); off += parts[j].length; }

    var ok = true;
    var digest = '';
    try { digest = DptCrypto.bytesToHex(DptCrypto.sha256(all)); } catch (e) { digest = ''; }
    if (mine.sha && digest && digest !== mine.sha) ok = false;

    Api.request('POST', '/api/transfer/finish', {
      id: mine.id, ok: ok, error: ok ? '' : 'hash mismatch'
    }, function (err, data) {
      if (err) { UI.toast(Api.errorText(err), 'err'); return; }
      adoptIncoming(data && data.transfer);
      if (!ok) { UI.toast(T('xfer.hashBad'), 'err'); return; }
      saveFile(mine.name, all);
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

  /// Follows both slots. The sender needs to learn that the receiver accepted,
  /// and the receiver needs to learn that the sender cancelled or that an offer
  /// expired. An offer addressed to me is shown even while I am sending my own
  /// file, which is the two-people-sending-at-once case.
  function pollOnce() {
    if (!enabled()) { stopPoll(); return; }
    if (App.state.route !== 'im') { stopPoll(); return; }
    var me = Number((App.state.user || {}).id) || 0;
    if (!me) return;
    Api.request('GET', '/api/transfers', null, function (err, data) {
      if (err) return;
      var list = data.transfers || [];
      var pending = 0;
      var foundIn = null;
      var foundOut = null;

      for (var i = 0; i < list.length; i++) {
        var t = list[i];

        // incoming: addressed to me and coming from the person in this chat.
        // An *offered* transfer is not shown yet - the code is the consent gate,
        // so until I type it there is nothing to accept, only a hint that a code
        // is waiting. Otherwise the code would be decoration and anybody in the
        // conversation could be prompted to accept a file they never asked for.
        if (t.to === me && t.from === withId) {
          if (t.state === 'offered') pending++;
          else if (inc && t.id === inc.id) foundIn = t;
          else if (!foundIn && (t.state === 'claimed' || t.state === 'active')) foundIn = t;
        }

        // outgoing: mine, addressed to the person in this chat
        if (t.from === me && t.to === withId) {
          if (out && t.id === out.id) foundOut = t;
        }
      }
      offers = pending;

      if (inc && foundIn && foundIn.id === inc.id) applyIncoming(foundIn);
      else if (foundIn && (!inc || inc.state === 'done' || inc.state === 'cancelled' ||
                           inc.state === 'declined' || inc.state === 'expired')) {
        adoptIncoming(foundIn);
      } else if (inc && !foundIn && inc.state !== 'done' && inc.state !== 'declined') {
        inc.state = 'expired';
        downloading = false;
      }

      if (out && foundOut) applyOutgoing(foundOut);

      paint();
    });
  }

  function applyOutgoing(t) {
    if (t.state === out.state) {
      if (!out.code && t.code) out.code = t.code;
      return;
    }
    var was = out.state;
    out.state = t.state;
    if (t.state === 'active' && was !== 'active') uploadChunks();
    if (t.state === 'done' || t.state === 'cancelled' || t.state === 'expired') {
      uploading = false;
      stopPoll();
    }
  }

  function applyIncoming(t) {
    if (t.state === inc.state) {
      if (t.chunkCount) inc.chunkCount = t.chunkCount;
      return;
    }
    var was = inc.state;
    inc.state = t.state;
    if (t.chunkCount) inc.chunkCount = t.chunkCount;
    if (t.sha256) inc.sha = t.sha256;
    if (t.state === 'active' && was !== 'active') downloadChunks();
    if (t.state === 'done' || t.state === 'cancelled' || t.state === 'expired') {
      downloading = false;
      stopPoll();
    }
  }

  function startPoll() {
    if (timer) return;
    timer = setInterval(pollOnce, 2500);
  }

  function stopPoll() {
    if (timer) { clearInterval(timer); timer = null; }
  }

  // ------------------------------------------------------------------ bind

  /// The fixed row: send button, code box, claim button.
  function bindRow() {
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
  }

  /// The buttons inside the status area, rebound after every repaint.
  function bindStatus() {
    var acc = byId('xfer-accept');
    if (acc) acc.addEventListener('click', function () { accept(); });
    var dec = byId('xfer-decline');
    if (dec) dec.addEventListener('click', function () { decline(); });
    var can = byId('xfer-cancel');
    if (can) can.addEventListener('click', cancel);
  }

  /// Tells the module which conversation is on screen. Called before the chat
  /// body is built, so the panel is generated for the right pair from the start.
  function setPeer(peerId) {
    if (withId !== peerId) {
      // nothing from the old conversation follows us into the new one
      if (out && (out.state === 'done' || out.state === 'cancelled' || out.state === 'expired')) out = null;
      if (inc && (inc.state === 'done' || inc.state === 'declined' ||
                  inc.state === 'cancelled' || inc.state === 'expired')) inc = null;
      offers = 0;
    }
    withId = peerId;
    lastStatus = '';
  }

  /// Called whenever a conversation is rendered.
  function attach(peerId) {
    setPeer(peerId);
    bindRow();
    lastStatus = statusHtml();
    var status = byId('xfer-status');
    if (status) status.innerHTML = lastStatus;
    bindStatus();
    startPoll();
    pollOnce();
  }

  /// Leaving the conversation: drop the poll, keep the transfer state so a
  /// refresh in the middle of an upload does not lose the file.
  function detach() {
    stopPoll();
    lastStatus = '';
  }

  return {
    panelHtml: panelHtml,
    setPeer: setPeer,
    attach: attach,
    detach: detach,
    paint: paint,
    enabled: enabled
  };
})();
