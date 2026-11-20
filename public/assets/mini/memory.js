/* Memory. Eight pairs, one clock, and a score that punishes dithering. */
(function () {
  'use strict';

  var B = window.MiniBridge;
  var root = document.getElementById('game');

  var FACES = ['🐱', '🐶', '🦊', '🐸', '🐼', '🦉', '🐙', '🦋'];

  var deck, open, matched, moves, startedAt, timer, done;
  var scoreEl, hintEl, gridEl;

  function build() {
    root.innerHTML =
      '<div class="mg-top">' +
      '<span class="mg-score" id="mg-score">0:00</span>' +
      '<span class="mg-label">' + B.t('время', 'time') + '</span>' +
      '<span class="mg-spacer"></span>' +
      '<span class="mg-label" id="mg-pairs">0/8</span>' +
      '<button type="button" class="mg-btn" id="mg-restart">' + B.t('Заново', 'Restart') + '</button>' +
      '</div>' +
      '<div class="mg-board"><div class="gmem" id="mg-grid"></div></div>' +
      '<div class="mg-hint" id="mg-hint">' + B.t('Найдите все пары', 'Find every pair') + '</div>';

    scoreEl = document.getElementById('mg-score');
    hintEl = document.getElementById('mg-hint');
    gridEl = document.getElementById('mg-grid');
    document.getElementById('mg-restart').addEventListener('click', restart);
  }

  function shuffle(list) {
    for (var i = list.length - 1; i > 0; i--) {
      var j = Math.floor(Math.random() * (i + 1));
      var t = list[i]; list[i] = list[j]; list[j] = t;
    }
    return list;
  }

  function restart() {
    var cards = [];
    for (var i = 0; i < FACES.length; i++) {
      cards.push({ face: FACES[i], id: i });
      cards.push({ face: FACES[i], id: i });
    }
    deck = shuffle(cards);
    open = [];
    matched = 0;
    moves = 0;
    startedAt = 0;
    done = false;
    if (timer) { clearInterval(timer); timer = null; }
    var old = root.querySelector('.mg-over');
    if (old && old.parentNode) old.parentNode.removeChild(old);

    var html = '';
    for (var k = 0; k < deck.length; k++) {
      html += '<button type="button" class="card" data-i="' + k + '">' + deck[k].face + '</button>';
    }
    gridEl.innerHTML = html;
    var btns = gridEl.querySelectorAll('.card');
    for (var b = 0; b < btns.length; b++) btns[b].addEventListener('click', flip);

    scoreEl.textContent = '0:00';
    document.getElementById('mg-pairs').textContent = '0/8';
    hintEl.textContent = B.t('Найдите все пары', 'Find every pair');
  }

  function tick() {
    if (!startedAt) return;
    var s = Math.floor((Date.now() - startedAt) / 1000);
    scoreEl.textContent = Math.floor(s / 60) + ':' + (s % 60 < 10 ? '0' : '') + (s % 60);
  }

  function flip() {
    if (done) return;
    var i = Number(this.getAttribute('data-i'));
    var el = this;
    if (el.className.indexOf('done') >= 0 || el.className.indexOf('up') >= 0) return;
    if (open.length >= 2) return;

    if (!startedAt) {
      startedAt = Date.now();
      timer = setInterval(tick, 250);
    }

    el.className = 'card up';
    open.push({ i: i, el: el, face: deck[i].face });

    if (open.length === 2) {
      moves++;
      var a = open[0], b = open[1];
      if (a.face === b.face) {
        a.el.className = 'card done';
        b.el.className = 'card done';
        matched++;
        open = [];
        B.sound('message');
        document.getElementById('mg-pairs').textContent = matched + '/8';
        if (matched === FACES.length) finish();
      } else {
        B.sound('error');
        setTimeout(function () {
          a.el.className = 'card wrong';
          b.el.className = 'card wrong';
          setTimeout(function () {
            a.el.className = 'card';
            b.el.className = 'card';
            open = [];
          }, 300);
        }, 260);
        // the pair stays "open" until the flip back, so a third click cannot
        // slip in and turn a miss into a match
        open.push({ i: -1, el: null, face: '#' });
      }
    }
  }

  function finish() {
    done = true;
    if (timer) { clearInterval(timer); timer = null; }
    var secs = Math.max(1, Math.round((Date.now() - startedAt) / 1000));
    // 1000 a pair, less 25 a second and 10 a move: fast and clean wins, and the
    // floor keeps a slow but complete run from scoring nothing at all
    var score = Math.max(0, matched * 1000 - secs * 25 - moves * 10);
    scoreEl.textContent = Math.floor(secs / 60) + ':' + (secs % 60 < 10 ? '0' : '') + (secs % 60);
    hintEl.textContent = B.t('Все пары найдены', 'Every pair found');
    B.over(score);

    var over = document.createElement('div');
    over.className = 'mg-over';
    over.innerHTML = '<b>' + B.t('Готово!', 'Cleared!') + '</b>' +
      '<span>' + B.t('Очков: ', 'Score: ') + score + ' · ' +
      B.t('ходов: ', 'moves: ') + moves + ' · ' + secs + ' ' + B.t('с', 's') + '</span>' +
      '<button type="button" class="mg-btn" id="mg-again">' + B.t('Ещё раз', 'Play again') + '</button>';
    root.querySelector('.mg-board').appendChild(over);
    document.getElementById('mg-again').addEventListener('click', restart);
  }

  build();
  restart();
})();
