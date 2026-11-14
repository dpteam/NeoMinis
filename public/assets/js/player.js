/* =========================================================================
   Ruffle host page. Loaded only by /play/<id>, never by the SPA.

   That page is embedded in an iframe with sandbox="allow-scripts", so this
   document has an opaque origin: it cannot read cookies, localStorage or the
   parent window. The movie URL arrives in a data- attribute instead of an
   inline script, which keeps the page free of inline JavaScript and lets the
   CSP stay a plain allow-list.
   ========================================================================= */
(function () {
  'use strict';

  var stage = document.getElementById('stage');
  var msg = document.getElementById('msg');

  function fail(text) {
    if (!msg) return;
    msg.textContent = text;
    msg.style.display = 'flex';
  }

  function hideMsg() {
    if (msg) msg.style.display = 'none';
  }

  if (!stage) return;
  var url = stage.getAttribute('data-swf');
  if (!url) { fail('No game'); return; }
  if (!window.RufflePlayer) { fail('Ruffle is not loaded'); return; }

  var R = window.RufflePlayer;
  R.config = {
    autoplay: 'on',
    unmuteOverlay: 'hidden',
    letterbox: 'on',
    scale: 'showAll',
    contextMenu: 'rightClickOnly',
    splashScreen: false,
    warnOnUnsupportedContent: false,
    // no navigation out of the frame and no JS bridge to the host page
    openUrlMode: 'deny',
    allowScriptAccess: false,
    logLevel: 'error',
    publicPath: stage.getAttribute('data-base') || ''
  };

  fetch(url, { credentials: 'same-origin' })
    .then(function (res) {
      if (!res.ok) throw new Error('HTTP ' + res.status);
      return res.arrayBuffer();
    })
    .then(function (buffer) {
      var player = R.newest().createPlayer();
      player.id = 'ruffle';
      stage.appendChild(player);
      hideMsg();
      player.load({ data: buffer });
    })
    .catch(function (e) { fail('Load failed: ' + e.message); });
})();
