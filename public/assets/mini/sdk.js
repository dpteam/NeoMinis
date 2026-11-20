/* =========================================================================
   The bridge between a mini-game and the page that hosts it.

   The game runs in an iframe with sandbox="allow-scripts", which means it is on
   an opaque origin: no cookies, no localStorage shared with the site, no way to
   reach into the parent document. That is deliberate - a game is untrusted code
   as far as the site is concerned, even though we wrote it.

   So the game never talks to the API. It talks to the SPA over postMessage, and
   the SPA - which does have the session - submits the score. A game that lies
   about its score can only lie to itself.
   ========================================================================= */
(function () {
  'use strict';

  var params = new URLSearchParams(location.search || '');
  var lang = params.get('lang') === 'en' ? 'en' : 'ru';

  function post(msg) {
    msg.__mini = true;
    try { parent.postMessage(msg, '*'); } catch (e) { }
  }

  /// The parent sends its computed colours, so a game looks like the template the
  /// reader chose instead of like a screenshot from another site.
  function applyTheme(vars) {
    if (!vars) return;
    var root = document.documentElement;
    for (var k in vars) {
      if (!Object.prototype.hasOwnProperty.call(vars, k)) continue;
      if (k.indexOf('--') !== 0) continue;
      root.style.setProperty(k, String(vars[k]));
    }
    // a canvas game cannot read CSS variables while drawing, so it caches the
    // palette and needs to be told when the palette changed
    try {
      document.dispatchEvent(new CustomEvent('minitheme'));
    } catch (e) { }
  }

  window.addEventListener('message', function (e) {
    var d = e.data;
    if (!d || d.__mini !== true) return;
    if (d.type === 'theme') applyTheme(d.vars);
  });

  var bridge = {
    lang: lang,
    /// Two strings, one answer: ru when the reader is on Russian, English otherwise.
    t: function (ru, en) { return lang === 'ru' ? ru : en; },
    ready: function () { post({ type: 'ready' }); },
    sound: function (name) { post({ type: 'sound', name: name }); },
    status: function (text) { post({ type: 'status', text: String(text || '') }); },
    /// The round is over and this is the score. The parent submits it once.
    over: function (score) {
      post({ type: 'over', score: Math.max(0, Math.round(Number(score) || 0)) });
    }
  };

  window.MiniBridge = bridge;
  bridge.ready();
})();
