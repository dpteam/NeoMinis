/* =========================================================================
   CallUI - voice and video calls over WebRTC. The server only relays the
   signalling (SDP + ICE) through the existing long-poll channel; audio and
   video flow directly between the browsers.
   ========================================================================= */
var CallUI = (function () {
  'use strict';

  var call = null;            // the LiveCall we are in
  var peers = {};             // userId -> { pc, stream, pending: [] }
  var local = null;           // local MediaStream
  var muted = false;
  var camOff = false;
  var after = 0;              // last call event id we have seen
  var polling = false;
  var ringTimer = null;
  var ringCall = 0;           // call id currently ringing on this side
  var ringClose = null;       // closes the ring modal
  var startedAt = 0;
  var tickTimer = null;

  function T(k, a, b) { return I18N.t(k, a, b); }
  function byId(id) { return document.getElementById(id); }

  function iceServers() {
    var s = (App.state.site && App.state.site.callStun) || '';
    if (!s) return [];
    var list = [];
    var parts = String(s).split(',');
    for (var i = 0; i < parts.length; i++) {
      var u = parts[i].replace(/^\s+|\s+$/g, '');
      if (u) list.push({ urls: u });
    }
    return list;
  }

  function supported() {
    return !!(window.RTCPeerConnection && navigator.mediaDevices && navigator.mediaDevices.getUserMedia);
  }

  // ------------------------------------------------------------- overlay

  function overlay() { return byId('call-overlay'); }

  function ensureOverlay() {
    var o = overlay();
    if (o) return o;
    o = document.createElement('div');
    o.id = 'call-overlay';
    o.className = 'call-overlay';
    o.innerHTML =
      '<div class="call-panel">' +
      '<div class="call-top">' +
      '<span class="call-title" id="call-title"></span>' +
      '<span class="call-timer" id="call-timer">00:00</span>' +
      '<span class="call-count tiny" id="call-count"></span>' +
      '</div>' +
      '<div class="call-grid" id="call-grid"></div>' +
      '<div class="call-controls">' +
      '<button type="button" class="call-btn" id="call-mic" title="' + UI.attr(T('calls.mic')) + '">' + UI.icon('microphone') + '</button>' +
      '<button type="button" class="call-btn" id="call-cam" title="' + UI.attr(T('calls.cam')) + '">' + UI.icon('video') + '</button>' +
      '<button type="button" class="call-btn call-hang" id="call-hang" title="' + UI.attr(T('calls.hangup')) + '">' + UI.icon('phone-slash') + '</button>' +
      '</div></div>';
    document.body.appendChild(o);
    byId('call-mic').addEventListener('click', toggleMic);
    byId('call-cam').addEventListener('click', toggleCam);
    byId('call-hang').addEventListener('click', hangup);
    return o;
  }

  function tile(user, isLocal) {
    var id = 'call-tile-' + (isLocal ? 'me' : user.id);
    var t = byId(id);
    if (t) return t;
    t = document.createElement('div');
    t.id = id;
    t.className = 'call-tile' + (isLocal ? ' local' : '');
    t.innerHTML = '<video autoplay playsinline' + (isLocal ? ' muted' : '') + '></video>' +
      '<div class="call-name">' + UI.esc(user.displayName || user.username || '?') + '</div>' +
      '<div class="call-state tiny"></div>';
    byId('call-grid').appendChild(t);
    return t;
  }

  function removeTile(userId) {
    var t = byId('call-tile-' + userId);
    if (t && t.parentNode) t.parentNode.removeChild(t);
  }

  function attach(stream, user, isLocal) {
    var t = tile(user, isLocal);
    var v = t.querySelector('video');
    if (v.srcObject !== stream) { try { v.srcObject = stream; } catch (e) { v.src = URL.createObjectURL(stream); } }
    if (!isLocal) paintState(user.id);
  }

  function setTileState(userId, text) {
    var t = byId('call-tile-' + userId);
    if (!t) return;
    var s = t.querySelector('.call-state');
    if (s) s.textContent = text || '';
  }

  /// <summary>Renders "connected · muted" style labels from the two halves.</summary>
  function paintState(userId) {
    var p = peers[userId];
    if (!p) return;
    var parts = [];
    if (p.conn) parts.push(p.conn);
    if (p.med) parts.push(p.med);
    setTileState(userId, parts.join(' · '));
  }

  // --------------------------------------------------------------- media

  function getMedia(video) {
    var constraints = { audio: true, video: !!video };
    return navigator.mediaDevices.getUserMedia(constraints).then(function (stream) {
      local = stream;
      return stream;
    });
  }

  function stopLocal() {
    if (!local) return;
    var tracks = local.getTracks ? local.getTracks() : [];
    for (var i = 0; i < tracks.length; i++) { try { tracks[i].stop(); } catch (e) { } }
    local = null;
  }

  function toggleMic() {
    if (!local) return;
    muted = !muted;
    var tracks = local.getAudioTracks ? local.getAudioTracks() : [];
    for (var i = 0; i < tracks.length; i++) tracks[i].enabled = !muted;
    var b = byId('call-mic');
    if (b) b.className = 'call-btn' + (muted ? ' off' : '');
    if (call) Api.request('POST', '/api/call/media', { id: call.id, muted: muted }, function () { });
  }

  function toggleCam() {
    if (!local) return;
    camOff = !camOff;
    var tracks = local.getVideoTracks ? local.getVideoTracks() : [];
    for (var i = 0; i < tracks.length; i++) tracks[i].enabled = !camOff;
    var b = byId('call-cam');
    if (b) b.className = 'call-btn' + (camOff ? ' off' : '');
    if (call) Api.request('POST', '/api/call/media', { id: call.id, video: !camOff }, function () { });
  }

  // ----------------------------------------------------------- signalling

  function send(to, kind, data) {
    if (!call) return;
    var payload = JSON.stringify({ kind: kind, data: data });
    Api.request('POST', '/api/call/signal', { id: call.id, to: to, payload: payload }, function () { });
  }

  function peerFor(userId, user) {
    var p = peers[userId];
    if (p) return p;
    p = { pc: null, stream: null, pending: [], user: user || { id: userId } };
    peers[userId] = p;
    return p;
  }

  function createPc(userId, user) {
    var p = peerFor(userId, user);
    if (p.pc) return p;
    var pc = new RTCPeerConnection({ iceServers: iceServers() });
    p.pc = pc;
    p.conn = T('calls.connecting');

    if (local) {
      var tracks = local.getTracks ? local.getTracks() : [];
      for (var i = 0; i < tracks.length; i++) { try { pc.addTrack(tracks[i], local); } catch (e) { } }
    }

    pc.onicecandidate = function (e) {
      if (e && e.candidate) send(userId, 'candidate', e.candidate);
    };
    pc.ontrack = function (e) {
      var stream = (e.streams && e.streams[0]) || new MediaStream([e.track]);
      p.stream = stream;
      attach(stream, p.user, false);
    };
    pc.onconnectionstatechange = function () {
      var st = pc.connectionState;
      if (st === 'connected') p.conn = T('calls.connected');
      else if (st === 'connecting' || st === 'new') p.conn = T('calls.connecting');
      else if (st === 'disconnected') p.conn = T('calls.disconnected');
      else if (st === 'failed' || st === 'closed') p.conn = T('calls.failed');
      paintState(userId);
    };
    return p;
  }

  function offerTo(userId, user) {
    var p = createPc(userId, user);
    p.conn = T('calls.connecting');
    paintState(userId);
    p.pc.createOffer().then(function (desc) {
      return p.pc.setLocalDescription(desc).then(function () {
        send(userId, 'offer', { type: desc.type, sdp: desc.sdp });
      });
    }).catch(function () { p.conn = T('calls.failed'); paintState(userId); });
  }

  function onSignal(from, payload) {
    if (!payload || !payload.kind) return;
    var user = null;
    if (call) {
      for (var i = 0; i < call.members.length; i++) if (call.members[i].id === from) user = call.members[i].user;
    }
    var p = peerFor(from, user);

    if (payload.kind === 'offer') {
      createPc(from, user);
      p.pc.setRemoteDescription(new RTCSessionDescription(payload.data)).then(function () {
        flushCandidates(p);
        return p.pc.createAnswer();
      }).then(function (desc) {
        return p.pc.setLocalDescription(desc).then(function () {
          send(from, 'answer', { type: desc.type, sdp: desc.sdp });
        });
      }).catch(function () { setTileState(from, T('calls.failed')); });
      return;
    }

    if (payload.kind === 'answer') {
      if (!p.pc) return;
      p.pc.setRemoteDescription(new RTCSessionDescription(payload.data)).then(function () {
        flushCandidates(p);
      }).catch(function () { });
      return;
    }

    if (payload.kind === 'candidate') {
      if (!p.pc || !p.pc.remoteDescription) { p.pending.push(payload.data); return; }
      try { p.pc.addIceCandidate(new RTCIceCandidate(payload.data)); } catch (e) { }
    }
  }

  function flushCandidates(p) {
    for (var i = 0; i < p.pending.length; i++) {
      try { p.pc.addIceCandidate(new RTCIceCandidate(p.pending[i])); } catch (e) { }
    }
    p.pending = [];
  }

  // -------------------------------------------------------------- events

  function startPolling() {
    if (polling) return;
    polling = true;
    pump();
  }

  /// <summary>
  /// Jumps the cursor to the newest event so a fresh page load does not replay
  /// rings and hang-ups from calls that finished hours ago.
  /// </summary>
  function syncCursor(then) {
    Api.request('GET', '/api/call/wait?after=9223372036854775807&timeout=1', null, function (err, data) {
      if (!err && data && typeof data.latest === 'number') after = data.latest;
      then();
    });
  }

  function pump() {
    if (!polling) return;
    Api.request('GET', '/api/call/wait?after=' + after, null, function (err, data) {
      if (!polling) return;
      if (err) { setTimeout(pump, 4000); return; }
      if (data && data.busy) { setTimeout(pump, 3000); return; }
      var events = (data && data.events) || [];
      for (var i = 0; i < events.length; i++) {
        if (events[i].id > after) after = events[i].id;
        handleEvent(events[i]);
      }
      if (data && data.latest && data.latest > after) after = data.latest;
      pump();
    });
  }

  function handleEvent(e) {
    if (!e || !e.type) return;

    if (e.type === 'incoming') {
      if (call) {   // busy: tell the caller instead of dropping the ring silently
        Api.request('POST', '/api/call/answer', { id: e.call, accept: false }, function () { });
        return;
      }
      showIncoming(e);
      return;
    }

    if (!call) {
      // ringing but not connected yet: the caller gave up, so close the ring
      if (ringCall && (e.type === 'ended' || e.type === 'left') && (!e.call || e.call === ringCall))
        closeRing();
      return;
    }
    if (e.call && e.call !== call.id && e.type !== 'ended') return;

    if (e.type === 'accepted') {
      // the callee picked up: the caller opens the offer
      offerTo(e.from, memberUser(e.from));
      updateMembers(e.payload);
      setTitleForPeer(e.from);
      return;
    }
    if (e.type === 'declined') {
      UI.toast(T('calls.declined'), 'err');
      if (call && call.members.length <= 2) hangup(true);
      return;
    }
    if (e.type === 'joined') {
      updateMembers(e.payload);
      // everyone already in the room offers to the newcomer
      offerTo(e.from, memberUser(e.from));
      return;
    }
    if (e.type === 'left') {
      closePeer(e.from);
      dropMember(e.from);
      updateMembers(call);
      if (!otherAccepted()) { UI.toast(T('calls.ended'), 'info'); hangup(true); return; }
      UI.toast(T('calls.left'), 'info');
      return;
    }
    if (e.type === 'ended') {
      UI.toast(T('calls.ended'), 'info');
      teardown();
      return;
    }
    if (e.type === 'media') {
      updateMembers(e.payload);
      return;
    }
    if (e.type === 'signal') {
      onSignal(e.from, e.payload);
    }
  }

  function memberUser(id) {
    if (!call) return { id: id };
    for (var i = 0; i < call.members.length; i++) if (call.members[i].id === id) return call.members[i].user;
    return { id: id };
  }

  /// <summary>1:1 calls show the other person's name once the call is picked up.</summary>
  function setTitleForPeer(userId) {
    if (!call || call.room) return;
    var u = memberUser(userId);
    var el = byId('call-title');
    if (el && u) el.textContent = u.displayName || u.username || el.textContent;
  }

  function dropMember(id) {
    if (!call || !call.members) return;
    var left = [];
    for (var i = 0; i < call.members.length; i++) if (call.members[i].id !== id) left.push(call.members[i]);
    call.members = left;
  }

  /// <summary>True when somebody else is still on the line.</summary>
  function otherAccepted() {
    if (!call || !call.members) return false;
    for (var i = 0; i < call.members.length; i++)
      if (!call.members[i].mine && call.members[i].accepted) return true;
    return false;
  }

  function updateMembers(payload) {
    if (!payload || typeof payload !== 'object') return;
    if (payload.id) call = payload;
    if (!call || !call.members) return;
    var count = 0;
    for (var i = 0; i < call.members.length; i++) {
      var m = call.members[i];
      if (m.accepted) count++;
      if (m.mine) continue;
      var p = peers[m.id];
      if (!p) continue;
      if (m.muted) p.med = T('calls.mutedUser');
      else if (!m.video && call.kind === 'video') p.med = T('calls.camOffUser');
      else p.med = '';
      paintState(m.id);
    }
    var el = byId('call-count');
    if (el) el.textContent = count + ' / ' + call.members.length;

    // a 1:1 call is titled after the person on the other end
    if (!call.room) {
      for (var j = 0; j < call.members.length; j++) {
        if (call.members[j].mine || !call.members[j].accepted) continue;
        setTitleForPeer(call.members[j].id);
        break;
      }
    }
  }

  function closePeer(userId) {
    var p = peers[userId];
    if (!p) return;
    if (p.pc) { try { p.pc.close(); } catch (e) { } }
    peers[userId] = null;
    delete peers[userId];
    removeTile(userId);
  }

  // ------------------------------------------------------------- lifecycle

  function openOverlay(title, kind) {
    ensureOverlay();
    var o = overlay();
    o.className = 'call-overlay open';
    var t = byId('call-title');
    if (t) t.textContent = title || (kind === 'video' ? T('calls.videoCall') : T('calls.audioCall'));
    var camBtn = byId('call-cam');
    if (camBtn) camBtn.style.display = kind === 'video' ? '' : 'none';
    startedAt = Date.now();
    if (tickTimer) clearInterval(tickTimer);
    tickTimer = setInterval(function () {
      var el = byId('call-timer');
      if (!el) return;
      var s = Math.floor((Date.now() - startedAt) / 1000);
      el.textContent = (s < 600 ? '0' : '') + Math.floor(s / 60) + ':' + ((s % 60) < 10 ? '0' : '') + (s % 60);
    }, 1000);
  }

  function teardown() {
    closeRing();
    for (var k in peers) if (peers.hasOwnProperty(k) && peers[k]) closePeer(k);
    peers = {};
    stopLocal();
    if (tickTimer) { clearInterval(tickTimer); tickTimer = null; }
    var o = overlay();
    if (o) {
      o.className = 'call-overlay';
      var grid = byId('call-grid');
      if (grid) grid.innerHTML = '';
    }
    call = null;
    muted = false;
    camOff = false;
    // the global poller keeps running so the next ring still arrives
  }

  function hangup(silent) {
    var id = call ? call.id : 0;
    if (id) Api.request('POST', '/api/call/leave', { id: id }, function () { });
    teardown();
    if (!silent) UI.toast(T('calls.hungUp'), 'info');
  }

  function startCall(withId, video) {
    if (!supported()) { UI.toast(T('calls.unsupported'), 'err'); return; }
    if (call) { UI.toast(T('calls.already'), 'err'); return; }
    getMedia(video).then(function () {
      Api.request('POST', '/api/call/start', { with: withId, video: !!video }, function (err, data) {
        if (err) { stopLocal(); UI.toast(Api.errorText(err), 'err'); return; }
        call = data.call;
        openOverlay(T('calls.calling'), call.kind);
        attach(local, App.state.user, true);
        var grid = byId('call-grid');
        if (grid && grid.firstChild) grid.insertBefore(byId('call-tile-me'), grid.firstChild);
        updateMembers(call);
        startPolling();
      });
    }).catch(function () {
      UI.toast(T('calls.micDenied'), 'err');
    });
  }

  function joinRoom(room, video) {
    if (!supported()) { UI.toast(T('calls.unsupported'), 'err'); return; }
    if (call) { UI.toast(T('calls.already'), 'err'); return; }
    getMedia(video).then(function () {
      Api.request('POST', '/api/call/start', { room: room, video: !!video }, function (err, data) {
        if (err) { stopLocal(); UI.toast(Api.errorText(err), 'err'); return; }
        call = data.call;
        openOverlay(room, call.kind);
        attach(local, App.state.user, true);
        var grid = byId('call-grid');
        if (grid && grid.firstChild) grid.insertBefore(byId('call-tile-me'), grid.firstChild);
        updateMembers(call);
        startPolling();
      });
    }).catch(function () {
      UI.toast(T('calls.micDenied'), 'err');
    });
  }

  // ---------------------------------------------------------- incoming ring

  function showIncoming(e) {
    var c = e.payload;
    if (!c) return;
    var from = e.from;
    var user = { id: from };
    Api.request('GET', '/api/profile?user=' + from, null, function (err, data) {
      if (!err && data && data.user) user = data.user;
      renderRing(c, user);
    });
  }

  function renderRing(c, user) {
    var body = '<div class="call-ring">' + UI.avatar(user, 64, false) +
      '<div class="call-ring-name">' + UI.esc(user.displayName || user.username || '?') + '</div>' +
      '<div class="tiny muted">' + (c.kind === 'video' ? T('calls.incomingVideo') : T('calls.incomingAudio')) + '</div></div>';

    var close = UI.modal({
      title: T('calls.incoming'),
      icon: 'phone',
      body: body,
      dismissable: false,
      actions: [
        {
          label: T('calls.decline'), cls: 'btn-danger', onClick: function () {
            Api.request('POST', '/api/call/answer', { id: c.id, accept: false }, function () { });
          }
        },
        {
          label: T('calls.accept'), cls: 'btn-ok', icon: 'phone', keepOpen: true,
          onClick: function (box, done) {
            Api.request('POST', '/api/call/answer', { id: c.id, accept: true }, function (err, data) {
              if (err) { UI.toast(Api.errorText(err), 'err'); return; }
              closeRing();
              acceptCall(data.call, user);
            });
            return false;
          }
        }
      ]
    });

    ringCall = c.id;
    ringClose = close;

    if (ringTimer) clearTimeout(ringTimer);
    ringTimer = setTimeout(function () {
      Api.request('POST', '/api/call/answer', { id: c.id, accept: false }, function () { });
      closeRing();
    }, 45000);
  }

  /// <summary>Closes the ringing modal (answered, declined, cancelled or timed out).</summary>
  function closeRing() {
    if (ringClose) { try { ringClose(); } catch (e) { } }
    ringClose = null;
    ringCall = 0;
    if (ringTimer) { clearTimeout(ringTimer); ringTimer = null; }
  }

  function acceptCall(c, user) {
    call = c;
    closeRing();
    getMedia(call.kind === 'video').then(function () {
      openOverlay(user.displayName || user.username, call.kind);
      attach(local, App.state.user, true);
      updateMembers(call);
      startPolling();
    }).catch(function () {
      Api.request('POST', '/api/call/leave', { id: call.id }, function () { });
      teardown();
      UI.toast(T('calls.micDenied'), 'err');
    });
  }

  // ----------------------------------------------------------------- view

  App.views.calls = function (main) {
    main.innerHTML = '<div class="card"><div class="card-h">' + UI.icon('phone') + ' ' + T('calls.title') + '</div>' +
      '<div class="card-b"><div class="tiny muted" style="margin-bottom:8px">' + T('calls.roomHint') + '</div>' +
      '<div class="inline-fields"><div class="field"><label>' + T('calls.room') + '</label>' +
      '<input class="input" id="call-room" maxlength="60" placeholder="team">' +
      '</div></div><div style="display:flex;gap:6px;flex-wrap:wrap">' +
      '<button class="btn btn-primary" id="call-room-audio">' + UI.icon('phone') + ' ' + T('calls.joinAudio') + '</button>' +
      '<button class="btn" id="call-room-video">' + UI.icon('video') + ' ' + T('calls.joinVideo') + '</button>' +
      '</div></div></div>' +
      '<div class="card"><div class="card-h">' + UI.icon('clock-rotate-left') + ' ' + T('calls.history') + '</div>' +
      '<div class="card-b flush" id="call-history"><div class="center" style="padding:18px">' + UI.spinner() + '</div></div></div>';

    function roomName() {
      var v = (byId('call-room') || {}).value || '';
      return v.replace(/^\s+|\s+$/g, '').replace(/\s+/g, '_');
    }

    byId('call-room-audio').addEventListener('click', function () {
      var r = roomName();
      if (r.length < 3) { UI.toast(T('calls.needRoom'), 'err'); return; }
      joinRoom(r, false);
    });
    byId('call-room-video').addEventListener('click', function () {
      var r = roomName();
      if (r.length < 3) { UI.toast(T('calls.needRoom'), 'err'); return; }
      joinRoom(r, true);
    });

    Api.request('GET', '/api/call/history', null, function (err, data) {
      var box = byId('call-history');
      if (!box) return;
      if (err) { box.innerHTML = '<div class="alert err">' + UI.esc(Api.errorText(err)) + '</div>'; return; }
      var list = (data && data.calls) || [];
      if (!list.length) { box.innerHTML = UI.empty('phone', T('calls.noHistory'), T('calls.noHistoryHint')); return; }
      var html = '';
      for (var i = 0; i < list.length; i++) {
        var c = list[i];
        var names = [];
        for (var k = 0; k < c.participants.length; k++) {
          var p = c.participants[k];
          names.push(p.displayName || p.username || '?');
        }
        html += '<div class="call-row">' + UI.icon(c.kind === 'video' ? 'video' : 'phone') +
          '<div class="call-row-main"><div><b>' + UI.esc(c.room || names.join(', ')) + '</b></div>' +
          '<div class="tiny muted">' + UI.formatDate(c.started, true) + ' · ' + fmtDur(c.duration) + '</div></div></div>';
      }
      box.innerHTML = html;
    });
  };

  function fmtDur(sec) {
    var s = Math.max(0, Number(sec) || 0);
    var m = Math.floor(s / 60);
    return m + ':' + ((s % 60) < 10 ? '0' : '') + (s % 60);
  }

  function resumeActive() {
    Api.request('GET', '/api/call/active', null, function (err, data) {
      var list = (err || !data || !data.calls) ? [] : data.calls;
      if (!list.length) return;
      resume(list[0]);
    });
  }

  /// <summary>
  /// Re-joins a call that is still alive on the server (page reloaded mid-call).
  /// Announcing ourselves as accepted makes the other side re-offer.
  /// </summary>
  function resume(c) {
    if (call || !c || !c.members) return;
    var me = null;
    var accepted = 0;
    for (var i = 0; i < c.members.length; i++) {
      if (c.members[i].accepted) accepted++;
      if (c.members[i].mine) me = c.members[i];
    }
    if (!me || !me.accepted) return;
    if (accepted <= 1 && !c.room) return;   // nobody else left on the line

    call = c;
    getMedia(c.kind === 'video').then(function () {
      var title = c.room;
      if (!title) {
        for (var k = 0; k < c.members.length; k++) {
          if (c.members[k].mine) continue;
          var u = c.members[k].user || {};
          title = u.displayName || u.username || T('calls.audioCall');
        }
      }
      openOverlay(title, c.kind);
      attach(local, App.state.user, true);
      updateMembers(c);
      Api.request('POST', '/api/call/answer', { id: c.id, accept: true }, function () { });
      startPolling();
    }).catch(function () { teardown(); });
  }

  return {
    init: function () {
      if (!App.state.authed) return;
      syncCursor(function () {
        startPolling();
        resumeActive();
      });
    },
    stop: function () {
      polling = false;
      teardown();
      after = 0;
    },
    start: startCall,
    joinRoom: joinRoom,
    hangup: hangup,
    active: function () { return !!call; },
    /// <summary>Peer connection states, handy when a call will not connect.</summary>
    debug: function () {
      var out = {};
      for (var k in peers) if (peers.hasOwnProperty(k) && peers[k] && peers[k].pc)
        out[k] = peers[k].pc.connectionState;
      return out;
    }
  };
})();
