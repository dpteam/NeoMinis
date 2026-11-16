/* =========================================================================
   SoundFX - tiny WebAudio synth: soft sine/triangle plucks, low pass and a
   light compressor. No audio files, no libraries. "Math audio" on purpose.
   ========================================================================= */
var SoundFX = (function () {
  'use strict';

  var ctx = null;
  var master = null;
  var enabled = true;
  var volume = 0.55;
  var events = {
    click: true, post: true, like: true, comment: true, message: true,
    notification: true, friend: true, login: true, logout: false,
    error: true, success: true, theme: true
  };

  var PRESETS = {
    click:   { type: 'sine',     filter: 4200, notes: [[1046.50, 0, .05, .42]] },
    post:    { type: 'triangle', filter: 3000, notes: [[523.25, 0, .12, .8], [659.25, .06, .12, .75], [783.99, .12, .26, .7]] },
    like:    { type: 'sine',     filter: 3400, notes: [[880.00, 0, .08, .65], [1174.66, .05, .16, .5]] },
    comment: { type: 'sine',     filter: 2800, notes: [[659.25, 0, .09, .6], [880.00, .05, .14, .5]] },
    message: { type: 'sine',     filter: 3400, notes: [[987.77, 0, .09, .65], [1318.51, .055, .18, .5]] },
    notification: { type: 'sine', filter: 2600, notes: [[783.99, 0, .1, .6], [1046.50, .06, .18, .5]] },
    friend:  { type: 'sine',     filter: 2600, notes: [[659.25, 0, .14, .8], [987.77, .075, .28, .7]] },
    login:   { type: 'sine',     filter: 2800, notes: [[523.25, 0, .12, .7], [783.99, .07, .2, .65], [1046.50, .14, .3, .55]] },
    logout:  { type: 'sine',     filter: 1800, notes: [[523.25, 0, .13, .6], [392.00, .07, .26, .55]] },
    error:   { type: 'triangle', filter: 900,  notes: [[196.00, 0, .2, .7], [174.61, .1, .28, .6]] },
    success: { type: 'sine',     filter: 3000, notes: [[659.25, 0, .1, .65], [880.00, .06, .22, .55]] },
    theme:   { type: 'triangle', filter: 2600, notes: [[587.33, 0, .1, .5], [880.00, .06, .2, .45]] }
  };

  /* ------------------------------------------------------------- voices
     Every template gets its own synth character, so switching from Paper to
     Cyberpunk changes what the interface *sounds* like as well as what it looks
     like. Still no audio files: this is a set of parameters applied on top of the
     event presets above.

       transpose  semitones applied to every note (-12 = an octave down)
       filter     multiplies the preset's low-pass cutoff (< 1 = duller)
       type       overrides the waveform for every event in that theme
       decay      multiplies note length (> 1 = more sustain)
       gain       multiplies peak level (compensates for duller/darker timbres)
       detune     cents of detune on the second oscillator (0 = pure)
       wobble     seconds of slow pitch drift, for the tape-like themes
  */
  var VOICES = {
    branded:  { transpose: 0,  filter: 1,    decay: 1,    gain: 1,    detune: 0 },
    aero:     { transpose: 2,  filter: 1.25, decay: 1.15, gain: .95,  detune: 4,   type: 'sine' },
    metro:    { transpose: 0,  filter: .85,  decay: .72,  gain: 1.1,  detune: 0,   type: 'square' },
    paper:    { transpose: -5, filter: .55,  decay: 1.3,  gain: 1.25, detune: 0,   type: 'triangle' },
    terminal: { transpose: 0,  filter: 1.5,  decay: .45,  gain: 1.05, detune: 0,   type: 'square' },
    soft:     { transpose: 3,  filter: .7,   decay: 1.35, gain: 1.1,  detune: 0,   type: 'sine' },
    brutal:   { transpose: -7, filter: .6,   decay: .6,   gain: 1.35, detune: 18,  type: 'sawtooth' },
    neon:     { transpose: 7,  filter: 1.4,  decay: 1.1,  gain: .9,   detune: 8,   type: 'triangle' },
    warm:     { transpose: -3, filter: .65,  decay: 1.2,  gain: 1.2,  detune: 3,   type: 'triangle' },
    slate:    { transpose: -2, filter: .95,  decay: .9,   gain: 1,    detune: 0,   type: 'sine' },
    matrix:   { transpose: 0,  filter: 1.3,  decay: .5,   gain: 1,    detune: 0,   type: 'square' },
    cyberpunk:{ transpose: 5,  filter: 1.45, decay: .8,   gain: .95,  detune: 12,  type: 'sawtooth' },
    newyear:  { transpose: 5,  filter: 1.1,  decay: 1.5,  gain: .95,  detune: 2,   type: 'sine' },
    autumn:   { transpose: -4, filter: .7,   decay: 1.25, gain: 1.2,  detune: 5,   type: 'triangle' },
    horror:   { transpose: -12, filter: .45, decay: 1.6,  gain: 1.4,  detune: 26,  type: 'sawtooth', wobble: .18 }
  };

  var skin = 'branded';

  function setSkin(id) {
    skin = VOICES[id] ? id : 'branded';
    return skin;
  }

  function voiceOf() {
    return VOICES[skin] || VOICES.branded;
  }

  /// The semitone transpose, as a frequency multiplier.
  function pitch(voice) {
    return Math.pow(2, (voice.transpose || 0) / 12);
  }

  function ensure() {
    if (ctx) return ctx;
    var AC = window.AudioContext || window.webkitAudioContext;
    if (!AC) return null;
    try {
      ctx = new AC();
      master = ctx.createGain();
      master.gain.value = volume * 0.5;
      if (ctx.createDynamicsCompressor) {
        var comp = ctx.createDynamicsCompressor();
        comp.threshold.value = -18;
        comp.knee.value = 24;
        comp.ratio.value = 3.5;
        comp.attack.value = 0.004;
        comp.release.value = 0.25;
        master.connect(comp);
        comp.connect(ctx.destination);
      } else {
        master.connect(ctx.destination);
      }
    } catch (e) {
      ctx = null;
      master = null;
    }
    return ctx;
  }

  function voice(c, preset, freq, delay, dur, mul) {
    var v = voiceOf();
    var t0 = c.currentTime + 0.012 + delay;
    var peak = 0.28 * (mul || 1) * 0.9 * (v.gain || 1);
    var len = dur * (v.decay || 1);
    var f = freq * pitch(v);
    var osc = c.createOscillator();
    var detune = c.createOscillator();
    var filt = c.createBiquadFilter();
    var gain = c.createGain();
    var gain2 = c.createGain();

    osc.type = v.type || preset.type || 'sine';
    osc.frequency.setValueAtTime(f, t0);
    // the tape-like themes drift a little, which is what makes a VHS horror
    // sound like a VHS rather than like a synth
    if (v.wobble) {
      try {
        osc.frequency.setValueAtTime(f * 0.985, t0);
        osc.frequency.linearRampToValueAtTime(f * 1.015, t0 + v.wobble);
        osc.frequency.linearRampToValueAtTime(f, t0 + v.wobble * 2);
      } catch (e) { }
    }

    detune.type = 'sine';
    detune.frequency.setValueAtTime(f * 2.002, t0);
    if (v.detune) {
      try { detune.detune.setValueAtTime(v.detune, t0); } catch (e) { }
    }

    filt.type = 'lowpass';
    filt.frequency.setValueAtTime(Math.min((preset.filter || 3000) * (v.filter || 1), 16000), t0);
    filt.Q.value = 0.6;

    gain.gain.setValueAtTime(0.0001, t0);
    gain.gain.exponentialRampToValueAtTime(peak, t0 + 0.018);
    gain.gain.exponentialRampToValueAtTime(0.0001, t0 + len);

    gain2.gain.setValueAtTime(0.0001, t0);
    gain2.gain.exponentialRampToValueAtTime(peak * 0.22, t0 + 0.03);
    gain2.gain.exponentialRampToValueAtTime(0.0001, t0 + len * 0.8);

    osc.connect(filt);
    filt.connect(gain);
    gain.connect(master);
    detune.connect(gain2);
    gain2.connect(master);

    osc.start(t0);
    detune.start(t0);
    osc.stop(t0 + len + 0.05);
    detune.stop(t0 + len + 0.05);
  }

  function play(name) {
    if (!enabled) return;
    if (events[name] === false) return;
    var preset = PRESETS[name];
    if (!preset) return;
    var c = ensure();
    if (!c) return;
    if (c.state === 'suspended') {
      try { c.resume(); } catch (e) { }
    }
    for (var i = 0; i < preset.notes.length; i++) {
      var n = preset.notes[i];
      try { voice(c, preset, n[0], n[1], n[2], n.length > 3 ? n[3] : 1); }
      catch (e) { }
    }
  }

  function unlock() {
    var c = ensure();
    if (!c) return;
    if (c.state === 'suspended') {
      try { c.resume(); } catch (e) { }
    }
  }

  function persist() {
    try {
      localStorage.setItem('social.sound', JSON.stringify({ enabled: enabled, volume: volume, events: events }));
    } catch (e) { }
  }

  function load() {
    try {
      var raw = localStorage.getItem('social.sound');
      if (!raw) return;
      var o = JSON.parse(raw);
      if (typeof o.enabled === 'boolean') enabled = o.enabled;
      if (typeof o.volume === 'number') volume = Math.max(0, Math.min(1, o.volume));
      if (o.events) {
        for (var k in events) {
          if (events.hasOwnProperty(k) && o.events.hasOwnProperty(k)) events[k] = !!o.events[k];
        }
      }
    } catch (e) { }
  }

  function setEnabled(on) {
    enabled = !!on;
    if (enabled) {
      unlock();
      play('click');
    }
    persist();
    return enabled;
  }

  function isEnabled() { return enabled; }

  function setVolume(v) {
    volume = Math.max(0, Math.min(1, Number(v) || 0));
    if (master) {
      try { master.gain.value = volume * 0.5; } catch (e) { }
    }
    persist();
    return volume;
  }

  function getVolume() { return volume; }

  function setEventEnabled(name, on) {
    if (!events.hasOwnProperty(name)) return;
    events[name] = !!on;
    persist();
  }

  function isEventEnabled(name) { return events[name] !== false; }

  function setEvents(map) {
    if (!map) return;
    for (var k in events) {
      if (events.hasOwnProperty(k) && map.hasOwnProperty(k)) events[k] = !!map[k];
    }
    persist();
  }

  function getEvents() {
    var out = {};
    for (var k in events) if (events.hasOwnProperty(k)) out[k] = events[k];
    return out;
  }

  load();
  try {
    document.addEventListener('pointerdown', unlock, { once: true });
    document.addEventListener('keydown', unlock, { once: true });
  } catch (e) { }

  return {
    play: play,
    unlock: unlock,
    setEnabled: setEnabled,
    isEnabled: isEnabled,
    setVolume: setVolume,
    getVolume: getVolume,
    setEventEnabled: setEventEnabled,
    isEventEnabled: isEventEnabled,
    setEvents: setEvents,
    getEvents: getEvents,
    // the template's synth character, so switching a skin changes the sound too
    setSkin: setSkin,
    getSkin: function () { return skin; },
    voices: VOICES,
    presets: PRESETS
  };
})();
