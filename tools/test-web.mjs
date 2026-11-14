#!/usr/bin/env node
/* =========================================================================
   Social Server - end-to-end API test client (Node 18+, no dependencies).

   Mirrors the browser crypto: PBKDF2-HMAC-SHA256, HMAC-CTR sealing and
   request signing, so it exercises exactly what the SPA does.

   usage:
     node tools/test-web.mjs [baseUrl] [adminBaseUrl] [--quiet]
     node tools/test-web.mjs http://127.0.0.1:8080 http://127.0.0.1:8090
   ========================================================================= */
import crypto from 'node:crypto';

const BASE = (process.argv[2] && !process.argv[2].startsWith('--') ? process.argv[2] : 'http://127.0.0.1:8080').replace(/\/+$/, '');
const ADMIN_BASE = (process.argv[3] && !process.argv[3].startsWith('--') ? process.argv[3] : '').replace(/\/+$/, '');
const QUIET = process.argv.includes('--quiet');

const MAC_LABEL = Buffer.from('social.v1.mac', 'utf8');
const ENC_LABEL = Buffer.from('social.v1.enc', 'utf8');

let passed = 0;
const failures = [];

function ok(name, condition, detail) {
  if (condition) {
    passed++;
    if (!QUIET) console.log('  PASS  ' + name + (detail ? '  (' + detail + ')' : ''));
  } else {
    failures.push(name + (detail ? '  [' + detail + ']' : ''));
    console.log('  FAIL  ' + name + (detail ? '  (' + detail + ')' : ''));
  }
}

function sha256(buf) { return crypto.createHash('sha256').update(buf).digest(); }
function hmac(key, data) { return crypto.createHmac('sha256', key).update(data).digest(); }
function pbkdf2(password, salt, iterations, length) {
  return crypto.pbkdf2Sync(password, salt, iterations, length, 'sha256');
}
function hex(buf) { return Buffer.from(buf).toString('hex'); }
function fromHex(s) { return Buffer.from(s, 'hex'); }

function subKeys(master) {
  return { mac: hmac(master, MAC_LABEL), enc: hmac(master, ENC_LABEL) };
}

function xorKeystream(encKey, iv, data) {
  const out = Buffer.alloc(data.length);
  let counter = 0;
  for (let off = 0; off < data.length; off += 32) {
    const input = Buffer.alloc(20);
    iv.copy(input, 0);
    input.writeUInt32BE(counter >>> 0, 16);
    const ks = hmac(encKey, input);
    for (let i = 0; i < 32 && off + i < data.length; i++) out[off + i] = data[off + i] ^ ks[i];
    counter++;
  }
  return out;
}

function seal(master, plain) {
  const keys = subKeys(master);
  const iv = crypto.randomBytes(16);
  const ct = xorKeystream(keys.enc, iv, plain);
  const tag = hmac(keys.mac, Buffer.concat([iv, ct])).subarray(0, 16);
  return Buffer.concat([iv, ct, tag]).toString('base64');
}

function open(master, envelopeB64) {
  let envelope;
  try { envelope = Buffer.from(envelopeB64, 'base64'); } catch { return null; }
  if (envelope.length < 32) return null;
  const iv = envelope.subarray(0, 16);
  const ct = envelope.subarray(16, envelope.length - 16);
  const tag = envelope.subarray(envelope.length - 16);
  const keys = subKeys(master);
  const expect = hmac(keys.mac, Buffer.concat([iv, ct])).subarray(0, 16);
  if (!crypto.timingSafeEqual(tag, expect)) return null;
  return xorKeystream(keys.enc, iv, ct);
}

function signRequest(master, method, target, ts, nonce, body) {
  const macKey = subKeys(master).mac;
  const canonical = method.toUpperCase() + '\n' + target + '\n' + ts + '\n' + nonce + '\n' + hex(sha256(body || Buffer.alloc(0)));
  return hex(hmac(macKey, Buffer.from(canonical, 'utf8')));
}

function verifierOf(password, username, iterations) {
  return hex(pbkdf2(password, Buffer.from('social.login.v1|' + username.toLowerCase(), 'utf8'), iterations, 32));
}

// ------------------------------------------------------------------ client

class Client {
  constructor(label, base) {
    this.label = label;
    this.base = base || BASE;
    this.cookie = '';
    this.key = null;
    this.iterations = 12000;
  }

  async raw(method, path, body, headers) {
    const res = await fetch(this.base + path, { method, body, headers, redirect: 'manual' });    const setCookie = res.headers.getSetCookie ? res.headers.getSetCookie() : [];
    for (const c of setCookie) {
      const first = c.split(';')[0];
      if (first && first.includes('=')) {
        const [name, value] = [first.slice(0, first.indexOf('=')), first.slice(first.indexOf('=') + 1)];
        if (value === '') this.cookie = '';
        else this.cookie = name + '=' + value;
      }
    }
    const text = await res.text();
    return { status: res.status, headers: res.headers, text };
  }

  async handshake() {
    const r = await this.raw('GET', '/api/handshake');
    const data = JSON.parse(r.text);
    this.iterations = data.kdfIterations || 12000;
    return data;
  }

  async authCall(path, authHeaders, k0, bodyObj) {
    const ts = String(Math.floor(Date.now() / 1000));
    const nonce = crypto.randomBytes(16).toString('hex');
    const wire = seal(k0, Buffer.from(JSON.stringify(bodyObj || {}), 'utf8'));
    const sig = signRequest(k0, 'POST', path, ts, nonce, Buffer.from(wire, 'utf8'));
    const headers = Object.assign({
      'X-Api-Ts': ts, 'X-Api-Nonce': nonce, 'X-Api-Sig': sig,
      'X-Api-Enc': '1', 'Content-Type': 'text/plain; charset=utf-8'
    }, authHeaders);
    const r = await this.raw('POST', path, wire, headers);
    const plain = open(k0, r.text);
    let data = null;
    if (plain) data = JSON.parse(plain.toString('utf8'));
    else if (r.text) { try { data = JSON.parse(r.text); } catch { data = null; } } // errors are sent unsealed
    return { status: r.status, data, raw: r };
  }

  async register(username, password, displayName) {
    const hs = await this.handshake();
    const verifier = verifierOf(password, username, this.iterations);
    const salt = crypto.randomBytes(16).toString('hex');
    const k0 = pbkdf2(Buffer.from(verifier, 'utf8'), fromHex(salt), this.iterations, 32);
    const r = await this.authCall('/api/register', {
      'X-Auth-User': username, 'X-Auth-Verifier': verifier, 'X-Auth-Salt': salt
    }, k0, { displayName: displayName || username, captchaId: '', captcha: '' });
    if (r.status === 200 && r.data) {
      this.key = pbkdf2(Buffer.from(verifier, 'utf8'), fromHex(r.data.sessionSalt), this.iterations, 32);
      this.user = r.data.user;
      this.recovery = r.data.recoveryCode;
    }
    return r;
  }

  async login(username, password) {
    await this.handshake();
    const verifier = verifierOf(password, username, this.iterations);
    const salt = crypto.randomBytes(16).toString('hex');
    const k0 = pbkdf2(Buffer.from(verifier, 'utf8'), fromHex(salt), this.iterations, 32);
    const r = await this.authCall('/api/login', {
      'X-Auth-User': username, 'X-Auth-Verifier': verifier, 'X-Auth-Salt': salt
    }, k0, {});
    if (r.status === 200 && r.data) {
      this.key = pbkdf2(Buffer.from(verifier, 'utf8'), fromHex(r.data.sessionSalt), this.iterations, 32);
      this.user = r.data.user;
    }
    return r;
  }

  async call(method, path, bodyObj, opts) {
    opts = opts || {};
    const ts = String(Math.floor(Date.now() / 1000));
    const nonce = opts.nonce || crypto.randomBytes(16).toString('hex');
    let wire = '';
    if (method !== 'GET' || bodyObj) wire = seal(this.key, Buffer.from(JSON.stringify(bodyObj || {}), 'utf8'));
    const sig = opts.signature || signRequest(this.key, method, path, ts, nonce, Buffer.from(wire, 'utf8'));
    const headers = {
      'X-Api-Ts': ts, 'X-Api-Nonce': nonce, 'X-Api-Sig': sig, 'X-Api-Enc': '1'
    };
    if (wire) headers['Content-Type'] = 'text/plain; charset=utf-8';
    if (this.cookie) headers['Cookie'] = this.cookie;
    const r = await this.raw(method, path, wire || undefined, headers);
    const plain = r.text ? open(this.key, r.text) : null;
    let data = null;
    if (plain) data = JSON.parse(plain.toString('utf8'));
    else if (r.text) { try { data = JSON.parse(r.text); } catch { data = null; } } // errors are sent unsealed
    return { status: r.status, data, raw: r };
  }
}

// -------------------------------------------------------------------- main

async function main() {
  console.log('Social Server API test -> ' + BASE + (ADMIN_BASE ? ' (admin: ' + ADMIN_BASE + ')' : ''));
  console.log('-------------------------------------------------------------');

  // ---- static surface
  const index = await fetch(BASE + '/');
  const indexText = await index.text();
  ok('index.html served', index.status === 200 && indexText.includes('assets/js/app.js'), String(index.status));
  ok('security headers', (index.headers.get('content-security-policy') || '').includes("default-src 'self'"), '');
  ok('nosniff header', index.headers.get('x-content-type-options') === 'nosniff', '');
  ok('frame denied', index.headers.get('x-frame-options') === 'DENY', '');
  const appJs = await fetch(BASE + '/assets/js/app.js');
  ok('app.js served', appJs.status === 200, String(appJs.status));
  const notFound = await fetch(BASE + '/nope-does-not-exist', { headers: { Accept: 'text/html' } });
  ok('404 page', notFound.status === 404, String(notFound.status));
  const traversal = await fetch(BASE + '/../config.ini');
  ok('path traversal blocked', traversal.status === 404 || traversal.status === 400, String(traversal.status));

  const health = await fetch(BASE + '/api/health');
  const healthData = await health.json();
  ok('health endpoint', health.status === 200 && healthData.ok === true, healthData.status || '');

  // ---- registration
  const stamp = Date.now().toString(36);
  const PNG = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==';
  const alice = new Client('alice');
  const bob = new Client('bob');
  const ua = 'alice_' + stamp;
  const ub = 'bob_' + stamp;

  const hs = await alice.handshake();
  ok('handshake', hs.ok === true && hs.scheme === 'hmac-sha256+ctr', hs.kdf);
  ok('handshake has no secrets', !JSON.stringify(hs).includes('serverSecret'), '');

  const regA = await alice.register(ua, 'alice-password-1', 'Alice Test');
  ok('register first user', regA.status === 200 && !!alice.key, String(regA.status));
  ok('recovery code issued', typeof alice.recovery === 'string' && alice.recovery.length >= 20, '');
  const expectAdmin = hs.userCount === 0;
  ok('first account becomes admin', !!regA.data && regA.data.user.role === (expectAdmin ? 'admin' : 'user'),
    'role=' + (regA.data && regA.data.user.role) + ' existing=' + hs.userCount);

  const regB = await bob.register(ub, 'bob-password-1', 'Bob Test');
  ok('register second user', regB.status === 200 && !!bob.key, String(regB.status));
  ok('second user is not admin', regB.data && regB.data.user && regB.data.user.role === 'user', '');

  const dup = new Client('dup');
  const dupReg = await dup.register(ua, 'whatever-123456');
  ok('duplicate username rejected', dupReg.status === 409 && dupReg.data && dupReg.data.error === 'username-taken', String(dupReg.status));

  // ---- auth guards
  const anon = new Client('anon');
  await anon.handshake();
  const unsigned = await anon.raw('GET', '/api/me');
  ok('unsigned request rejected', unsigned.status === 401, String(unsigned.status));

  const me = await alice.call('GET', '/api/me');
  ok('me returns profile', me.status === 200 && me.data && me.data.me.user.username === ua, String(me.status));
  ok('me has counters', me.data && typeof me.data.me.unreadNotifications === 'number', '');

  const badSig = await alice.call('GET', '/api/me', null, { signature: 'ab'.repeat(32) });
  ok('bad signature rejected', badSig.status === 401 && badSig.data && badSig.data.error === 'bad-signature', String(badSig.status));

  const ts = String(Math.floor(Date.now() / 1000));
  const nonce = crypto.randomBytes(16).toString('hex');
  const sig = signRequest(alice.key, 'GET', '/api/me', ts, nonce, Buffer.alloc(0));
  const first = await alice.call('GET', '/api/me', null, { nonce, signature: sig });
  const replay = await alice.call('GET', '/api/me', null, { nonce, signature: sig });
  ok('first signed call ok', first.status === 200, String(first.status));
  ok('replay rejected', replay.status === 401 && replay.data && replay.data.error === 'replay', String(replay.status));

  const plainBody = await (async () => {
    const t = String(Math.floor(Date.now() / 1000));
    const n = crypto.randomBytes(16).toString('hex');
    const s = signRequest(alice.key, 'GET', '/api/me', t, n, Buffer.alloc(0));
    const h = { 'X-Api-Ts': t, 'X-Api-Nonce': n, 'X-Api-Sig': s };
    if (alice.cookie) h['Cookie'] = alice.cookie;
    return alice.raw('GET', '/api/me', undefined, h);
  })();
  ok('unencrypted call refused', plainBody.status === 400, String(plainBody.status));

  // ---- content flow
  const post = await alice.call('POST', '/api/post', { body: 'Hello from the test client', visibility: 'public' });
  ok('create post', post.status === 200 && post.data && post.data.post.id > 0, String(post.status));
  const postId = post.data && post.data.post.id;

  const feedBob = await bob.call('GET', '/api/feed?tab=global&limit=10');
  const found = feedBob.data && feedBob.data.posts.some(p => p.id === postId);
  ok('post visible in global feed', !!found, '');

  const like = await bob.call('POST', '/api/like', { id: postId });
  ok('like toggles on', like.status === 200 && like.data.liked === true && like.data.likes === 1, JSON.stringify(like.data));
  const unlike = await bob.call('POST', '/api/like', { id: postId });
  ok('like toggles off', unlike.data && unlike.data.liked === false && unlike.data.likes === 0, '');
  await bob.call('POST', '/api/like', { id: postId });

  const comment = await bob.call('POST', '/api/comment', { post: postId, body: 'nice one' });
  ok('comment created', comment.status === 200 && comment.data.comment.body === 'nice one', String(comment.status));
  const comments = await alice.call('GET', '/api/comments?post=' + postId);
  ok('comment listed', comments.data && comments.data.comments.length === 1, '');

  const mentionPost = await bob.call('POST', '/api/post', { body: 'hello @' + ua + ' and #newtag' });
  ok('post with mention and tag', mentionPost.status === 200, String(mentionPost.status));
  const aliceNotif = await alice.call('GET', '/api/notifications?limit=50');
  ok('mention notified', aliceNotif.data && aliceNotif.data.notifications.some(n => n.type === 'mention'), '');
  const tagRes = await alice.call('GET', '/api/tag?tag=newtag');
  ok('tag page finds the post', tagRes.status === 200 && tagRes.data.posts.some(p => p.id === mentionPost.data.post.id),
    String(tagRes.status));
  const tagList = await alice.call('GET', '/api/tags');
  ok('tag cloud lists the tag', tagList.data && tagList.data.tags.some(t => t.tag === 'newtag'), '');
  const tagMissing = await alice.call('GET', '/api/tag?tag=');
  ok('empty tag rejected', tagMissing.status === 400, String(tagMissing.status));

  // ---- polls
  const pollPost = await alice.call('POST', '/api/post', {
    body: 'choose one', poll: { question: 'Coffee or tea?', options: ['Coffee', 'Tea'] }
  });
  ok('poll post created', pollPost.status === 200 && pollPost.data.post.poll &&
    pollPost.data.post.poll.options.length === 2, String(pollPost.status));
  const pollId = pollPost.data && pollPost.data.post.id;
  const vote = await bob.call('POST', '/api/poll', { post: pollId, option: 1 });
  ok('poll vote counted', vote.status === 200 && vote.data.post.poll.total === 1 &&
    vote.data.post.poll.myVote === 1 && vote.data.post.poll.options[1].percent === 100,
    JSON.stringify(vote.data && vote.data.post.poll));
  const changeVote = await bob.call('POST', '/api/poll', { post: pollId, option: 0 });
  ok('poll vote changes', changeVote.data.post.poll.myVote === 0 && changeVote.data.post.poll.total === 1, '');
  const unvote = await bob.call('POST', '/api/poll', { post: pollId, option: 0 });
  ok('poll vote withdrawn', unvote.data.post.poll.myVote === -1 && unvote.data.post.poll.total === 0, '');
  const badVote = await bob.call('POST', '/api/poll', { post: pollId, option: 9 });
  ok('bad poll option rejected', badVote.status === 400, String(badVote.status));
  const badPoll = await alice.call('POST', '/api/post', { body: 'x', poll: { question: 'q', options: ['only one'] } });
  ok('poll needs two options', badPoll.status === 400, String(badPoll.status));
  const plainVote = await bob.call('POST', '/api/poll', { post: postId, option: 0 });
  ok('voting without a poll rejected', plainVote.status === 400, String(plainVote.status));

  // ---- drafts and scheduling
  const draftSave = await alice.call('POST', '/api/drafts/save', {
    body: 'draft body', video: '', visibility: 'friends', poll: { question: 'q?', options: ['a', 'b'] }
  });
  ok('draft saved', draftSave.status === 200 && draftSave.data.id > 0, String(draftSave.status));
  const draftId = draftSave.data && draftSave.data.id;
  const draftList = await alice.call('GET', '/api/drafts');
  ok('draft listed', draftList.status === 200 && draftList.data.drafts.some(d => d.id === draftId), '');
  const draftEmpty = await alice.call('POST', '/api/drafts/save', { body: '' });
  ok('empty draft rejected', draftEmpty.status === 400, String(draftEmpty.status));
  const draftBob = await bob.call('GET', '/api/drafts');
  ok('drafts are private', draftBob.data.drafts.every(d => d.id !== draftId), '');
  const draftDel = await alice.call('POST', '/api/drafts/delete', { id: draftId });
  ok('draft deleted', draftDel.status === 200, String(draftDel.status));

  const future = new Date(Date.now() + 3600000).toISOString();
  const scheduled = await alice.call('POST', '/api/post', { body: 'later post', scheduledFor: future });
  ok('post scheduled', scheduled.status === 200 && scheduled.data.post.scheduledFor === future,
    String(scheduled.status) + ' ' + (scheduled.data && scheduled.data.post.scheduledFor));
  const scheduledId = scheduled.data && scheduled.data.post.id;
  const feedNow = await bob.call('GET', '/api/feed?tab=global&limit=50');
  ok('scheduled post stays hidden', !feedNow.data.posts.some(p => p.id === scheduledId), '');
  const myScheduled = await alice.call('GET', '/api/drafts');
  ok('scheduled post listed for its author',
    myScheduled.data.scheduled.some(p => p.id === scheduledId), '');
  const pastSchedule = await alice.call('POST', '/api/post', {
    body: 'x', scheduledFor: new Date(Date.now() - 86400000).toISOString()
  });
  ok('past schedule rejected', pastSchedule.status === 400, String(pastSchedule.status));
  await alice.call('POST', '/api/post/delete', { id: scheduledId });

  const feedbackAnon = await fetch(BASE + '/api/feedback', {
    method: 'POST',
    headers: { 'Content-Type': 'text/plain; charset=utf-8' },
    body: JSON.stringify({ kind: 'bug', message: 'test bug report from the suite', contact: 'qa@example.com', page: '#/feed' })
  });
  const feedbackBody = await feedbackAnon.json();
  ok('anonymous feedback accepted', feedbackAnon.status === 200 && feedbackBody.ok === true && feedbackBody.id > 0,
    String(feedbackAnon.status));

  const feedbackShort = await fetch(BASE + '/api/feedback', {
    method: 'POST',
    headers: { 'Content-Type': 'text/plain; charset=utf-8' },
    body: JSON.stringify({ kind: 'bug', message: 'x' })
  });
  ok('short feedback rejected', feedbackShort.status === 400, String(feedbackShort.status));

  const feedbackTrap = await fetch(BASE + '/api/feedback', {
    method: 'POST',
    headers: { 'Content-Type': 'text/plain; charset=utf-8' },
    body: JSON.stringify({ kind: 'bug', message: 'buy cheap pills', website: 'http://spam.example' })
  });
  const feedbackTrapBody = await feedbackTrap.json();
  ok('honeypot looks successful', feedbackTrap.status === 200 && feedbackTrapBody.id === undefined,
    JSON.stringify(feedbackTrapBody));

  const feedbackGet = await fetch(BASE + '/api/feedback');
  ok('feedback is not readable anonymously', feedbackGet.status === 401 || feedbackGet.status === 405,
    String(feedbackGet.status));

  // ---- live notifications and per-type mutes
  const baseNotif = await alice.call('GET', '/api/notifications?limit=1');
  const lastId = baseNotif.data.notifications.length ? baseNotif.data.notifications[0].id : 0;
  const mute = await alice.call('POST', '/api/settings', { notifyMute: ['mention'] });
  ok('notify mute saved', mute.status === 200 && mute.data.user.notifyMute === 'mention', String(mute.status));
  await bob.call('POST', '/api/post', { body: 'quiet @' + ua });
  const waitMuted = await alice.call('GET', '/api/notifications/wait?after=' + lastId + '&timeout=2');
  ok('muted mention is dropped', waitMuted.status === 200 && waitMuted.data.notifications.length === 0,
    JSON.stringify(waitMuted.data && waitMuted.data.notifications.length));
  const unmute = await alice.call('POST', '/api/settings', { notifyMute: [] });
  ok('notify mute cleared', unmute.status === 200 && unmute.data.user.notifyMute === '', '');
  await bob.call('POST', '/api/post', { body: 'loud @' + ua });
  const waitLive = await alice.call('GET', '/api/notifications/wait?after=' + lastId + '&timeout=10');
  ok('live wait returns the mention',
    waitLive.status === 200 && waitLive.data.notifications.some(n => n.type === 'mention'),
    JSON.stringify((waitLive.data && waitLive.data.notifications || []).map(n => n.type)));

  const fr = await bob.call('POST', '/api/friend', { user: alice.user.id, action: 'request' });
  ok('friend request sent', fr.status === 200 && fr.data.status === 'pending', JSON.stringify(fr.data && fr.data.status));
  const friends = await alice.call('GET', '/api/friends');
  ok('incoming request visible', friends.data && friends.data.incoming.length === 1, '');
  const accept = await alice.call('POST', '/api/friend', { user: bob.user.id, action: 'accept' });
  ok('friend request accepted', accept.status === 200 && accept.data.status === 'accepted', '');
  const wall = await alice.call('GET', '/api/wall?user=' + bob.user.id);
  ok('friend wall readable', wall.status === 200, String(wall.status));

  const msg = await bob.call('POST', '/api/messages/send', { to: alice.user.id, body: 'hi alice' });
  ok('message sent', msg.status === 200 && msg.data.message.body === 'hi alice', String(msg.status));
  const threads = await alice.call('GET', '/api/messages/threads');
  ok('thread visible', threads.data && threads.data.threads.length === 1 && threads.data.unread === 1, '');
  const conv = await alice.call('GET', '/api/messages?with=' + bob.user.id);
  ok('conversation read', conv.data && conv.data.messages.length === 1, '');

  // ---- messenger: attachments, quotes, typing, live delivery, read receipts
  const quotedId = conv.data.messages[0].id;
  const imgMsg = await bob.call('POST', '/api/messages/send', { to: alice.user.id, body: '', image: PNG });
  ok('message picture saved', imgMsg.status === 200 && String(imgMsg.data.message.image).indexOf('/media/messages/') === 0,
    String(imgMsg.status) + ' ' + (imgMsg.data && imgMsg.data.message.image));
  const replyMsg = await bob.call('POST', '/api/messages/send', { to: alice.user.id, body: 'quote me', replyTo: quotedId });
  ok('reply is quoted', replyMsg.status === 200 && replyMsg.data.message.replyTo &&
    replyMsg.data.message.replyTo.id === quotedId, JSON.stringify(replyMsg.data && replyMsg.data.message.replyTo));
  const badReply = await bob.call('POST', '/api/messages/send', { to: alice.user.id, body: 'x', replyTo: 999999 });
  ok('foreign quote rejected', badReply.status === 400, String(badReply.status));

  // a minimal 0.1s 8kHz mono WAV, enough for the container sniffing
  const wavSamples = 800;
  const wavBuf = Buffer.alloc(44 + wavSamples * 2);
  wavBuf.write('RIFF', 0); wavBuf.writeUInt32LE(36 + wavSamples * 2, 4); wavBuf.write('WAVE', 8);
  wavBuf.write('fmt ', 12); wavBuf.writeUInt32LE(16, 16); wavBuf.writeUInt16LE(1, 20);
  wavBuf.writeUInt16LE(1, 22); wavBuf.writeUInt32LE(8000, 24); wavBuf.writeUInt32LE(16000, 28);
  wavBuf.writeUInt16LE(2, 32); wavBuf.writeUInt16LE(16, 34);
  wavBuf.write('data', 36); wavBuf.writeUInt32LE(wavSamples * 2, 40);
  const WAV = 'data:audio/wav;base64,' + wavBuf.toString('base64');

  const voiceMsg = await bob.call('POST', '/api/messages/send', { to: alice.user.id, body: '', audio: WAV, audioSec: 3 });
  ok('voice message saved', voiceMsg.status === 200 && voiceMsg.data.message.audio &&
    voiceMsg.data.message.audio.indexOf('/media/messages/') === 0 && voiceMsg.data.message.audioSec === 3,
    String(voiceMsg.status) + ' ' + (voiceMsg.data && voiceMsg.data.message.audio));
  const tooLong = await bob.call('POST', '/api/messages/send', { to: alice.user.id, body: '', audio: WAV, audioSec: 99999 });
  ok('overlong voice rejected', tooLong.status === 400, String(tooLong.status));
  const badAudio = await bob.call('POST', '/api/messages/send', { to: alice.user.id, body: '', audio: PNG });
  ok('non-audio payload rejected', badAudio.status === 400, String(badAudio.status));
  const voiceList = await alice.call('GET', '/api/messages?with=' + bob.user.id);
  ok('voice message in the thread', voiceList.data && voiceList.data.messages.some(m => m.audio), '');

  // ---- message reactions and search
  const targetMsg = voiceList.data.messages[voiceList.data.messages.length - 1];
  const react = await bob.call('POST', '/api/messages/react', { id: targetMsg.id, emoji: '👍' });
  ok('message reaction added', react.status === 200 && (react.data.message.reactions || []).length === 1 &&
    react.data.message.reactions[0].count === 1 && react.data.message.reactions[0].mine === true,
    JSON.stringify(react.data && react.data.message.reactions));
  const react2 = await bob.call('POST', '/api/messages/react', { id: targetMsg.id, emoji: '🔥' });
  ok('reaction replaces the old one', (react2.data.message.reactions || []).length === 1 &&
    react2.data.message.reactions[0].emoji === '🔥', JSON.stringify(react2.data && react2.data.message.reactions));
  const react3 = await bob.call('POST', '/api/messages/react', { id: targetMsg.id, emoji: '🔥' });
  ok('reaction toggles off', (react3.data.message.reactions || []).length === 0, '');
  const badEmoji = await bob.call('POST', '/api/messages/react', { id: targetMsg.id, emoji: '🚀' });
  ok('unknown reaction rejected', badEmoji.status === 400, String(badEmoji.status));

  const searchMsg = await bob.call('GET', '/api/messages/search?q=' + encodeURIComponent('quote me'));
  ok('message search finds text', searchMsg.status === 200 && searchMsg.data.results.length >= 1 &&
    searchMsg.data.results[0].peer.id === alice.user.id, String(searchMsg.status));
  const searchShort = await bob.call('GET', '/api/messages/search?q=a');
  ok('too short query rejected', searchShort.status === 400, String(searchShort.status));
  const searchMiss = await bob.call('GET', '/api/messages/search?q=' + encodeURIComponent('zzz-nothing-here'));
  ok('search miss returns nothing', searchMiss.status === 200 && searchMiss.data.results.length === 0, '');

  // ---- file attachments
  const TXT = 'data:text/plain;base64,' + Buffer.from('attachment body').toString('base64');
  const fileMsg = await bob.call('POST', '/api/messages/send', {
    to: alice.user.id, body: '', file: TXT, fileName: 'notes.txt'
  });
  ok('file attachment saved', fileMsg.status === 200 && String(fileMsg.data.message.file).indexOf('/media/files/') === 0 &&
    fileMsg.data.message.fileName === 'notes.txt' && fileMsg.data.message.fileSize > 0,
    String(fileMsg.status) + ' ' + (fileMsg.data && fileMsg.data.message.file));
  const htmlMsg = await bob.call('POST', '/api/messages/send', {
    to: alice.user.id, body: '', file: 'data:text/html;base64,' + Buffer.from('<b>x</b>').toString('base64'),
    fileName: 'evil.html'
  });
  ok('html attachment rejected', htmlMsg.status === 400, String(htmlMsg.status));
  const noName = await bob.call('POST', '/api/messages/send', { to: alice.user.id, body: '', file: TXT });
  ok('file without a name rejected', noName.status === 400, String(noName.status));

  // ---- albums
  const albumMsg = await bob.call('POST', '/api/messages/send', {
    to: alice.user.id, body: 'album', images: [PNG, PNG, PNG]
  });
  ok('album saved', albumMsg.status === 200 && (albumMsg.data.message.images || []).length === 2 &&
    !!albumMsg.data.message.image, JSON.stringify(albumMsg.data && albumMsg.data.message.images));
  const tooMany = await bob.call('POST', '/api/messages/send', {
    to: alice.user.id, body: '', images: [PNG, PNG, PNG, PNG, PNG, PNG, PNG]
  });
  ok('album is capped at six', tooMany.status === 200 && (tooMany.data.message.images || []).length === 5,
    String(tooMany.status));

  // ---- read receipts: alice opens the thread, bob sees readUpTo
  await alice.call('GET', '/api/messages?with=' + bob.user.id);
  const bobWait = await bob.call('GET', '/api/messages/wait?with=' + alice.user.id + '&after=0&timeout=1');
  ok('read up to is reported', bobWait.status === 200 && !!bobWait.data.readUpTo, String(bobWait.data && bobWait.data.readUpTo));

  const fileFetch = await fetch(BASE + fileMsg.data.message.file);
  const fileHeaders = fileFetch.headers;
  ok('attachment is served as a download', fileFetch.status === 200 &&
    (fileHeaders.get('content-disposition') || '').indexOf('attachment') === 0,
    String(fileHeaders.get('content-disposition')));
  ok('attachment gets a sandboxed CSP', (fileHeaders.get('content-security-policy') || '').indexOf('sandbox') >= 0,
    String(fileHeaders.get('content-security-policy')));

  // ---- unread divider
  await bob.call('POST', '/api/messages/send', { to: alice.user.id, body: 'unread marker' });
  const withUnread = await alice.call('GET', '/api/messages?with=' + bob.user.id + '&limit=50');
  ok('conversation reports where unread starts', withUnread.status === 200 && withUnread.data.unreadFrom > 0,
    String(withUnread.data && withUnread.data.unreadFrom));
  const againUnread = await alice.call('GET', '/api/messages?with=' + bob.user.id + '&limit=50');
  ok('second read has no unread marker', againUnread.data.unreadFrom === 0, String(againUnread.data.unreadFrom));

  // ---- filtered post search
  const spQ = await alice.call('GET', '/api/search/posts?q=' + encodeURIComponent('choose one'));
  ok('post search by text', spQ.status === 200 && spQ.data.posts.some(p => p.id === pollId), String(spQ.status));
  const spPoll = await alice.call('GET', '/api/search/posts?media=poll');
  ok('post search filters polls', spPoll.status === 200 && spPoll.data.posts.length >= 1 &&
    spPoll.data.posts.every(p => p.poll), String(spPoll.status));
  const spText = await alice.call('GET', '/api/search/posts?media=text&limit=50');
  ok('text filter skips media posts', spText.status === 200 &&
    spText.data.posts.every(p => !p.image && !p.video && !p.poll), String(spText.status));
  const spTag = await alice.call('GET', '/api/search/posts?tag=newtag');
  ok('post search by tag', spTag.status === 200 && spTag.data.posts.length >= 1, String(spTag.status));
  const spAuthor = await alice.call('GET', '/api/search/posts?author=' + bob.user.id);
  ok('post search by author', spAuthor.status === 200 &&
    spAuthor.data.posts.every(p => p.author.id === bob.user.id), String(spAuthor.status));
  const spPopular = await alice.call('GET', '/api/search/posts?media=text&sort=popular&limit=50');
  ok('popular sorting answers', spPopular.status === 200 && Array.isArray(spPopular.data.posts), '');
  const spOld = await alice.call('GET', '/api/search/posts?media=text&sort=old&limit=50');
  const oldIds = (spOld.data.posts || []).map(p => p.id);
  ok('old sorting is ascending', oldIds.every((v, i) => i === 0 || oldIds[i - 1] <= v), JSON.stringify(oldIds.slice(0, 4)));
  const spEmpty = await alice.call('GET', '/api/search/posts');
  ok('empty search rejected', spEmpty.status === 400, String(spEmpty.status));

  // ---- message forwarding
  const fwd = await bob.call('POST', '/api/messages/forward', { id: quotedId, to: alice.user.id });
  ok('message forwarded', fwd.status === 200 && fwd.data.message.forwardedFrom &&
    fwd.data.message.forwardedFrom.id === bob.user.id && fwd.data.message.body === 'hi alice',
    JSON.stringify(fwd.data && fwd.data.message.forwardedFrom));
  const fwdAlien = await bob.call('POST', '/api/messages/forward', { id: 999999, to: alice.user.id });
  ok('forward of unknown message rejected', fwdAlien.status === 404, String(fwdAlien.status));

  // ---- post reactions
  const postReact = await alice.call('POST', '/api/reaction', { id: postId, emoji: '🔥' });
  ok('post reaction added', postReact.status === 200 && (postReact.data.post.reactions || []).length === 1 &&
    postReact.data.post.reactions[0].mine === true, JSON.stringify(postReact.data && postReact.data.post.reactions));
  const postReactSwitch = await bob.call('POST', '/api/reaction', { id: postId, emoji: '👍' });
  const chips = (postReactSwitch.data.post.reactions || []);
  ok('second reaction joins the post', chips.length === 2 && chips.some(r => r.emoji === '🔥' && r.count === 1) &&
    chips.some(r => r.emoji === '👍' && r.mine === true), JSON.stringify(chips));
  const postReactOff = await bob.call('POST', '/api/reaction', { id: postId, emoji: '👍' });
  ok('post reaction toggles off', (postReactOff.data.post.reactions || []).length === 1, '');
  const postReactHeart = await alice.call('POST', '/api/reaction', { id: postId, emoji: '❤️' });
  ok('like emoji is not a post reaction', postReactHeart.status === 400, String(postReactHeart.status));

  // ---- reposts
  const originPost = await alice.call('POST', '/api/post', { body: 'original to repost' });
  const originId = originPost.data.post.id;
  const repost = await bob.call('POST', '/api/post/repost', { id: originId, body: 'look at this' });
  ok('repost created', repost.status === 200 && repost.data.post.repostOf &&
    repost.data.post.repostOf.id === originId && repost.data.post.body === 'look at this',
    String(repost.status));
  const repostId = repost.data && repost.data.post.id;
  const originAfter = await alice.call('GET', '/api/post?id=' + originId);
  ok('origin counts the repost', originAfter.data.post.reposts === 1, String(originAfter.data.post.reposts));
  const repostTwice = await bob.call('POST', '/api/post/repost', { id: repostId });
  ok('reposting a repost points at the original',
    repostTwice.data.post.repostOf.id === originId, String(repostTwice.data.post.repostOf.id));
  const originAfter2 = await alice.call('GET', '/api/post?id=' + originId);
  ok('nested repost bumps the same counter', originAfter2.data.post.reposts === 2, '');
  const reactionsBefore = await alice.call('GET', '/api/notifications?limit=5');
  const reactNotify = await bob.call('POST', '/api/reaction', { id: originId, emoji: '🎉' });
  ok('reaction accepted', reactNotify.status === 200, String(reactNotify.status));
  const notifAfter = await alice.call('GET', '/api/notifications?limit=5');
  ok('repost notifies the author',
    notifAfter.data.notifications.some(n => n.type === 'repost'), '');
  ok('reaction notifies the author',
    notifAfter.data.notifications.some(n => n.type === 'reaction'), '');
  const deleteRepost = await bob.call('POST', '/api/post/delete', { id: repostId });
  const originAfter3 = await alice.call('GET', '/api/post?id=' + originId);
  ok('deleting a repost lowers the counter', deleteRepost.status === 200 && originAfter3.data.post.reposts === 1,
    String(originAfter3.data.post.reposts));

  // ---- media library
  const wavSamples2 = 800;
  const wav2 = Buffer.alloc(44 + wavSamples2 * 2);
  wav2.write('RIFF', 0); wav2.writeUInt32LE(36 + wavSamples2 * 2, 4); wav2.write('WAVE', 8);
  wav2.write('fmt ', 12); wav2.writeUInt32LE(16, 16); wav2.writeUInt16LE(1, 20);
  wav2.writeUInt16LE(1, 22); wav2.writeUInt32LE(8000, 24); wav2.writeUInt32LE(16000, 28);
  wav2.writeUInt16LE(2, 32); wav2.writeUInt16LE(16, 34);
  wav2.write('data', 36); wav2.writeUInt32LE(wavSamples2 * 2, 40);
  const TRACK = 'data:audio/wav;base64,' + wav2.toString('base64');

  const upTrack = await alice.call('POST', '/api/media/upload', {
    kind: 'audio', fileName: 'song.wav', data: TRACK, title: 'Test track', artist: 'Tester', duration: 7
  });
  ok('audio uploaded', upTrack.status === 200 && upTrack.data.item.url.indexOf('/media/library/') === 0 &&
    upTrack.data.item.title === 'Test track' && upTrack.data.item.duration === 7,
    String(upTrack.status) + ' ' + (upTrack.data && upTrack.data.item.url));
  const trackId = upTrack.data && upTrack.data.item.id;

  const myMusic = await alice.call('GET', '/api/media?kind=audio');
  ok('library lists the track', myMusic.data.items.some(i => i.id === trackId), '');
  const bobMusic = await bob.call('GET', '/api/media?kind=audio');
  ok('shared shelf shows it without delete rights',
    bobMusic.data.items.some(i => i.id === trackId && i.mine === false), '');
  const bobOwn = await bob.call('GET', '/api/media?kind=audio&user=' + bob.user.id);
  ok("another user's library is separate", !bobOwn.data.items.some(i => i.id === trackId), '');
  const aliceOwn = await alice.call('GET', '/api/media?kind=audio&user=' + alice.user.id);
  ok('own library lists the track', aliceOwn.data.items.some(i => i.id === trackId), '');

  const audioFetch = await fetch(BASE + upTrack.data.item.url);
  ok('track streams inline as audio', audioFetch.status === 200 &&
    (audioFetch.headers.get('content-type') || '').indexOf('audio/') === 0 &&
    (audioFetch.headers.get('content-disposition') || '') === 'inline',
    String(audioFetch.headers.get('content-type')) + ' ' + String(audioFetch.headers.get('content-disposition')));

  const wrongKind = await alice.call('POST', '/api/media/upload', {
    kind: 'video', fileName: 'song.wav', data: TRACK
  });
  ok('audio file refused as video', wrongKind.status === 400, String(wrongKind.status));
  const badKind = await alice.call('POST', '/api/media/upload', {
    kind: 'documents', fileName: 'a.wav', data: TRACK
  });
  ok('unknown media kind rejected', badKind.status === 400, String(badKind.status));

  const foreignDelete = await bob.call('POST', '/api/media/delete', { id: trackId });
  ok("cannot delete somebody else's track", foreignDelete.status === 404, String(foreignDelete.status));
  const delTrack = await alice.call('POST', '/api/media/delete', { id: trackId });
  ok('own track deleted', delTrack.status === 200, String(delTrack.status));
  const goneTrack = await alice.call('GET', '/api/media?kind=audio');
  ok('deleted track is gone', !goneTrack.data.items.some(i => i.id === trackId), '');

  // ---- save to my library + reference counted files
  const mineTrack = await alice.call('POST', '/api/media/upload', {
    kind: 'audio', fileName: 'shared.wav', data: TRACK, title: 'Shared track', duration: 3
  });
  const sharedId = mineTrack.data.item.id;
  const saveOwn = await alice.call('POST', '/api/media/save', { id: sharedId });
  ok('cannot save your own track', saveOwn.status === 400, String(saveOwn.status));
  const saveOther = await bob.call('POST', '/api/media/save', { id: sharedId });
  ok('saving somebody else\'s track works', saveOther.status === 200 &&
    saveOther.data.item.url === mineTrack.data.item.url && saveOther.data.item.savedFrom === alice.user.id,
    JSON.stringify(saveOther.data && saveOther.data.item));
  const savedTwice = await bob.call('POST', '/api/media/save', { id: sharedId });
  ok('saving twice is refused', savedTwice.status === 400, String(savedTwice.status));

  const fileUrl = mineTrack.data.item.url;
  const delOrigin = await alice.call('POST', '/api/media/delete', { id: sharedId });
  ok('origin deleted', delOrigin.status === 200, String(delOrigin.status));
  const stillThere = await fetch(BASE + fileUrl);
  ok('file survives while somebody still uses it', stillThere.status === 200, String(stillThere.status));
  const bobCopy = await bob.call('GET', '/api/media?kind=audio&user=' + bob.user.id);
  const bobSaved = bobCopy.data.items.find(i => i.url === fileUrl);
  ok('saved copy remains on the other shelf', !!bobSaved, '');
  if (bobSaved) {
    await bob.call('POST', '/api/media/delete', { id: bobSaved.id });
    const goneNow = await fetch(BASE + fileUrl);
    ok('file removed once nobody uses it', goneNow.status === 404, String(goneNow.status));
  }

  // ---- playlists
  const plCreate = await alice.call('POST', '/api/playlist/save', { name: 'Evening mix' });
  ok('playlist created', plCreate.status === 200 && plCreate.data.id > 0, String(plCreate.status));
  const plId = plCreate.data && plCreate.data.id;
  const plEmptyName = await alice.call('POST', '/api/playlist/save', { name: '' });
  ok('playlist needs a name', plEmptyName.status === 400, String(plEmptyName.status));

  const plTrack = await alice.call('POST', '/api/media/upload', {
    kind: 'audio', fileName: 'pl.wav', data: TRACK, title: 'Playlist track', duration: 2
  });
  const plTrackId = plTrack.data.item.id;
  const plAdd = await alice.call('POST', '/api/playlist/item', { id: plId, media: plTrackId, add: true });
  ok('track added to playlist', plAdd.status === 200, String(plAdd.status));
  const plAddMissing = await alice.call('POST', '/api/playlist/item', { id: plId, media: 999999, add: true });
  ok('unknown track refused', plAddMissing.status === 404, String(plAddMissing.status));
  const plList = await alice.call('GET', '/api/playlists');
  ok('playlist lists one item', plList.data.playlists.some(p => p.id === plId && p.count === 1), '');
  const plItems = await alice.call('GET', '/api/playlist?id=' + plId);
  ok('playlist items returned', plItems.data.items.length === 1 && plItems.data.items[0].title === 'Playlist track', '');
  const plForeign = await bob.call('GET', '/api/playlist?id=' + plId);
  ok('playlist is private', plForeign.status === 404, String(plForeign.status));
  const plRemove = await alice.call('POST', '/api/playlist/item', { id: plId, media: plTrackId, add: false });
  const plAfterRemove = await alice.call('GET', '/api/playlist?id=' + plId);
  ok('track removed from playlist', plRemove.status === 200 && plAfterRemove.data.items.length === 0, '');
  const plDelete = await alice.call('POST', '/api/playlist/delete', { id: plId });
  ok('playlist deleted', plDelete.status === 200, String(plDelete.status));
  await alice.call('POST', '/api/media/delete', { id: plTrackId });

  await bob.call('POST', '/api/messages/typing', { to: alice.user.id });
  const typingWait = await alice.call('GET', '/api/messages/wait?with=' + bob.user.id + '&after=0&timeout=3');
  ok('typing is visible', typingWait.status === 200 && typingWait.data.typing === true, JSON.stringify(typingWait.data && typingWait.data.typing));

  const beforeWait = await alice.call('GET', '/api/messages?with=' + bob.user.id + '&limit=50');
  const lastSeen = beforeWait.data.messages.length ? beforeWait.data.messages[beforeWait.data.messages.length - 1].id : 0;
  const liveWait = await alice.call('GET', '/api/messages/wait?with=' + bob.user.id + '&after=' + lastSeen + '&timeout=1');
  ok('live wait answers', liveWait.status === 200 && typeof liveWait.data.latest === 'number', String(liveWait.status));
  const readState = await bob.call('GET', '/api/messages/wait?with=' + alice.user.id + '&after=0&timeout=1');
  ok('read receipt reported', readState.status === 200 && readState.data.read === true, JSON.stringify(readState.data && readState.data.read));

  const group = await alice.call('POST', '/api/group/create', { name: 'Test Group ' + stamp, description: 'x', visibility: 'public' });
  ok('group created', group.status === 200 && group.data.group.id > 0, String(group.status));
  const gid = group.data && group.data.group.id;
  const join = await bob.call('POST', '/api/group/join', { id: gid });
  ok('group joined', join.status === 200 && join.data.group.isMember === true, '');
  const gpost = await bob.call('POST', '/api/post', { body: 'group post', group: gid });
  ok('group post created', gpost.status === 200, String(gpost.status));
  const gfeed = await alice.call('GET', '/api/group/feed?id=' + gid);
  ok('group feed has post', gfeed.data && gfeed.data.posts.length === 1, '');

  const gedit = await alice.call('POST', '/api/group/edit', { id: gid, name: 'Renamed ' + stamp, description: 'updated', visibility: 'public' });
  ok('group edit', gedit.status === 200 && gedit.data.group.name === 'Renamed ' + stamp, String(gedit.status));
  const geditBad = await bob.call('POST', '/api/group/edit', { id: gid, name: 'hijack' });
  ok('non-owner group edit refused', geditBad.status === 403, String(geditBad.status));
  const gav = await alice.call('POST', '/api/group/avatar', { id: gid, data: PNG });
  ok('group avatar saved', gav.status === 200 && String(gav.data.avatar).indexOf('/media/groups/') === 0,
    String(gav.status));
  const kick = await alice.call('POST', '/api/group/kick', { id: gid, user: bob.user.id });
  ok('member kicked', kick.status === 200 && kick.data.group.members === 1, String(kick.status));

  const notif = await alice.call('GET', '/api/notifications');
  ok('notifications delivered', notif.data && notif.data.notifications.length > 0, String(notif.data && notif.data.unread));
  const readAll = await alice.call('POST', '/api/notifications/read', { all: true });
  ok('notifications marked read', readAll.data && readAll.data.unread === 0, '');

  const search = await alice.call('GET', '/api/search?q=' + encodeURIComponent('Bob'));
  ok('search finds user', search.data && search.data.users.length >= 1, '');

  const stats = await alice.call('GET', '/api/stats');
  ok('stats endpoint', stats.data && stats.data.users >= 2, '');

  const settings = await alice.call('POST', '/api/settings', { theme: 'dark', lang: 'en' });
  ok('settings saved', settings.status === 200 && settings.data.user.theme === 'dark', '');
  const badTheme = await alice.call('POST', '/api/settings', { theme: 'neon' });
  ok('bad theme rejected', badTheme.status === 400, String(badTheme.status));

  // ---- video, bookmarks, interests, badges
  const vpost = await alice.call('POST', '/api/post', {
    body: 'watch this', video: 'https://www.youtube.com/watch?v=dQw4w9WgXcQ'
  });
  ok('post with video', vpost.status === 200 && vpost.data.post.video === 'https://www.youtube.com/watch?v=dQw4w9WgXcQ',
    String(vpost.status));
  const badVideo = await alice.call('POST', '/api/post', { body: 'x', video: 'javascript:alert(1)' });
  ok('bad video link rejected', badVideo.status === 400, String(badVideo.status));

  const mark = await bob.call('POST', '/api/bookmark', { id: postId });
  ok('bookmark toggles on', mark.status === 200 && mark.data.bookmarked === true && mark.data.total === 1,
    JSON.stringify(mark.data));
  const marks = await bob.call('GET', '/api/bookmarks');
  ok('bookmarks listed', marks.status === 200 && marks.data.posts.length === 1 && marks.data.posts[0].id === postId, '');
  ok('bookmarked flag on post', marks.data.posts[0].bookmarked === true, '');
  const unmark = await bob.call('POST', '/api/bookmark', { id: postId });
  ok('bookmark toggles off', unmark.data && unmark.data.bookmarked === false, '');

  const tmpPost = await alice.call('POST', '/api/post', { body: 'temporary post' });
  const tmpId = tmpPost.data && tmpPost.data.post.id;
  await bob.call('POST', '/api/bookmark', { id: tmpId });
  await alice.call('POST', '/api/post/delete', { id: tmpId });
  const afterDel = await bob.call('GET', '/api/bookmarks');
  ok('bookmark dropped with the post', afterDel.data && afterDel.data.total === 0, JSON.stringify(afterDel.data && afterDel.data.total));

  // ---- post tools: edit, pin, likes, photos
  const edit = await alice.call('POST', '/api/post/edit', { id: postId, body: 'Hello edited', imageAction: 'keep' });
  ok('post edit works', edit.status === 200 && edit.data.post.body === 'Hello edited' && !!edit.data.post.edited,
    String(edit.status));
  const editBad = await bob.call('POST', '/api/post/edit', { id: postId, body: 'hijack' });
  ok('foreign post edit refused', editBad.status === 403, String(editBad.status));

  const pin = await alice.call('POST', '/api/post/pin', { id: postId, on: true });
  ok('post pinned', pin.status === 200 && pin.data.pinned === true, String(pin.status));
  const ownWall = await alice.call('GET', '/api/wall?user=' + alice.user.id);
  ok('pinned post first on the wall', ownWall.data.posts.length > 0 && ownWall.data.posts[0].id === postId &&
    ownWall.data.posts[0].pinned === true, JSON.stringify(ownWall.data.posts.map(p => p.id).slice(0, 3)));
  const unpin = await alice.call('POST', '/api/post/pin', { id: postId, on: false });
  ok('post unpinned', unpin.data && unpin.data.pinned === false, '');

  const likes = await alice.call('GET', '/api/likes?post=' + postId);
  ok('likes list', likes.status === 200 && likes.data.users.some(u => u.id === bob.user.id) && likes.data.total >= 1,
    String(likes.status));

  const photoPost = await alice.call('POST', '/api/post', { body: 'a picture', image: PNG });
  ok('post with image', photoPost.status === 200 && String(photoPost.data.post.image).indexOf('/media/posts/') === 0,
    String(photoPost.status));
  const photos = await alice.call('GET', '/api/photos?user=' + alice.user.id);
  ok('photos gallery lists the image', photos.status === 200 && photos.data.photos.length >= 1, String(photos.status));
  const profPhotos = await alice.call('GET', '/api/profile?user=' + alice.user.id);
  ok('profile returns recent photos', Array.isArray(profPhotos.data.photos) && profPhotos.data.photos.length >= 1, '');
  const dropPhoto = await alice.call('POST', '/api/post/delete', { id: photoPost.data.post.id });
  ok('photo post removed', dropPhoto.status === 200, String(dropPhoto.status));

  const interests = await alice.call('POST', '/api/profile', { favGames: 'Doom', favMusic: 'MIDI', mood: 'happy' });
  ok('interests saved', interests.status === 200 && interests.data.user.favGames === 'Doom' &&
    interests.data.user.mood === 'happy', String(interests.status));
  const badMood = await alice.call('POST', '/api/profile', { mood: 'angry' });
  ok('bad mood rejected', badMood.status === 400, String(badMood.status));

  const prof = await alice.call('GET', '/api/profile?user=' + alice.user.id);
  const badgeIds = String(prof.data && prof.data.user.badges || '').split(',');
  ok('first post badge granted', prof.status === 200 && badgeIds.indexOf('first_post') >= 0,
    JSON.stringify(prof.data && prof.data.user.badges));
  ok('site exposes badge catalogue', Array.isArray(hs.site && hs.site.badges) && hs.site.badges.length > 0, '');

  // ---- admin
  const adminOnPublic = await alice.call('GET', '/api/admin/overview');
  ok('admin API hidden on the public board', adminOnPublic.status === 404, String(adminOnPublic.status));

  let admin = null;
  if (ADMIN_BASE) {
    // sessions are per-host cookies, so log in again on the admin board
    if (expectAdmin) {
      admin = new Client('admin-test', ADMIN_BASE);
      const r = await admin.login(ua, 'alice-password-1');
      if (r.status !== 200) admin = null;
    } else if (process.env.SOCIAL_ADMIN_USER && process.env.SOCIAL_ADMIN_PASSWORD) {
      admin = new Client('admin', ADMIN_BASE);
      const r = await admin.login(process.env.SOCIAL_ADMIN_USER, process.env.SOCIAL_ADMIN_PASSWORD);
      if (r.status !== 200) admin = null;
    }
  } else if (expectAdmin) {
    admin = alice;
  }
  if (!admin) {
    console.log('  SKIP  admin tests (set SOCIAL_ADMIN_BASE and SOCIAL_ADMIN_USER / SOCIAL_ADMIN_PASSWORD)');
  } else {
    const adminBase = ADMIN_BASE || BASE;
    const overview = await admin.call('GET', '/api/admin/overview');
    ok('admin overview', overview.status === 200 && overview.data.counts.users >= 2, String(overview.status));
    ok('overview has runtime diagnostics', typeof overview.data.activeWaits === 'number' &&
      typeof overview.data.threadsFree === 'number' && typeof overview.data.memoryMB === 'number', '');
    const bobAdmin = new Client('bob-admin', adminBase);
    const bobLogin = await bobAdmin.login(ub, 'bob-password-1');
    const forbidden = bobLogin.status === 200 ? await bobAdmin.call('GET', '/api/admin/overview') : null;
    ok('admin area closed for users', forbidden !== null && forbidden.status === 403,
      forbidden ? String(forbidden.status) : 'login failed');
    const banned = await admin.call('POST', '/api/admin/user', { id: bob.user.id, action: 'ban', reason: 'test', days: 1 });
    ok('admin can ban', banned.status === 200 && banned.data.user.banned === true, String(banned.status));
    const bannedCall = await bob.call('GET', '/api/me');
    ok('banned user blocked', bannedCall.status === 403 || bannedCall.status === 401, String(bannedCall.status));
    const unbanned = await admin.call('POST', '/api/admin/user', { id: bob.user.id, action: 'unban' });
    ok('admin can unban', unbanned.status === 200 && unbanned.data.user.banned === false, '');
    const logs = await admin.call('GET', '/api/admin/logs?limit=20');
    ok('admin logs', logs.status === 200 && Array.isArray(logs.data.entries), '');
    const broadcast = await admin.call('POST', '/api/admin/broadcast', { text: 'test broadcast' });
    ok('broadcast', broadcast.status === 200 && broadcast.data.delivered >= 1, '');
    const backup = await admin.call('POST', '/api/admin/backup', {});
    ok('backup created', backup.status === 200 && typeof backup.data.path === 'string', '');

    const fbList = await admin.call('GET', '/api/admin/feedback');
    ok('admin sees feedback', fbList.status === 200 && Array.isArray(fbList.data.items), String(fbList.status));
    const firstFb = (fbList.data.items || [])[0];
    if (firstFb) {
      const fbUpdate = await admin.call('POST', '/api/admin/feedback/update',
        { id: firstFb.id, action: 'status', status: 'closed', note: 'handled by the suite' });
      ok('feedback can be closed', fbUpdate.status === 200, String(fbUpdate.status));
      const fbClosed = await admin.call('GET', '/api/admin/feedback?status=closed');
      ok('closed feedback listed', fbClosed.data.items.some(f => f.id === firstFb.id && f.note === 'handled by the suite'), '');
      const fbDel = await admin.call('POST', '/api/admin/feedback/update', { id: firstFb.id, action: 'delete' });
      ok('feedback can be deleted', fbDel.status === 200, String(fbDel.status));
    }
    const fbBad = await admin.call('POST', '/api/admin/feedback/update', { id: 999999, action: 'status', status: 'read' });
    ok('unknown feedback rejected', fbBad.status === 404, String(fbBad.status));
  }

  // ---- login / recovery / logout
  const alice2 = new Client('alice2');
  const relogin = await alice2.login(ua, 'alice-password-1');
  ok('login works', relogin.status === 200 && !!alice2.key, String(relogin.status));
  const wrongPass = new Client('alice3');
  const bad = await wrongPass.login(ua, 'wrong-password-1');
  ok('wrong password rejected', bad.status === 401, String(bad.status));

  const recover = new Client('recover');
  await recover.handshake();
  const verifier = verifierOf('brand-new-password', ua, recover.iterations);
  const salt = crypto.randomBytes(16).toString('hex');
  const k0 = pbkdf2(Buffer.from(alice.recovery, 'utf8'), fromHex(salt), recover.iterations, 32);
  const ts2 = String(Math.floor(Date.now() / 1000));
  const nonce2 = crypto.randomBytes(16).toString('hex');
  const wire = seal(k0, Buffer.from(JSON.stringify({ newVerifier: verifier }), 'utf8'));
  const sig2 = signRequest(k0, 'POST', '/api/recover', ts2, nonce2, Buffer.from(wire, 'utf8'));
  const recRes = await recover.raw('POST', '/api/recover', wire, {
    'X-Api-Ts': ts2, 'X-Api-Nonce': nonce2, 'X-Api-Sig': sig2, 'X-Api-Enc': '1',
    'X-Auth-User': ua, 'X-Auth-Recover': alice.recovery, 'X-Auth-Salt': salt
  });
  const recPlain = open(k0, recRes.text);
  ok('recovery code works', recRes.status === 200 && recPlain !== null, String(recRes.status));

  const afterRecover = new Client('after');
  const newLogin = await afterRecover.login(ua, 'brand-new-password');
  ok('login with new password', newLogin.status === 200 && !!afterRecover.key, String(newLogin.status));

  const logout = await afterRecover.call('POST', '/api/logout', {});
  ok('logout ok', logout.status === 200, '');
  const afterLogout = await afterRecover.call('GET', '/api/me');
  ok('session gone after logout', afterLogout.status === 401, String(afterLogout.status));

  console.log('-------------------------------------------------------------');
  console.log('passed: ' + passed + '   failed: ' + failures.length);
  if (failures.length) {
    for (const f of failures) console.log('  - ' + f);
    process.exit(1);
  }
  console.log('ALL API TESTS PASSED');
}

main().catch(err => {
  console.error('test client crashed: ' + (err && err.stack || err));
  process.exit(2);
});
