/* =========================================================================
   UI - DOM helpers, safe rendering, toasts, modals, themes. ES5 only.
   Everything user supplied goes through esc() or textContent.
   ========================================================================= */
var UI = (function () {
  'use strict';

  function $(sel, root) { return (root || document).querySelector(sel); }
  function $$(sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); }

  function esc(s) {
    if (s === null || s === undefined) return '';
    return String(s)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  function attr(s) { return esc(s).replace(/\n/g, ' '); }

  function nl2br(s) {
    return esc(s).replace(/\r\n/g, '\n').replace(/\r/g, '\n').replace(/\n/g, '<br>');
  }

  function safeUrl(url) {
    if (!url) return '';
    var u = String(url).trim();
    if (/^https?:\/\//i.test(u)) return u;
    if (u.charAt(0) === '/' && u.charAt(1) !== '/') return u;
    return '';
  }

  function linkify(text) {
    var safe = esc(text);
    var urls = [];
    safe = safe.replace(/(https?:\/\/[^\s<]+[^\s<.,:;"')\]\}])/gi, function (m) {
      var href = m.replace(/&amp;/g, '&');
      urls.push('<a href="' + attr(href) + '" target="_blank" rel="noopener noreferrer nofollow">' + m + '</a>');
      return '\u0001' + (urls.length - 1) + '\u0001';
    });
    // @mentions -> profile links (only when a real @ starts the word)
    safe = safe.replace(/(^|[^\w@\/])@([A-Za-z0-9_.-]{3,24})/g, function (m, pre, name) {
      return pre + '<a class="mention" href="#/u/' + encodeURIComponent(name) + '">@' + name + '</a>';
    });
    // #hashtags -> tag pages (latin, digits, underscore, cyrillic)
    safe = safe.replace(/(^|[^\w#&\/])#([0-9A-Za-z_\u0400-\u04FF]{2,40})/g, function (m, pre, tag) {
      return pre + '<a class="tag" href="#/tag/' + encodeURIComponent(tag) + '">#' + tag + '</a>';
    });
    return safe.replace(/\u0001(\d+)\u0001/g, function (m, i) { return urls[Number(i)] || ''; });
  }

  function timeAgo(iso) {
    if (!iso) return '';
    var t = Date.parse(iso);
    if (isNaN(t)) return '';
    var diff = Math.max(0, Math.floor((Date.now() - t) / 1000));
    if (diff < 45) return I18N.t('time.justNow');
    if (diff < 3600) return I18N.t('time.minute', Math.floor(diff / 60));
    if (diff < 86400) return I18N.t('time.hour', Math.floor(diff / 3600));
    if (diff < 604800) return I18N.t('time.day', Math.floor(diff / 86400));
    return formatDate(iso, false);
  }

  function formatDate(iso, withTime) {
    if (!iso) return '';
    var d = new Date(iso);
    if (isNaN(d.getTime())) return '';
    try {
      var opts = withTime
        ? { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }
        : { year: 'numeric', month: 'short', day: 'numeric' };
      return d.toLocaleString(I18N.getLang(), opts);
    } catch (e) {
      return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate());
    }
  }

  function pad(n) { return n < 10 ? '0' + n : String(n); }

  function initials(name) {
    var s = String(name || '?').trim();
    if (!s) return '?';
    var parts = s.split(/\s+/);
    if (parts.length > 1 && parts[0] && parts[1]) return (parts[0].charAt(0) + parts[1].charAt(0)).toUpperCase();
    return s.charAt(0).toUpperCase();
  }

  function avatar(user, size, rounded) {
    var u = user || {};
    var px = size || 32;
    var cls = 'avatar a' + (Math.abs(Number(u.id) || 0) % 8) + (rounded === false ? ' sq' : ' rd');
    var style = 'width:' + px + 'px;height:' + px + 'px;font-size:' + Math.max(10, Math.round(px * 0.4)) + 'px';
    var src = safeUrl(u.avatar);
    var inner = src
      ? '<img src="' + attr(src) + '" alt="">'
      : esc(initials(u.displayName || u.username));
    return '<span class="' + cls + '" style="' + style + '">' + inner + '</span>';
  }

  function icon(name, extra) {
    return '<i class="fa-solid fa-' + attr(name) + (extra ? ' ' + attr(extra) : '') + '"></i>';
  }

  function userLink(user, cls) {
    var u = user || {};
    var href = '#/u/' + encodeURIComponent(u.username || '');
    return '<a class="' + (cls || '') + '" href="' + attr(href) + '">' + esc(u.displayName || u.username || '?') + '</a>';
  }

  function roleBadge(role) {
    if (role === 'admin') return '<span class="badge admin">' + icon('shield-halved') + ' admin</span>';
    if (role === 'moderator') return '<span class="badge mod">' + icon('gavel') + ' mod</span>';
    return '';
  }

  function presence(user) {
    return '<span class="presence-dot ' + (user && user.online ? 'on' : '') + '"></span>';
  }

  function spinner() { return '<span class="spinner"></span>'; }

  function empty(iconName, title, hint) {
    return '<div class="empty">' + icon(iconName || 'inbox') +
      '<b>' + esc(title || I18N.t('feed.empty')) + '</b>' +
      (hint ? '<span>' + esc(hint) + '</span>' : '') + '</div>';
  }

  // ------------------------------------------------------------------ toast

  function toast(message, type, timeout) {
    var root = document.getElementById('toast-root');
    if (!root) return;
    var el = document.createElement('div');
    el.className = 'toast ' + (type || 'info');
    var ic = type === 'err' ? 'circle-exclamation' : type === 'ok' ? 'circle-check' : 'circle-info';
    el.innerHTML = icon(ic) + '<div>' + esc(message) + '</div>';
    root.appendChild(el);
    var ms = timeout || (type === 'err' ? 5200 : 3200);
    setTimeout(function () {
      el.className += ' out';
      setTimeout(function () { if (el.parentNode) el.parentNode.removeChild(el); }, 280);
    }, ms);
  }

  // ------------------------------------------------------------------ modal

  var modalStack = [];

  function modal(opts) {
    opts = opts || {};
    var root = document.getElementById('modal-root');
    if (!root) return function () { };

    var scrim = document.createElement('div');
    scrim.className = 'scrim';
    var box = document.createElement('div');
    box.className = 'modal' + (opts.wide ? ' wide' : '');
    var title = opts.title ? '<div class="modal-h">' + icon(opts.icon || 'window-restore') + '<span>' + esc(opts.title) + '</span><button class="icon-btn x" type="button" aria-label="' + attr(I18N.t('common.close')) + '">' + icon('xmark') + '</button></div>' : '';
    var footer = '';
    if (opts.actions && opts.actions.length) {
      footer = '<div class="modal-f">';
      for (var i = 0; i < opts.actions.length; i++) {
        var a = opts.actions[i];
        footer += '<button type="button" class="btn ' + (a.cls || '') + '" data-action="' + i + '">' + (a.icon ? icon(a.icon) + ' ' : '') + esc(a.label) + '</button>';
      }
      footer += '</div>';
    }
    box.innerHTML = title + '<div class="modal-b">' + (opts.body || '') + '</div>' + footer;
    scrim.appendChild(box);
    root.appendChild(scrim);

    function close() {
      if (scrim.parentNode) scrim.parentNode.removeChild(scrim);
      for (var j = modalStack.length - 1; j >= 0; j--) if (modalStack[j] === close) modalStack.splice(j, 1);
      if (opts.onClose) opts.onClose();
    }

    function onKey(e) {
      if (e.key === 'Escape') { e.preventDefault(); close(); }
    }
    document.addEventListener('keydown', onKey);

    scrim.addEventListener('click', function (e) {
      if (e.target === scrim && opts.dismissable !== false) close();
    });
    var x = box.querySelector('.x');
    if (x) x.addEventListener('click', close);

    var buttons = box.querySelectorAll('[data-action]');
    for (var k = 0; k < buttons.length; k++) {
      buttons[k].addEventListener('click', function () {
        var idx = Number(this.getAttribute('data-action'));
        var action = opts.actions[idx];
        if (action && action.onClick) {
          var result = action.onClick(box, close);
          if (result !== false && action.keepOpen !== true) close();
        } else close();
      });
    }

    var oldClose = close;
    close = function () {
      document.removeEventListener('keydown', onKey);
      oldClose();
    };
    modalStack.push(close);
    setTimeout(function () {
      var f = box.querySelector('input, textarea, select, button.btn-primary');
      if (f) { try { f.focus(); } catch (e) { } }
    }, 30);
    return close;
  }

  function confirmBox(title, message, okLabel, onOk, danger) {
    return modal({
      title: title,
      icon: danger ? 'triangle-exclamation' : 'circle-question',
      body: '<div style="font-size:13px;line-height:1.55">' + esc(message) + '</div>',
      actions: [
        { label: I18N.t('common.cancel'), cls: 'btn-ghost' },
        { label: okLabel || I18N.t('common.confirm'), cls: danger ? 'btn-danger' : 'btn-primary', onClick: onOk }
      ]
    });
  }

  function imageModal(src) {
    var safe = safeUrl(src);
    if (!safe) return;
    return modal({
      title: '',
      wide: true,
      body: '<img class="modal-img" src="' + attr(safe) + '" alt="">',
      actions: [{ label: I18N.t('common.close'), cls: 'btn-ghost' }]
    });
  }

  // ------------------------------------------------------------------ theme

  var THEMES = ['light', 'dark', 'oled'];

  function getTheme() {
    try {
      var t = localStorage.getItem('social.theme');
      if (t && THEMES.indexOf(t) >= 0) return t;
    } catch (e) { }
    return 'light';
  }

  function applyTheme(name) {
    if (THEMES.indexOf(name) < 0) name = 'light';
    document.documentElement.setAttribute('data-theme', name);
    try { localStorage.setItem('social.theme', name); } catch (e) { }
    var meta = document.querySelector('meta[name="theme-color"]');
    if (meta) meta.setAttribute('content', name === 'light' ? '#4a76a8' : name === 'dark' ? '#1b2c3f' : '#06101a');
    return name;
  }

  function cycleTheme() {
    var cur = getTheme();
    var next = THEMES[(THEMES.indexOf(cur) + 1) % THEMES.length];
    applyTheme(next);
    return next;
  }

  // ------------------------------------------------------------------ misc

  function copy(text) {
    var s = String(text || '');
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(s);
      return true;
    }
    var ta = document.createElement('textarea');
    ta.value = s;
    ta.style.position = 'fixed';
    ta.style.left = '-9999px';
    document.body.appendChild(ta);
    ta.select();
    var ok = false;
    try { ok = document.execCommand('copy'); } catch (e) { }
    document.body.removeChild(ta);
    return ok;
  }

  function fileToDataUrl(file, maxBytes, cb) {
    if (!file) return cb('no-file');
    if (file.size > maxBytes) return cb('too-large');
    if (!/^image\/(png|jpe?g|gif|webp)$/i.test(file.type || '')) return cb('bad-type');
    if (!window.FileReader) return cb('no-reader');
    var reader = new FileReader();
    reader.onload = function () { cb(null, String(reader.result)); };
    reader.onerror = function () { cb('read-error'); };
    reader.readAsDataURL(file);
  }

  function debounce(fn, wait) {
    var t = null;
    return function () {
      var args = arguments, self = this;
      if (t) clearTimeout(t);
      t = setTimeout(function () { t = null; fn.apply(self, args); }, wait);
    };
  }

  function throttle(fn, wait) {
    var last = 0;
    return function () {
      var now = Date.now();
      if (now - last < wait) return;
      last = now;
      fn.apply(this, arguments);
    };
  }

  function scrollTop() {
    try { window.scrollTo(0, 0); } catch (e) { }
  }

  function bytes(n) {
    n = Number(n) || 0;
    if (n < 1024) return n + ' B';
    if (n < 1048576) return (n / 1024).toFixed(1) + ' KB';
    if (n < 1073741824) return (n / 1048576).toFixed(1) + ' MB';
    return (n / 1073741824).toFixed(2) + ' GB';
  }

  function query(name) {
    var m = new RegExp('[?&]' + name + '=([^&]*)').exec(window.location.hash || '');
    return m ? decodeURIComponent(m[1]) : null;
  }

  function on(root, event, selector, handler) {
    (root || document).addEventListener(event, function (e) {
      var target = e.target;
      while (target && target !== root) {
        if (target.matches && target.matches(selector)) { handler.call(target, e); return; }
        target = target.parentNode;
      }
    });
  }

  return {
    $: $, $$: $$, esc: esc, attr: attr, nl2br: nl2br, linkify: linkify,
    safeUrl: safeUrl, timeAgo: timeAgo, formatDate: formatDate, initials: initials,
    avatar: avatar, icon: icon, userLink: userLink, roleBadge: roleBadge, presence: presence,
    spinner: spinner, empty: empty, toast: toast, modal: modal, confirm: confirmBox,
    imageModal: imageModal, getTheme: getTheme, applyTheme: applyTheme, cycleTheme: cycleTheme,
    themes: THEMES, copy: copy, fileToDataUrl: fileToDataUrl, debounce: debounce,
    throttle: throttle, scrollTop: scrollTop, bytes: bytes, query: query, on: on, pad: pad
  };
})();
