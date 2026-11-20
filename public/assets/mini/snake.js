/* Snake. 16 by 16, one apple, and a snake that gets faster until it does not. */
(function () {
  'use strict';

  var B = window.MiniBridge;
  var root = document.getElementById('game');
  var N = 16;
  var STEP_START = 155;     // ms per move at the start
  var STEP_MIN = 68;        // and at the end, however long you survive

  var canvas, ctx, scoreEl, hintEl;
  var snake, dir, queued, apple, score, dead, stepMs, acc, last, raf;
  var palette = {};

  function readPalette() {
    var cs = getComputedStyle(document.documentElement);
    function v(name, fallback) {
      var s = cs.getPropertyValue(name);
      return s && s.trim() ? s.trim() : fallback;
    }
    palette = {
      accent: v('--accent', '#4472a4'),
      panel: v('--panel', '#ffffff'),
      panel2: v('--panel-2', '#f2f5f9'),
      border: v('--border', '#d3dae4'),
      text: v('--text', '#1f2733')
    };
  }

  function build() {
    root.innerHTML =
      '<div class="mg-top">' +
      '<span class="mg-score" id="mg-score">0</span>' +
      '<span class="mg-label">' + B.t('очков', 'points') + '</span>' +
      '<span class="mg-spacer"></span>' +
      '<button type="button" class="mg-btn" id="mg-restart">' + B.t('Заново', 'Restart') + '</button>' +
      '</div>' +
      '<div class="mg-board"><canvas class="mg-canvas" id="mg-canvas" width="384" height="384"></canvas></div>' +
      '<div class="mg-pad">' +
      '<button type="button" class="up" data-dir="0">&#9650;</button>' +
      '<button type="button" class="left" data-dir="3">&#9664;</button>' +
      '<button type="button" class="down" data-dir="2">&#9660;</button>' +
      '<button type="button" class="right" data-dir="1">&#9654;</button>' +
      '</div>' +
      '<div class="mg-hint" id="mg-hint">' + B.t('Стрелки или свайп', 'Arrow keys or swipe') + '</div>';

    canvas = document.getElementById('mg-canvas');
    ctx = canvas.getContext('2d');
    scoreEl = document.getElementById('mg-score');
    hintEl = document.getElementById('mg-hint');
    document.getElementById('mg-restart').addEventListener('click', restart);

    var pads = root.querySelectorAll('.mg-pad button');
    for (var i = 0; i < pads.length; i++) {
      pads[i].addEventListener('click', function () {
        turn(Number(this.getAttribute('data-dir')));
      });
    }

    document.addEventListener('keydown', onKey);
    bindSwipe(canvas);
    document.addEventListener('minitheme', function () { readPalette(); draw(); });
  }

  function bindSwipe(el) {
    var sx = 0, sy = 0, tracking = false;
    el.addEventListener('touchstart', function (e) {
      if (!e.touches || !e.touches.length) return;
      sx = e.touches[0].clientX; sy = e.touches[0].clientY; tracking = true;
    }, { passive: true });
    el.addEventListener('touchend', function (e) {
      if (!tracking) return;
      tracking = false;
      var t = (e.changedTouches && e.changedTouches[0]) || null;
      if (!t) return;
      var dx = t.clientX - sx, dy = t.clientY - sy;
      if (Math.abs(dx) < 18 && Math.abs(dy) < 18) return;
      if (Math.abs(dx) > Math.abs(dy)) turn(dx > 0 ? 1 : 3);
      else turn(dy > 0 ? 2 : 0);
    }, { passive: true });
  }

  // 0 up, 1 right, 2 down, 3 left
  function onKey(e) {
    var k = e.key;
    if (k === 'ArrowUp' || k === 'w' || k === 'W') { turn(0); e.preventDefault(); }
    else if (k === 'ArrowRight' || k === 'd' || k === 'D') { turn(1); e.preventDefault(); }
    else if (k === 'ArrowDown' || k === 's' || k === 'S') { turn(2); e.preventDefault(); }
    else if (k === 'ArrowLeft' || k === 'a' || k === 'A') { turn(3); e.preventDefault(); }
  }

  function turn(d) {
    if (dead) return;
    // a 180 turn is a bite, so it is dropped rather than punished
    if ((d + 2) % 4 === dir) return;
    queued = d;
  }

  function restart() {
    // a fresh round must clear the "game over" panel from the last one, or the
    // board restarts underneath a message that still says the game is finished
    var old = root.querySelector('.mg-over');
    if (old && old.parentNode) old.parentNode.removeChild(old);

    snake = [{ x: 8, y: 8 }, { x: 7, y: 8 }, { x: 6, y: 8 }];
    dir = 1;
    queued = 1;
    score = 0;
    dead = false;
    stepMs = STEP_START;
    acc = 0;
    last = 0;
    placeApple();
    scoreEl.textContent = '0';
    hintEl.textContent = B.t('Стрелки или свайп', 'Arrow keys or swipe');
    draw();
    if (raf) cancelAnimationFrame(raf);
    raf = requestAnimationFrame(loop);
  }

  function placeApple() {
    var free = [];
    for (var y = 0; y < N; y++) {
      for (var x = 0; x < N; x++) {
        var taken = false;
        for (var i = 0; i < snake.length; i++) {
          if (snake[i].x === x && snake[i].y === y) { taken = true; break; }
        }
        if (!taken) free.push({ x: x, y: y });
      }
    }
    apple = free.length ? free[Math.floor(Math.random() * free.length)] : null;
  }

  function loop(ts) {
    if (dead) return;
    if (!last) last = ts;
    var dt = ts - last;
    last = ts;
    acc += dt;
    while (acc >= stepMs) {
      acc -= stepMs;
      step();
      if (dead) return;
    }
    draw();
    raf = requestAnimationFrame(loop);
  }

  function step() {
    dir = queued;
    var head = snake[0];
    var nx = head.x + (dir === 1 ? 1 : dir === 3 ? -1 : 0);
    var ny = head.y + (dir === 2 ? 1 : dir === 0 ? -1 : 0);

    if (nx < 0 || ny < 0 || nx >= N || ny >= N) return die();
    // the tail moves out of the way this step, so it is not a wall
    for (var i = 0; i < snake.length - 1; i++) {
      if (snake[i].x === nx && snake[i].y === ny) return die();
    }

    snake.unshift({ x: nx, y: ny });
    if (apple && nx === apple.x && ny === apple.y) {
      score += 10;
      scoreEl.textContent = String(score);
      B.sound('message');
      if (stepMs > STEP_MIN) stepMs -= 4;
      placeApple();
      if (!apple) return die();     // board full: nothing left to eat
    } else {
      snake.pop();
    }
  }

  function die() {
    if (dead) return;
    dead = true;
    if (raf) cancelAnimationFrame(raf);
    draw();
    B.over(score);
    // one panel, ever: a second one would sit on top of the first and the
    // "play again" button underneath would be unreachable
    var stale = root.querySelector('.mg-over');
    if (stale && stale.parentNode) stale.parentNode.removeChild(stale);
    var over = document.createElement('div');
    over.className = 'mg-over';
    over.innerHTML = '<b>' + B.t('Игра окончена', 'Game over') + '</b>' +
      '<span>' + B.t('Очков: ', 'Score: ') + score + '</span>' +
      '<button type="button" class="mg-btn" id="mg-again">' + B.t('Ещё раз', 'Play again') + '</button>';
    root.querySelector('.mg-board').appendChild(over);
    document.getElementById('mg-again').addEventListener('click', restart);
  }

  function draw() {
    if (!ctx) return;
    var size = canvas.width;
    var cell = size / N;
    ctx.fillStyle = palette.panel;
    ctx.fillRect(0, 0, size, size);

    // a very faint grid: enough to read distances, not enough to distract
    ctx.strokeStyle = palette.border;
    ctx.globalAlpha = 0.35;
    ctx.lineWidth = 1;
    for (var g = 1; g < N; g++) {
      ctx.beginPath();
      ctx.moveTo(g * cell, 0); ctx.lineTo(g * cell, size);
      ctx.moveTo(0, g * cell); ctx.lineTo(size, g * cell);
      ctx.stroke();
    }
    ctx.globalAlpha = 1;

    if (apple) {
      ctx.fillStyle = palette.accent;
      ctx.beginPath();
      ctx.arc(apple.x * cell + cell / 2, apple.y * cell + cell / 2, cell * 0.34, 0, Math.PI * 2);
      ctx.fill();
    }

    for (var i = snake.length - 1; i >= 0; i--) {
      var s = snake[i];
      var inset = i === 0 ? 1.5 : 2.5;
      ctx.fillStyle = i === 0 ? palette.text : palette.accent;
      ctx.globalAlpha = i === 0 ? 1 : Math.max(0.35, 1 - i / (snake.length + 6));
      roundRect(s.x * cell + inset, s.y * cell + inset, cell - inset * 2, cell - inset * 2, 3);
      ctx.fill();
    }
    ctx.globalAlpha = 1;
  }

  function roundRect(x, y, w, h, r) {
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.lineTo(x + w - r, y);
    ctx.quadraticCurveTo(x + w, y, x + w, y + r);
    ctx.lineTo(x + w, y + h - r);
    ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
    ctx.lineTo(x + r, y + h);
    ctx.quadraticCurveTo(x, y + h, x, y + h - r);
    ctx.lineTo(x, y + r);
    ctx.quadraticCurveTo(x, y, x + r, y);
    ctx.closePath();
  }

  readPalette();
  build();
  restart();
})();
