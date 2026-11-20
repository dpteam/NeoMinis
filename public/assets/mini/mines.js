/* Minesweeper. Nine by nine, ten mines, and a first click that is never a mine. */
(function () {
  'use strict';

  var B = window.MiniBridge;
  var root = document.getElementById('game');

  var W = 9, H = 9, MINES = 10;

  var mine, open, flag, placed, over, revealed, squares;
  var scoreEl, hintEl, gridEl, flagsEl;

  function build() {
    root.innerHTML =
      '<div class="mg-top">' +
      '<span class="mg-score" id="mg-score">0</span>' +
      '<span class="mg-label">' + B.t('открыто', 'opened') + '</span>' +
      '<span class="mg-spacer"></span>' +
      '<span class="mg-label" id="mg-flags">10</span>' +
      '<span class="mg-label">' + B.t('мин', 'mines') + '</span>' +
      '<button type="button" class="mg-btn" id="mg-restart">' + B.t('Заново', 'Restart') + '</button>' +
      '</div>' +
      '<div class="mg-board"><div class="gmines" id="mg-grid"></div></div>' +
      '<div class="mg-hint" id="mg-hint">' +
      B.t('Клик — открыть, правый клик — флажок', 'Click to open, right-click to flag') + '</div>';

    scoreEl = document.getElementById('mg-score');
    hintEl = document.getElementById('mg-hint');
    flagsEl = document.getElementById('mg-flags');
    gridEl = document.getElementById('mg-grid');
    document.getElementById('mg-restart').addEventListener('click', restart);
  }

  function restart() {
    mine = {};
    open = {};
    flag = {};
    placed = false;
    over = false;
    revealed = 0;
    if (timer) { clearInterval(timer); timer = null; }
    var old = root.querySelector('.mg-over');
    if (old && old.parentNode) old.parentNode.removeChild(old);

    var html = '';
    for (var i = 0; i < W * H; i++) html += '<button type="button" class="sq" data-i="' + i + '"></button>';
    gridEl.innerHTML = html;
    squares = gridEl.querySelectorAll('.sq');
    for (var k = 0; k < squares.length; k++) bindSquare(squares[k]);

    scoreEl.textContent = '0';
    flagsEl.textContent = String(MINES);
    hintEl.textContent = B.t('Клик — открыть, правый клик — флажок',
      'Click to open, right-click to flag');
  }

  var timer = null;

  function bindSquare(el) {
    el.addEventListener('click', function () {
      if (flag[this.getAttribute('data-i')]) return;
      reveal(Number(this.getAttribute('data-i')));
    });
    el.addEventListener('contextmenu', function (e) {
      e.preventDefault();
      toggleFlag(Number(this.getAttribute('data-i')));
    });
    // long press flags on a touch screen, where there is no right button
    var hold = null;
    el.addEventListener('touchstart', function (e) {
      var idx = Number(this.getAttribute('data-i'));
      hold = setTimeout(function () {
        hold = null;
        toggleFlag(idx);
        if (navigator.vibrate) navigator.vibrate(12);
      }, 420);
    }, { passive: true });
    el.addEventListener('touchend', function () {
      if (hold) { clearTimeout(hold); hold = null; }
    }, { passive: true });
    el.addEventListener('touchmove', function () {
      if (hold) { clearTimeout(hold); hold = null; }
    }, { passive: true });
  }

  function neighbours(i) {
    var x = i % W, y = Math.floor(i / W);
    var list = [];
    for (var dy = -1; dy <= 1; dy++) {
      for (var dx = -1; dx <= 1; dx++) {
        if (!dx && !dy) continue;
        var nx = x + dx, ny = y + dy;
        if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
        list.push(ny * W + nx);
      }
    }
    return list;
  }

  function countNear(i) {
    var n = 0, list = neighbours(i);
    for (var k = 0; k < list.length; k++) if (mine[list[k]]) n++;
    return n;
  }

  /// Mines are placed on the first click, and never on that square or its
  /// neighbours: an opening move that loses is not a game, it is a coin toss.
  function place(first) {
    var banned = {};
    banned[first] = true;
    var nb = neighbours(first);
    for (var k = 0; k < nb.length; k++) banned[nb[k]] = true;

    var pool = [];
    for (var i = 0; i < W * H; i++) if (!banned[i]) pool.push(i);
    for (var s = pool.length - 1; s > 0; s--) {
      var j = Math.floor(Math.random() * (s + 1));
      var t = pool[s]; pool[s] = pool[j]; pool[j] = t;
    }
    for (var m = 0; m < MINES && m < pool.length; m++) mine[pool[m]] = true;
    placed = true;
  }

  function reveal(i) {
    if (over || open[i] || flag[i]) return;
    if (!placed) place(i);
    if (mine[i]) return boom(i);

    // flood fill: an empty square opens its neighbours, which is the whole
    // pleasure of the game
    var stack = [i];
    while (stack.length) {
      var cur = stack.pop();
      if (open[cur] || flag[cur] || mine[cur]) continue;
      open[cur] = true;
      revealed++;
      var el = squares[cur];
      if (el) {
        var near = countNear(cur);
        el.className = 'sq open' + (near ? ' n' + near : '');
        el.textContent = near ? String(near) : '';
      }
      if (countNear(cur) === 0) {
        var nb = neighbours(cur);
        for (var k = 0; k < nb.length; k++) if (!open[nb[k]] && !flag[nb[k]]) stack.push(nb[k]);
      }
    }
    scoreEl.textContent = String(revealed);
    B.sound('message');
    if (revealed >= W * H - MINES) win();
  }

  function toggleFlag(i) {
    if (over || open[i]) return;
    if (flag[i]) {
      delete flag[i];
      squares[i].className = 'sq';
      squares[i].textContent = '';
    } else {
      var used = 0;
      for (var k in flag) if (flag.hasOwnProperty(k)) used++;
      if (used >= MINES) return;
      flag[i] = true;
      squares[i].className = 'sq flag';
      squares[i].textContent = '⚑';
    }
    var left = MINES;
    for (var f in flag) if (flag.hasOwnProperty(f)) left--;
    flagsEl.textContent = String(left);
  }

  function boom(i) {
    over = true;
    squares[i].className = 'sq boom';
    squares[i].textContent = '✳';
    for (var k in mine) {
      if (!mine.hasOwnProperty(k)) continue;
      if (flag[k]) continue;
      var el = squares[k];
      if (!el) continue;
      el.className = 'sq open';
      el.textContent = '✳';
    }
    // opened squares are worth something even in a loss: a lost round should not
    // always score zero, or the leaderboard would only ever hold wins
    var score = revealed * 10;
    scoreEl.textContent = String(revealed);
    hintEl.textContent = B.t('Мина. Очков: ', 'A mine. Score: ') + score;
    B.sound('error');
    B.over(score);
    showOver(B.t('Взорвалось', 'Boom'), score);
  }

  function win() {
    over = true;
    var score = revealed * 10 + 600;
    hintEl.textContent = B.t('Поле очищено', 'Field cleared');
    B.sound('success');
    B.over(score);
    showOver(B.t('Поле очищено!', 'Field cleared!'), score);
  }

  function showOver(title, score) {
    var box = document.createElement('div');
    box.className = 'mg-over';
    box.innerHTML = '<b>' + title + '</b>' +
      '<span>' + B.t('Очков: ', 'Score: ') + score + '</span>' +
      '<button type="button" class="mg-btn" id="mg-again">' + B.t('Ещё раз', 'Play again') + '</button>';
    root.querySelector('.mg-board').appendChild(box);
    document.getElementById('mg-again').addEventListener('click', restart);
  }

  build();
  restart();
})();
