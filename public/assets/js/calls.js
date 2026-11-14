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

  function siteCfg() { return App.state.site || {}; }

  /// <summary>
  /// Every ICE server the browser should try. STUN answers "what is my public
  /// address", TURN relays the media when a direct path cannot exist. Without a
  /// TURN entry two symmetric NATs can never connect, which is the usual reason
  /// a call sits at "connecting" forever.
  /// </summary>
  function iceServers() {
    var list = [];
    var stun = siteCfg().callStun || '';
    var parts = String(stun).split(',');
    for (var i = 0; i < parts.length; i++) {
      var u = parts[i].replace(/^\s+|\s+$/g, '');
      if (u) list.push({ urls: u });
    }
    var turn = siteCfg().callTurn || '';
    var turns = String(turn).split(',');
    for (var j = 0; j < turns.length; j++) {
      var t = turns[j].replace(/^\s+|\s+$/g, '');
      if (!t) continue;
      var server = { urls: t };
      var user = siteCfg().callTurnUser || '';
      if (user) {
        server.username = user;
        server.credential = siteCfg().callTurnPass || '';
      }
      list.push(server);
    }
    return list;
  }

  function hasRelay() {
    return !!(siteCfg().callTurn || '');
  }

  function icePolicy() {
    return (siteCfg().callIcePolicy === 'relay') ? 'relay' : 'all';
  }

  function supported() {
    if (!window.RTCPeerConnection) return false;
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) return false;
    return true;
  }

  /// <summary>Explains why calls cannot work here instead of failing silently.</summary>
  function explainUnsupported() {
    var why = window.UI && UI.mediaBlockedText ? UI.mediaBlockedText() : '';
    UI.toast(why || T('calls.unsupported'), 'err');
  }

  /// <summary>Turns a getUserMedia rejection into something a human can act on.</summary>
  function mediaErrorText(e) {
    var name = (e && e.name) || '';
    if (name === 'NotAllowedError' || name === 'SecurityError') return T('calls.micDenied');
    if (name === 'NotFoundError' || name === 'DevicesNotFoundError') return T('calls.noDevices');
    if (name === 'NotReadableError') return T('calls.deviceBusy');
    if (name === 'OverconstrainedError') return T('calls.noDevices');
    return T('calls.micDenied');
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
      '<button type="button" class="call-btn hidden" id="call-retry" title="' + UI.attr(T('calls.retry')) + '">' + UI.icon('rotate') + '</button>' +
      '<button type="button" class="call-btn call-hang" id="call-hang" title="' + UI.attr(T('calls.hangup')) + '">' + UI.icon('phone-slash') + '</button>' +
      '</div></div>';
    document.body.appendChild(o);
    byId('call-mic').addEventListener('click', toggleMic);
    byId('call-cam').addEventListener('click', toggleCam);
    byId('call-retry').addEventListener('click', retryConnections);
    byId('call-hang').addEventListener('click', hangup);
    return o;
  }

  /// <summary>
  /// Rebuilds every peer connection from scratch. A failed ICE negotiation does
  /// not recover on its own, and asking the two people to hang up and call again
  /// is a poor answer when the media devices are already open.
  /// </summary>
  function retryConnections() {
    var ids = [];
    for (var k in peers) if (peers.hasOwnProperty(k)) ids.push(k);
    if (!ids.length) return;
    for (var i = 0; i < ids.length; i++) dropPeer(ids[i]);
    UI.toast(T('calls.retrying'), 'info');
    // the offerer renegotiates; the other side answers the fresh offer
    for (var j = 0; j < ids.length; j++) {
      if (call && Number(call.initiator) === Number((App.state.user || {}).id)) {
        offerTo(Number(ids[j]), (peers[ids[j]] || {}).user);
      }
    }
    setRetryVisible(false);
  }

  /// Closes one peer connection and forgets it, so the next offer starts clean.
  function dropPeer(userId) {
    var p = peers[userId];
    if (!p) return;
    if (p.statsTimer) { clearInterval(p.statsTimer); p.statsTimer = null; }
    if (p.deadline) { clearTimeout(p.deadline); p.deadline = null; }
    if (p.pc) { try { p.pc.close(); } catch (e) { } }
    var user = p.user;
    delete peers[userId];
    if (user) peers[userId] = { pc: null, stream: null, pending: [], user: user };
  }

  function setRetryVisible(on) {
    var b = byId('call-retry');
    if (!b) return;
    b.className = on ? 'call-btn' : 'call-btn hidden';
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
    // srcObject is the only supported path; the old createObjectURL(stream)
    // fallback was removed from browsers years ago and only hid the failure.
    if (v && v.srcObject !== stream) {
      try { v.srcObject = stream; } catch (e) { }
    }
    if (!isLocal) paintState(user.id);
  }

  function setTileState(userId, text) {
    var t = byId('call-tile-' + userId);
    if (!t) return;
    var s = t.querySelector('.call-state');
    if (s) s.textContent = text || '';
  }

  /// <summary>
  /// Renders the label under a tile: connection state, then the numbers once
  /// there are any. The numbers are the difference between "it does not work"
  /// and "the relay is missing" or "the packet loss is 12%".
  /// </summary>
  function paintState(userId) {
    var p = peers[userId];
    if (!p) return;
    var parts = [];
    if (p.conn) parts.push(p.conn);
    if (p.med) parts.push(p.med);

    if (p.stats && p.conn === T('calls.connected')) {
      var bits = [];
      if (p.stats.kbps) bits.push(p.stats.kbps + ' kbps');
      if (p.stats.rtt) bits.push(p.stats.rtt + ' ms');
      if (p.stats.loss) bits.push(p.stats.loss + '% loss');
      if (p.stats.pair) bits.push((p.stats.relay ? '⤳ ' : '⇄ ') + p.stats.pair);
      if (bits.length) parts.push(bits.join(' · '));
    }

    // A failure with no relay configured is not a mystery: say what to do.
    if (p.conn === T('calls.failed') && !hasRelay()) parts.push(T('calls.noRelayHint'));
    if (p.conn === T('calls.failed') || p.conn === T('calls.disconnected')) setRetryVisible(true);

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
    var cfg = { iceServers: iceServers() };
    var policy = icePolicy();
    if (policy === 'relay') cfg.iceTransportPolicy = 'relay';
    var pc = new RTCPeerConnection(cfg);
    p.pc = pc;
    p.conn = T('calls.connecting');
    p.candidates = { host: 0, srflx: 0, relay: 0 };

    // Show the peer's tile straight away. It used to appear only when media
    // arrived, which meant a connection that failed *before* any track came
    // through had nowhere to print the reason - the reader saw a Reconnect
    // button and no explanation of what went wrong.
    tile(p.user, false);
    paintState(userId);

    if (local) {
      var tracks = local.getTracks ? local.getTracks() : [];
      for (var i = 0; i < tracks.length; i++) { try { pc.addTrack(tracks[i], local); } catch (e) { } }
    }

    pc.onicecandidate = function (e) {
      if (!e || !e.candidate) return;
      send(userId, 'candidate', e.candidate);
      // Count what we actually gathered: a call with zero "relay" candidates on a
      // network that blocks direct paths can never work, and saying so is more
      // useful than "connecting" forever.
      try {
        var type = e.candidate.type || 'host';
        if (p.candidates[type] === undefined) p.candidates[type] = 0;
        p.candidates[type]++;
        paintState(userId);
      } catch (err) { }
    };
    pc.ontrack = function (e) {
      var stream = (e.streams && e.streams[0]) || new MediaStream([e.track]);
      p.stream = stream;
      attach(stream, p.user, false);
    };
    pc.oniceconnectionstatechange = function () {
      p.ice = pc.iceConnectionState;
      if (pc.iceConnectionState === 'failed') p.conn = T('calls.failed');
      paintState(userId);
      if (pc.iceConnectionState === 'failed') statsFor(userId);
    };
    pc.onconnectionstatechange = function () {
      var st = pc.connectionState;
      if (st === 'connected') { p.conn = T('calls.connected'); connectedNow(userId); }
      else if (st === 'connecting' || st === 'new') p.conn = T('calls.connecting');
      else if (st === 'disconnected') p.conn = T('calls.disconnected');
      else if (st === 'failed' || st === 'closed') { p.conn = T('calls.failed'); statsFor(userId); }
      paintState(userId);
    };

    // Chromium does not always fire "failed": with no usable candidates at all a
    // connection can sit at "connecting" indefinitely, which is exactly the
    // "the call never connects and says nothing" complaint. Give it a deadline.
    p.deadline = setTimeout(function () {
      if (!p.pc || p.conn === T('calls.connected')) return;
      p.conn = T('calls.failed');
      paintState(userId);
      statsFor(userId);
    }, 22000);

    return p;
  }

  /// Clears the deadline and starts the quality numbers.
  function connectedNow(userId) {
    var p = peers[userId];
    if (!p) return;
    if (p.deadline) { clearTimeout(p.deadline); p.deadline = null; }
    setRetryVisible(false);
    statsLoop(userId);
  }

  /// <summary>
  /// One getStats() snapshot: which candidate pair won, and what the media is
  /// actually doing. "It sounds bad" becomes a number, and "it never connects"
  /// becomes a visible reason.
  /// </summary>
  function statsFor(userId) {
    var p = peers[userId];
    if (!p || !p.pc) return;
    try {
      p.pc.getStats(null).then(function (report) {
        var out = { pair: null, rtt: 0, kbps: 0, loss: 0, relay: false };
        var byId = {};
        report.forEach(function (s) { byId[s.id] = s; });
        report.forEach(function (s) {
          if (s.type === 'candidate-pair' && (s.selected || s.state === 'succeeded')) {
            out.rtt = Math.round(s.currentRoundTripTime ? s.currentRoundTripTime * 1000 : 0);
            var local = byId[s.localCandidateId];
            var remote = byId[s.remoteCandidateId];
            if (local) {
              out.pair = (local.candidateType || '?') + ' → ' + ((remote && remote.candidateType) || '?');
              if (local.candidateType === 'relay' || (remote && remote.candidateType === 'relay')) out.relay = true;
            }
          }
          if (s.type === 'inbound-rtp' && !s.isRemote) {
            if (s.bytesReceived !== undefined && p.lastBytes !== undefined && s.timestamp && p.lastTs) {
              var dBytes = s.bytesReceived - p.lastBytes;
              var dt = (s.timestamp - p.lastTs) / 1000;
              if (dt > 0) out.kbps = Math.round((dBytes * 8) / dt / 1000);
            }
            if (s.bytesReceived !== undefined) p.lastBytes = s.bytesReceived;
            if (s.timestamp) p.lastTs = s.timestamp;
            if (s.packetsLost !== undefined && s.packetsReceived !== undefined) {
              var total = s.packetsLost + s.packetsReceived;
              out.loss = total > 0 ? Math.round((s.packetsLost / total) * 1000) / 10 : 0;
            }
          }
        });
        p.stats = out;
        paintState(userId);
      }).catch(function () { });
    } catch (e) { }
  }

  function statsLoop(userId) {
    var p = peers[userId];
    if (!p) return;
    if (p.statsTimer) clearInterval(p.statsTimer);
    statsFor(userId);
    p.statsTimer = setInterval(function () {
      if (!peers[userId] || !peers[userId].pc) { clearInterval(p.statsTimer); return; }
      statsFor(userId);
    }, 3000);
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

  /// <summary>
  /// Glare: both sides offered at the same time, which happens whenever two
  /// people join a room together. Only one offer may win, otherwise both
  /// setRemoteDescription calls land on a connection in the wrong state and the
  /// call sits at "connecting" forever. The higher user id yields, which is
  /// arbitrary but stable and known to both sides.
  /// </summary>
  function shouldYieldOffer(myId, peerId, signalingState) {
    if (signalingState === 'stable') return false;   // no clash at all
    return Number(myId) > Number(peerId);
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

      var me = Number((App.state.user || {}).id) || 0;
      var yields = shouldYieldOffer(me, from, p.pc.signalingState);
      if (p.pc.signalingState !== 'stable' && !yields) return;   // our offer stands
      if (yields) p.pc.setLocalDescription({ type: 'rollback' }).catch(function () { });

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
      // a late answer to an offer we already rolled back is not an error
      if (p.pc.signalingState !== 'have-local-offer') return;
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
    // the channel page may be watching: put its idle panel back
    if (App.state.callsIntroReset) {
      try { App.state.callsIntroReset(); } catch (e) { }
    }
    // the global poller keeps running so the next ring still arrives
  }

  function hangup(silent) {
    var id = call ? call.id : 0;
    if (id) Api.request('POST', '/api/call/leave', { id: id }, function () { });
    teardown();
    if (!silent) UI.toast(T('calls.hungUp'), 'info');
  }

  function startCall(withId, video) {
    if (!supported()) { explainUnsupported(); return; }
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
    }).catch(function (e) {
      UI.toast(mediaErrorText(e), 'err');
    });
  }

  /// <summary>
  /// Joins (or opens) a named voice room. `onFail` runs when the attempt dies
  /// before the overlay is up, so the caller can restore whatever it was showing.
  /// </summary>
  function joinRoom(room, video, onFail) {
    if (!supported()) { explainUnsupported(); if (onFail) onFail(); return; }
    if (call) { UI.toast(T('calls.already'), 'err'); if (onFail) onFail(); return; }
    getMedia(video).then(function () {
      Api.request('POST', '/api/call/start', { room: room, video: !!video }, function (err, data) {
        if (err) { stopLocal(); UI.toast(Api.errorText(err), 'err'); if (onFail) onFail(); return; }
        call = data.call;
        openOverlay(room, call.kind);
        attach(local, App.state.user, true);
        var grid = byId('call-grid');
        if (grid && grid.firstChild) grid.insertBefore(byId('call-tile-me'), grid.firstChild);
        updateMembers(call);
        startPolling();
      });
    }).catch(function (e) {
      UI.toast(mediaErrorText(e), 'err');
      if (onFail) onFail();
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
    var channels = [];

    main.innerHTML = '<div class="chan-layout">' +
      '<aside class="chan-side">' +
      '<div class="chan-side-head"><span>' + UI.icon('hashtag') + ' ' + T('calls.channels') + '</span>' +
      '<button type="button" class="icon-btn" id="chan-new" title="' + UI.attr(T('calls.newChannel')) + '">' +
      UI.icon('plus') + '</button></div>' +
      '<div class="chan-list" id="chan-list"><div class="center" style="padding:16px">' + UI.spinner() + '</div></div>' +
      '<div class="chan-side-foot tiny muted">' + T('calls.channelsHint') + '</div>' +
      '</aside>' +
      '<section class="chan-main">' +
      '<div class="card"><div class="card-h">' + UI.icon('phone') + ' ' + T('calls.title') + '</div>' +
      '<div class="card-b" id="chan-intro">' +
      UI.empty('hashtag', T('calls.pickChannel'), T('calls.pickChannelHint')) + '</div></div>' +
      '<div class="card"><div class="card-h">' + UI.icon('clock-rotate-left') + ' ' + T('calls.history') + '</div>' +
      '<div class="card-b flush" id="call-history"><div class="center" style="padding:18px">' + UI.spinner() + '</div></div></div>' +
      '</section></div>';

    function loadChannels() {
      Api.request('GET', '/api/channels', null, function (err, data) {
        var box = byId('chan-list');
        if (!box) return;
        if (err) {
          box.innerHTML = '<div class="alert err" style="margin:8px">' + UI.esc(Api.errorText(err)) + '</div>';
          return;
        }
        channels = (data && data.channels) || [];
        if (!channels.length) {
          box.innerHTML = '<div class="tiny muted" style="padding:10px">' + T('calls.noChannels') + '</div>';
          return;
        }
        var html = '';
        for (var i = 0; i < channels.length; i++) {
          var c = channels[i];
          var inside = c.members || [];
          html += '<div class="chan-row' + (inside.length ? ' busy' : '') + '" data-id="' + c.id + '">' +
            '<button type="button" class="chan-join" data-id="' + c.id + '" title="' +
            UI.attr(T('calls.joinAudio')) + '">' +
            UI.icon(inside.length ? 'volume-high' : 'hashtag') +
            '<span class="chan-name">' + UI.esc(c.name) + '</span>' +
            (inside.length ? '<span class="chan-count">' + inside.length + '</span>' : '') +
            '</button>';
          if (inside.length) {
            html += '<div class="chan-avatars">';
            for (var k = 0; k < inside.length && k < 5; k++) {
              html += '<span title="' + UI.attr(inside[k].displayName || inside[k].username) + '">' +
                UI.avatar(inside[k], 20) + '</span>';
            }
            if (inside.length > 5) html += '<span class="tiny muted">+' + (inside.length - 5) + '</span>';
            html += '</div>';
          }
          html += '<div class="chan-actions">' +
            '<button type="button" class="icon-btn chan-video" data-id="' + c.id + '" title="' +
            UI.attr(T('calls.joinVideo')) + '">' + UI.icon('video') + '</button>' +
            (c.canManage ? '<button type="button" class="icon-btn chan-edit" data-id="' + c.id + '" title="' +
              UI.attr(T('common.edit')) + '">' + UI.icon('pen') + '</button>' : '') +
            '</div></div>';
        }
        box.innerHTML = html;
        bindChannelRows(box);
      });
    }

    function bindChannelRows(box) {
      var joins = box.querySelectorAll('.chan-join');
      for (var i = 0; i < joins.length; i++) {
        joins[i].addEventListener('click', function () {
          var c = channelById(this.getAttribute('data-id'));
          if (c) joinChannel(c, false);
        });
      }
      var vids = box.querySelectorAll('.chan-video');
      for (var k = 0; k < vids.length; k++) {
        vids[k].addEventListener('click', function (e) {
          e.stopPropagation();
          var c = channelById(this.getAttribute('data-id'));
          if (c) joinChannel(c, true);
        });
      }
      var edits = box.querySelectorAll('.chan-edit');
      for (var m = 0; m < edits.length; m++) {
        edits[m].addEventListener('click', function (e) {
          e.stopPropagation();
          var c = channelById(this.getAttribute('data-id'));
          if (c) channelModal(c);
        });
      }
    }

    function channelById(id) {
      for (var i = 0; i < channels.length; i++) if (String(channels[i].id) === String(id)) return channels[i];
      return null;
    }

    function joinChannel(c, video) {
      if (call) { UI.toast(T('calls.already'), 'err'); return; }
      var intro = byId('chan-intro');
      if (intro) {
        intro.innerHTML = '<div class="center" style="padding:18px">' + UI.spinner() + ' ' +
          UI.esc(T('calls.joining', c.name)) + '</div>';
      }
      // A join can fail for reasons that are not an error dialog: the microphone
      // is missing, the room filled up, the browser refused. Put the idle panel
      // back so the page does not sit on a spinner that will never finish.
      joinRoom(c.name, video, function () {
        var box = byId('chan-intro');
        if (!box) return;
        box.innerHTML = UI.empty('hashtag', T('calls.pickChannel'), T('calls.pickChannelHint'));
      });
    }

    /// Creates a channel, or edits and deletes one that is already there.
    function channelModal(c) {
      var isNew = !c;
      var body = '<div class="field"><label>' + T('calls.channelName') + '</label>' +
        '<input class="input" id="chan-name" maxlength="40" value="' + UI.attr(isNew ? '' : c.name) + '">' +
        '<div class="hint">' + T('calls.channelNameHint') + '</div></div>' +
        '<div class="field"><label>' + T('calls.channelTopic') + '</label>' +
        '<input class="input" id="chan-topic" maxlength="120" value="' + UI.attr(isNew ? '' : c.topic) + '"></div>';

      var actions = [{ label: T('common.cancel'), cls: 'btn-ghost' }];
      if (!isNew) {
        actions.push({
          label: T('common.delete'), cls: 'btn-danger',
          onClick: function (box, close) {
            UI.confirm(T('common.delete'), T('calls.deleteChannelConfirm'), T('common.delete'), function () {
              Api.request('POST', '/api/channel/delete', { id: c.id }, function (err) {
                if (err) { UI.toast(Api.errorText(err), 'err'); return; }
                UI.toast(T('calls.channelDeleted'), 'ok');
                if (close) close();
                loadChannels();
              });
            }, true);
            return false;
          }
        });
      }
      actions.push({
        label: isNew ? T('common.create') : T('common.save'), cls: 'btn-primary', keepOpen: true,
        onClick: function (box, close) {
          var name = (byId('chan-name') || {}).value || '';
          var topic = (byId('chan-topic') || {}).value || '';
          var path = isNew ? '/api/channel/create' : '/api/channel/edit';
          var payload = isNew ? { name: name, topic: topic } : { id: c.id, name: name, topic: topic };
          Api.request('POST', path, payload, function (err) {
            if (err) { UI.toast(Api.errorText(err), 'err'); return; }
            UI.toast(isNew ? T('calls.channelCreated') : T('common.saved'), 'ok');
            SoundFX.play('success');
            if (close) close();
            loadChannels();
          });
          return false;
        }
      });

      UI.modal({
        title: isNew ? T('calls.newChannel') : T('calls.editChannel'),
        icon: 'hashtag',
        body: body,
        actions: actions
      });
    }

    var newBtn = byId('chan-new');
    if (newBtn) newBtn.addEventListener('click', function () { channelModal(null); });

    loadChannels();
    // occupancy is a live fact, so refresh it while the page is on screen.
    // While this tab is in a call the panel above the feed is the truth, so the
    // list only needs to keep ticking when nobody is connected here.
    if (App.state.callsTicker) clearInterval(App.state.callsTicker);
    App.state.callsTicker = setInterval(function () {
      if (App.state.route !== 'calls' || document.hidden) return;
      if (!byId('chan-list')) { clearInterval(App.state.callsTicker); return; }
      loadChannels();
    }, 6000);
    // and put the idle panel back whenever a call ends, so the page never sits
    // on a "joining" spinner that will not finish
    App.state.callsIntroReset = function () {
      var box = byId('chan-intro');
      if (box) box.innerHTML = UI.empty('hashtag', T('calls.pickChannel'), T('calls.pickChannelHint'));
      loadChannels();
    };

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
          '<div class="tiny muted">' + UI.formatDate(c.started, true) + ' \u00b7 ' + fmtDur(c.duration) + '</div></div></div>';
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
    // exposed for the test harness: the glare decision is the one piece of call
    // logic that can be checked without two browsers and two microphones
    shouldYieldOffer: shouldYieldOffer,
    iceServers: iceServers,
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
