/* 2048. Slide, merge, repeat. 4 by 4, no timer, no way to lose except by trying. */
(function () {
  'use strict';

  var B = window.MiniBridge;
  var root = document.getElementById('game');

  var board, score, dead, cells, scoreEl, hintEl;

  function build() {
    root.innerHTML =
      '<div class="mg-top">' +
      '<span class="mg-score" id="mg-score">0</span>' +
      '<span class="mg-label">' + B.t('очков', 'points') + '</span>' +
      '<span class="mg-spacer"></span>' +
      '<button type="button" class="mg-btn" id="mg-restart">' + B.t('Заново', 'Restart') + '</button>' +
      '</div>' +
      '<div class="mg-board"><div class="g2048" id="mg-grid"></div></div>' +
      '<div class="mg-hint" id="mg-hint">' + B.t('Стрелки или свайп', 'Arrow keys or swipe') + '</div>';

    scoreEl = document.getElementById('mg-score');
    hintEl = document.getElementById('mg-hint');
    var grid = document.getElementById('mg-grid');
    var html = '';
    for (var i = 0; i < 16; i++) html += '<div class="cell" data-i="' + i + '" data-v="0"></div>';
    grid.innerHTML = html;
    cells = grid.querySelectorAll('.cell');

    document.getElementById('mg-restart').addEventListener('click', restart);
    document.addEventListener('keydown', onKey);
    bindSwipe(grid);
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
      if (Math.abs(dx) < 20 && Math.abs(dy) < 20) return;
      move(Math.abs(dx) > Math.abs(dy) ? (dx > 0 ? 1 : 3) : (dy > 0 ? 2 : 0));
    }, { passive: true });
  }

  function onKey(e) {
    var k = e.key;
    if (k === 'ArrowUp' || k === 'w' || k === 'W') { move(0); e.preventDefault(); }
    else if (k === 'ArrowRight' || k === 'd' || k === 'D') { move(1); e.preventDefault(); }
    else if (k === 'ArrowDown' || k === 's' || k === 'S') { move(2); e.preventDefault(); }
    else if (k === 'ArrowLeft' || k === 'a' || k === 'A') { move(3); e.preventDefault(); }
  }

  function restart() {
    board = [];
    for (var i = 0; i < 16; i++) board.push(0);
    score = 0;
    dead = false;
    var old = root.querySelector('.mg-over');
    if (old && old.parentNode) old.parentNode.removeChild(old);
    scoreEl.textContent = '0';
    hintEl.textContent = B.t('Стрелки или свайп', 'Arrow keys or swipe');
    spawn();
    spawn();
    render();
  }

  function emptyCells() {
    var list = [];
    for (var i = 0; i < 16; i++) if (!board[i]) list.push(i);
    return list;
  }

  function spawn() {
    var free = emptyCells();
    if (!free.length) return -1;
    var at = free[Math.floor(Math.random() * free.length)];
    board[at] = Math.random() < 0.9 ? 2 : 4;
    return at;
  }

  /// The 16 positions of one line, ordered from the edge the tiles move toward.
  function lineIndexes(dir, i) {
    var idx = [];
    for (var j = 0; j < 4; j++) {
      var r, c;
      if (dir === 0) { r = j; c = i; }
      else if (dir === 2) { r = 3 - j; c = i; }
      else if (dir === 3) { r = i; c = j; }
      else { r = i; c = 3 - j; }
      idx.push(r * 4 + c);
    }
    return idx;
  }

  function move(dir) {
    if (dead) return;
    var changed = false;
    var gained = 0;
    var mergedAt = [];

    for (var i = 0; i < 4; i++) {
      var idx = lineIndexes(dir, i);
      var vals = [];
      for (var j = 0; j < 4; j++) if (board[idx[j]]) vals.push(board[idx[j]]);

      var out = [];
      for (var k = 0; k < vals.length; k++) {
        if (k + 1 < vals.length && vals[k] === vals[k + 1]) {
          var sum = vals[k] * 2;
          out.push(sum);
          gained += sum;
          k++;
        } else {
          out.push(vals[k]);
        }
      }
      while (out.length < 4) out.push(0);

      for (var w = 0; w < 4; w++) {
        if (board[idx[w]] !== out[w]) changed = true;
        board[idx[w]] = out[w];
      }
      // remember which cells hold a merged tile, so they can pop
      for (var m = 0; m < out.length; m++) {
        if (out[m] && vals.length > m && out[m] !== vals[m]) mergedAt.push(idx[m]);
      }
    }

    if (!changed) return;

    score += gained;
    scoreEl.textContent = String(score);
    var at = spawn();
    render();
    if (at >= 0 && cells[at]) cells[at].className = 'cell pop';
    for (var p = 0; p < mergedAt.length; p++) {
      if (cells[mergedAt[p]]) cells[mergedAt[p]].className = 'cell pop';
    }
    if (gained) B.sound('message');

    if (!canMove()) finish();
  }

  function canMove() {
    if (emptyCells().length) return true;
    for (var r = 0; r < 4; r++) {
      for (var c = 0; c < 4; c++) {
        var v = board[r * 4 + c];
        if (c < 3 && board[r * 4 + c + 1] === v) return true;
        if (r < 3 && board[(r + 1) * 4 + c] === v) return true;
      }
    }
    return false;
  }

  function best() {
    var top = 0;
    for (var i = 0; i < 16; i++) if (board[i] > top) top = board[i];
    return top;
  }

  function finish() {
    dead = true;
    B.over(score);
    var over = document.createElement('div');
    over.className = 'mg-over';
    over.innerHTML = '<b>' + B.t('Ходов больше нет', 'No moves left') + '</b>' +
      '<span>' + B.t('Очков: ', 'Score: ') + score + ' · ' +
      B.t('старшая плитка ', 'best tile ') + best() + '</span>' +
      '<button type="button" class="mg-btn" id="mg-again">' + B.t('Ещё раз', 'Play again') + '</button>';
    root.querySelector('.mg-board').appendChild(over);
    document.getElementById('mg-again').addEventListener('click', restart);
  }

  function render() {
    for (var i = 0; i < 16; i++) {
      var el = cells[i];
      if (!el) continue;
      el.setAttribute('data-v', String(board[i] || 0));
      el.textContent = board[i] ? String(board[i]) : '';
    }
  }

  build();
  restart();
})();
