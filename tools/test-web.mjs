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

  /// Read-only session. The verifier uses the fixed __guest__ pseudo-username,
  /// exactly like the SPA does, so the shared secret never travels anywhere.
  async guest(password) {
    await this.handshake();
    const verifier = verifierOf(password || '', '__guest__', this.iterations);
    const salt = crypto.randomBytes(16).toString('hex');
    const k0 = pbkdf2(Buffer.from(verifier, 'utf8'), fromHex(salt), this.iterations, 32);
    const r = await this.authCall('/api/guest', {
      'X-Auth-Guest': verifier, 'X-Auth-Salt': salt
    }, k0, {});
    if (r.status === 200 && r.data) {
      this.key = pbkdf2(Buffer.from(verifier, 'utf8'), fromHex(r.data.sessionSalt), this.iterations, 32);
      this.guest = true;
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

  // a clip picked from the gallery
  const MP4 = 'data:video/mp4;base64,' + Buffer.concat([
    Buffer.from([0, 0, 0, 0x18]), Buffer.from('ftypisom'), Buffer.from([0, 0, 2, 0]),
    Buffer.from('isomiso2mp41'), Buffer.from([0, 0, 0, 8]), Buffer.from('mdat')
  ]).toString('base64');
  const clipMsg = await bob.call('POST', '/api/messages/send', {
    to: alice.user.id, body: '', video: MP4
  });
  ok('clip message saved', clipMsg.status === 200 && clipMsg.data.message.video &&
    clipMsg.data.message.video.indexOf('/media/messages/') === 0,
    String(clipMsg.status) + ' ' + (clipMsg.data && clipMsg.data.message && clipMsg.data.message.video));
  const fakeClip = await bob.call('POST', '/api/messages/send', {
    to: alice.user.id, body: '', video: 'data:video/mp4;base64,' + Buffer.from('<html>nope</html>').toString('base64')
  });
  ok('a fake clip is refused', fakeClip.status === 400, String(fakeClip.status));

  // executables never travel: the whitelist has no .exe/.apk/.scr
  for (const bad of ['virus.exe', 'app.apk', 'screen.scr']) {
    const sent = await bob.call('POST', '/api/messages/send', {
      to: alice.user.id, body: '', file: 'data:application/octet-stream;base64,' + Buffer.from('MZ').toString('base64'),
      fileName: bad
    });
    ok('executable refused: ' + bad, sent.status === 400, String(sent.status));
  }
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

  // ---- calls (signalling only; media is peer to peer)
  const callStart = await alice.call('POST', '/api/call/start', { with: bob.user.id, video: false });
  ok('call started', callStart.status === 200 && callStart.data.call.id > 0 &&
    callStart.data.call.kind === 'audio', String(callStart.status));
  const callId = callStart.data && callStart.data.call.id;

  const bobRing = await bob.call('GET', '/api/call/wait?after=0&timeout=5');
  const incoming = (bobRing.data.events || []).filter(e => e.type === 'incoming');
  ok('callee gets the ring', bobRing.status === 200 && incoming.length === 1 &&
    incoming[0].call === callId, JSON.stringify(bobRing.data.events && bobRing.data.events.map(e => e.type)));

  const answer = await bob.call('POST', '/api/call/answer', { id: callId, accept: true });
  ok('call answered', answer.status === 200 && answer.data.call.members.some(m => m.id === bob.user.id && m.accepted), '');
  const aliceAccepted = await alice.call('GET', '/api/call/wait?after=0&timeout=5');
  ok('caller sees the answer', (aliceAccepted.data.events || []).some(e => e.type === 'accepted' && e.from === bob.user.id), '');

  const signal = await alice.call('POST', '/api/call/signal', {
    id: callId, to: bob.user.id, payload: JSON.stringify({ kind: 'offer', data: { type: 'offer', sdp: 'v=0 test' } })
  });
  ok('signal relayed', signal.status === 200, String(signal.status));
  const bobSignal = await bob.call('GET', '/api/call/wait?after=0&timeout=5');
  const gotSignal = (bobSignal.data.events || []).filter(e => e.type === 'signal');
  ok('peer receives the offer', gotSignal.length === 1 && gotSignal[0].from === alice.user.id &&
    gotSignal[0].payload && gotSignal[0].payload.kind === 'offer', JSON.stringify(gotSignal[0] && gotSignal[0].payload));

  const badSignal = await bob.call('POST', '/api/call/signal', { id: callId, to: alice.user.id, payload: 'not json' });
  ok('garbage signal rejected', badSignal.status === 400, String(badSignal.status));

  const outsider = await new Client('outsider');
  const outsiderReg = await outsider.register('outsider_' + stamp, 'outsider-pass-1');
  if (outsiderReg.status === 200) {
    const outsiderSignal = await outsider.call('POST', '/api/call/signal', {
      id: callId, to: alice.user.id, payload: JSON.stringify({ kind: 'offer', data: {} })
    });
    ok('outsiders cannot inject signalling', outsiderSignal.status === 403, String(outsiderSignal.status));
  }

  const leave = await bob.call('POST', '/api/call/leave', { id: callId });
  ok('call left', leave.status === 200, String(leave.status));
  const aliceLeft = await alice.call('GET', '/api/call/wait?after=0&timeout=5');
  ok('caller is told about the hangup',
    (aliceLeft.data.events || []).some(e => e.type === 'left' || e.type === 'ended'), '');
  const history = await alice.call('GET', '/api/call/history');
  ok('call history recorded', history.data.calls.some(c => c.id === callId), '');

  // conference room: two people join the same name
  const roomJoin = await alice.call('POST', '/api/call/start', { room: 'team_' + stamp, video: true });
  ok('room opened', roomJoin.status === 200 && roomJoin.data.call.room === 'team_' + stamp, String(roomJoin.status));
  const roomId = roomJoin.data.call.id;
  const roomJoin2 = await bob.call('POST', '/api/call/start', { room: 'team_' + stamp, video: false });
  ok('second person joins the same room', roomJoin2.status === 200 && roomJoin2.data.call.id === roomId &&
    roomJoin2.data.call.members.length === 2, JSON.stringify(roomJoin2.data.call.members.length));
  const badRoom = await alice.call('POST', '/api/call/start', { room: 'x' });
  ok('too short room name rejected', badRoom.status === 400, String(badRoom.status));
  await alice.call('POST', '/api/call/leave', { id: roomId });
  await bob.call('POST', '/api/call/leave', { id: roomId });

  // ---- gifts
  const giftSent = await alice.call('POST', '/api/gifts/send', {
    to: bob.user.id, kind: 'heart', message: 'for the test suite'
  });
  ok('gift sent', giftSent.status === 200 && giftSent.data.gift.kind === 'heart' &&
    giftSent.data.gift.from && giftSent.data.gift.from.id === alice.user.id,
    JSON.stringify(giftSent.data && giftSent.data.gift));
  const giftId = giftSent.data && giftSent.data.gift && giftSent.data.gift.id;

  const bobGifts = await bob.call('GET', '/api/gifts');
  ok('recipient sees the gift', bobGifts.status === 200 && bobGifts.data.total === 1 &&
    bobGifts.data.gifts.some(g => g.id === giftId && g.message === 'for the test suite'),
    JSON.stringify(bobGifts.data));
  ok('gift is unseen until opened', bobGifts.data.unseen === 1, String(bobGifts.data.unseen));

  const giftNotif = await bob.call('GET', '/api/notifications?limit=20');
  ok('gift shows up in notifications',
    (giftNotif.data.notifications || []).some(n => n.type === 'gift' && n.actor && n.actor.id === alice.user.id), '');

  const sentList = await alice.call('GET', '/api/gifts/sent');
  ok('sender sees it in "sent"', sentList.data.gifts.some(g => g.id === giftId), '');

  const anonGift = await alice.call('POST', '/api/gifts/send', {
    to: bob.user.id, kind: 'cake', anonymous: true, message: 'secret admirer'
  });
  ok('anonymous gift sent', anonGift.status === 200 && anonGift.data.gift.anonymous === true,
    String(anonGift.status));
  const bobGifts2 = await bob.call('GET', '/api/gifts');
  const anonRow = (bobGifts2.data.gifts || []).filter(g => g.anonymous)[0];
  ok('anonymous gift hides the sender', !!anonRow && anonRow.from === null,
    JSON.stringify(anonRow && anonRow.from));

  const seen = await bob.call('POST', '/api/gifts/seen', { id: 0 });
  ok('gifts marked seen', seen.status === 200 && seen.data.unseen === 0, String(seen.data.unseen));

  const badGift = await alice.call('POST', '/api/gifts/send', { to: bob.user.id, kind: 'nonsense' });
  ok('unknown gift rejected', badGift.status === 400, String(badGift.status));
  const selfGift = await alice.call('POST', '/api/gifts/send', { to: alice.user.id, kind: 'heart' });
  ok('cannot gift yourself', selfGift.status === 400, String(selfGift.status));

  const profileGift = await bob.call('GET', '/api/profile?user=' + bob.user.id);
  ok('profile carries the gift counter', profileGift.data.giftCount === 2, String(profileGift.data.giftCount));

  const hidden = await bob.call('POST', '/api/gifts/delete', { id: giftId });
  ok('recipient can hide a gift', hidden.status === 200 && hidden.data.total === 1, String(hidden.status));
  const notMine = await alice.call('POST', '/api/gifts/delete', { id: anonGift.data.gift.id });
  ok('only the recipient can hide a gift', notMine.status === 404, String(notMine.status));

  // ---- Flash games (Ruffle)
  // Ruffle is vendored, not committed: a fresh clone must run
  // tools\fetch-ruffle.ps1 first and the shelf stays off until it does.
  const siteInfo = await alice.call('GET', '/api/settings');
  const gamesOn = !!(siteInfo.data && siteInfo.data.site && siteInfo.data.site.gamesEnabled);
  if (!gamesOn) {
    ok('games shelf is off when Ruffle is missing (skipped)', true, 'no ruffle bundle');
  } else {
  const swf = Buffer.concat([
    Buffer.from('FWS'), Buffer.from([6]), Buffer.alloc(4),
    Buffer.from([0x00, 0x00, 0x0C, 0x01, 0x00]),        // RECT: 11000 x 8000 twips
    Buffer.from([0x00, 0x0C]), Buffer.from([0x01, 0x00]), // 12 fps, 1 frame
    Buffer.from([0x43, 0x02, 0xE0, 0x28, 0x22]),        // SetBackgroundColor red
    Buffer.from([0x00, 0x00])                           // End
  ]);
  swf.writeUInt32LE(swf.length, 4);
  const SWF = 'data:application/x-shockwave-flash;base64,' + swf.toString('base64');

  const up = await alice.call('POST', '/api/game/upload', {
    data: SWF, title: 'Test game', description: 'uploaded by the suite'
  });
  // uploading a .swf is a privilege: an ordinary account is refused until an
  // administrator ticks "allow game uploads"
  const noPerm = await bob.call('POST', '/api/game/upload', { data: SWF, title: 'Not allowed' });
  ok('game upload needs permission', noPerm.status === 403 &&
    noPerm.data.error === 'no-permission', JSON.stringify(noPerm.data));
  ok('game uploaded', up.status === 200 && up.data.game.kind === 'game' &&
    up.data.game.url.startsWith('/media/games/') && up.data.game.url.endsWith('.swf') &&
    up.data.game.size === swf.length,
    JSON.stringify(up.data && up.data.game));
  const gameId = up.data && up.data.game && up.data.game.id;

  const fake = await alice.call('POST', '/api/game/upload', {
    data: 'data:application/x-shockwave-flash;base64,' + Buffer.from('<html>nope</html>').toString('base64'),
    title: 'Not a game'
  });
  ok('a renamed html file is not accepted as a game', fake.status === 400 && fake.data.error === 'bad-swf',
    JSON.stringify(fake.data));

  const shelf = await bob.call('GET', '/api/games');
  ok('game appears on the shelf', shelf.status === 200 && shelf.data.games.some(g => g.id === gameId), '');
  const searched = await bob.call('GET', '/api/games?q=' + encodeURIComponent('test game'));
  ok('games are searchable', searched.data.games.some(g => g.id === gameId), '');
  const missSearch = await bob.call('GET', '/api/games?q=zzz-nothing');
  ok('game search misses cleanly', missSearch.data.games.length === 0, '');

  const one = await bob.call('GET', '/api/game?id=' + gameId);
  ok('single game fetch', one.status === 200 && one.data.game.title === 'Test game', '');
  const missing = await bob.call('GET', '/api/game?id=999999');
  ok('unknown game 404s', missing.status === 404, String(missing.status));

  const played = await bob.call('POST', '/api/game/play', { id: gameId });
  ok('play counter grows', played.status === 200 && played.data.plays === 1, JSON.stringify(played.data));

  const liked = await bob.call('POST', '/api/game/like', { id: gameId });
  ok('game liked', liked.status === 200 && liked.data.liked === true && liked.data.likes === 1, '');
  const unliked = await bob.call('POST', '/api/game/like', { id: gameId });
  ok('game like toggles off', unliked.data.liked === false && unliked.data.likes === 0, '');

  const stolen = await bob.call('POST', '/api/game/edit', { id: gameId, title: 'Mine now' });
  ok('only the owner may rename a game', stolen.status === 403, String(stolen.status));
  const edited = await alice.call('POST', '/api/game/edit', { id: gameId, title: 'Renamed game' });
  ok('owner renames the game', edited.status === 200 && edited.data.game.title === 'Renamed game', '');

  // the movie must never come back as something a browser would run
  const movie = await bob.raw('GET', up.data.game.url);
  ok('swf is served as an inert download',
    movie.status === 200 &&
    (movie.headers.get('content-type') || '').indexOf('application/octet-stream') === 0 &&
    (movie.headers.get('content-disposition') || '').indexOf('attachment') >= 0 &&
    (movie.headers.get('x-content-type-options') || '') === 'nosniff',
    movie.headers.get('content-type') + ' | ' + movie.headers.get('content-disposition'));

  const player = await bob.raw('GET', '/play/' + gameId);
  ok('player page loads', player.status === 200 &&
    player.text.indexOf('/assets/vendor/ruffle/ruffle.js') >= 0 &&
    player.text.indexOf('/assets/js/player.js') >= 0, String(player.status));
  ok('player page has no inline script', player.text.indexOf('<script>') < 0, '');
  ok('player page allows wasm but not inline script',
    /script-src[^;]*wasm-unsafe-eval/.test(player.headers.get('content-security-policy') || '') &&
    !/script-src[^;]*unsafe-inline/.test(player.headers.get('content-security-policy') || ''),
    String(player.headers.get('content-security-policy')).slice(0, 120));
  const badPlayer = await bob.raw('GET', '/play/999999');
  ok('player page 404s for an unknown game', badPlayer.status === 404, String(badPlayer.status));

  const foreignDelete = await bob.call('POST', '/api/game/delete', { id: gameId });
  ok('only the owner may delete a game', foreignDelete.status === 403, String(foreignDelete.status));
  const removed = await alice.call('POST', '/api/game/delete', { id: gameId });
  ok('owner deletes the game', removed.status === 200, String(removed.status));
  const gone = await alice.call('GET', '/api/game?id=' + gameId);
  ok('deleted game is gone', gone.status === 404, String(gone.status));
  }

  // ---- communities: roles, join requests, moderation queue
  const comm = await alice.call('POST', '/api/group/create', {
    name: 'Review comm ' + stamp, description: 'needs approval', visibility: 'private'
  });
  ok('private community created', comm.status === 200 && comm.data.group.visibility === 'private' &&
    comm.data.group.role === 'owner' && comm.data.group.postPolicy === 'all',
    JSON.stringify(comm.data && comm.data.group));
  const commId = comm.data.group.id;

  // a stranger who never joins: used to prove closed communities stay closed
  const stranger0 = await new Client('stranger0');
  await stranger0.register('str0_' + stamp, 'outsider-pass-1');

  // a closed community cannot be joined directly
  const directJoin = await bob.call('POST', '/api/group/join', { id: commId });
  ok('joining a closed community sends a request', directJoin.status === 200 &&
    directJoin.data.requested === true && directJoin.data.group.isMember === false,
    JSON.stringify(directJoin.data && directJoin.data.group && directJoin.data.group.isMember));

  const reqs = await alice.call('GET', '/api/group/requests?id=' + commId);
  ok('owner sees the request', reqs.status === 200 &&
    reqs.data.requests.some(r => r.user === bob.user.id), '');
  const bobReqs = await bob.call('GET', '/api/group/requests?id=' + commId);
  ok('outsiders cannot read the request list', bobReqs.status === 403, String(bobReqs.status));

  const accepted = await alice.call('POST', '/api/group/answer', { id: commId, user: bob.user.id, accept: true });
  ok('owner accepts the request', accepted.status === 200 && accepted.data.group.members === 2, '');
  const acceptedAgain = await alice.call('POST', '/api/group/answer', { id: commId, user: bob.user.id, accept: true });
  ok('answering twice fails cleanly', acceptedAgain.status === 404, String(acceptedAgain.status));

  // roles
  const promote = await alice.call('POST', '/api/group/role', { id: commId, user: bob.user.id, role: 'editor' });
  ok('owner promotes a member to editor', promote.status === 200, String(promote.status));
  const bobGroup = await bob.call('GET', '/api/group?id=' + commId);
  ok('editor sees the role and the tools', bobGroup.data.group.role === 'editor' &&
    bobGroup.data.group.canModerate === true, JSON.stringify(bobGroup.data.group.role));

  const selfPromote = await bob.call('POST', '/api/group/role', { id: commId, user: bob.user.id, role: 'member' });
  ok('an editor cannot demote themselves', selfPromote.status === 403, String(selfPromote.status));
  const ownerRole = await alice.call('POST', '/api/group/role', { id: commId, user: alice.user.id, role: 'editor' });
  ok('the owner keeps the owner role', ownerRole.status === 400, String(ownerRole.status));
  const badRole = await alice.call('POST', '/api/group/role', { id: commId, user: bob.user.id, role: 'king' });
  ok('unknown role rejected', badRole.status === 400, String(badRole.status));

  // moderation queue: members post, editors approve
  const policy = await alice.call('POST', '/api/group/edit', {
    id: commId, name: 'Review comm ' + stamp, description: 'needs approval',
    visibility: 'private', postPolicy: 'review'
  });
  ok('community switched to review-before-publish', policy.status === 200 &&
    policy.data.group.postPolicy === 'review', JSON.stringify(policy.data.group.postPolicy));

  // bob is an editor, so his posts skip the queue
  const editorPost = await bob.call('POST', '/api/post', { body: 'editor post', group: commId });
  ok('editors post without review', editorPost.status === 200 &&
    editorPost.data.post.pending === false, JSON.stringify(editorPost.data.post.pending));

  // demote bob, then his post waits for approval
  await alice.call('POST', '/api/group/role', { id: commId, user: bob.user.id, role: 'member' });
  const queued = await bob.call('POST', '/api/post', { body: 'member post awaiting review', group: commId });
  ok('a member post lands in the queue', queued.status === 200 && queued.data.post.pending === true,
    JSON.stringify(queued.data.post.pending));
  const queuedId = queued.data.post.id;

  const wallBefore = await bob.call('GET', '/api/group?id=' + commId);
  ok('queued post is not on the wall', !wallBefore.data.posts.some(p => p.id === queuedId), '');
  const outsideFeed = await bob.call('GET', '/api/post?id=' + queuedId);
  ok('queued post is not readable by id', outsideFeed.status === 403 || outsideFeed.status === 404,
    String(outsideFeed.status));

  const queue = await alice.call('GET', '/api/group/pending?id=' + commId);
  ok('owner sees the queue', queue.status === 200 &&
    queue.data.posts.some(p => p.id === queuedId), '');
  const bobQueue = await bob.call('GET', '/api/group/pending?id=' + commId);
  ok('a plain member cannot read the queue', bobQueue.status === 403, String(bobQueue.status));

  const approved = await alice.call('POST', '/api/group/review', { id: commId, post: queuedId, approve: true });
  ok('owner publishes the queued post', approved.status === 200 && approved.data.approved === true, '');
  const nowVisible = await bob.call('GET', '/api/group?id=' + commId);
  ok('approved post appears on the wall', nowVisible.data.posts.some(p => p.id === queuedId), '');
  const ownerSees = await alice.call('GET', '/api/group?id=' + commId);
  ok('the owner sees a member post in a closed community',
    ownerSees.data.posts.some(p => p.id === queuedId), '');
  const outsiderSees = await stranger0.call('GET', '/api/group?id=' + commId);
  ok('an outsider gets a teaser, not the wall',
    outsiderSees.status === 200 && outsiderSees.data.limited === true &&
    outsiderSees.data.posts.length === 0, JSON.stringify(outsiderSees.data && outsiderSees.data.limited));
  const outsiderPost = await stranger0.call('GET', '/api/post?id=' + queuedId);
  ok('an outsider cannot read the post by id', outsiderPost.status === 403 || outsiderPost.status === 404,
    String(outsiderPost.status));
  const reviewTwice = await alice.call('POST', '/api/group/review', { id: commId, post: queuedId, approve: true });
  ok('reviewing twice fails cleanly', reviewTwice.status === 400, String(reviewTwice.status));

  // reject path
  const second = await bob.call('POST', '/api/post', { body: 'spam for the queue', group: commId });
  const rejected = await alice.call('POST', '/api/group/review', {
    id: commId, post: second.data.post.id, approve: false
  });
  ok('owner rejects a queued post', rejected.status === 200 && rejected.data.approved === false, '');
  const goneQueued = await bob.call('GET', '/api/post?id=' + second.data.post.id);
  ok('rejected post is gone', goneQueued.status === 404, String(goneQueued.status));

  // editors-only mode
  await alice.call('POST', '/api/group/edit', {
    id: commId, name: 'Review comm ' + stamp, description: 'needs approval',
    visibility: 'private', postPolicy: 'editors'
  });
  const memberTries = await bob.call('POST', '/api/post', { body: 'not allowed', group: commId });
  ok('editors-only blocks a member', memberTries.status === 403, String(memberTries.status));

  // leaving drops the editor badge too
  await alice.call('POST', '/api/group/role', { id: commId, user: bob.user.id, role: 'editor' });
  const left = await bob.call('POST', '/api/group/leave', { id: commId });
  ok('member can leave', left.status === 200 && left.data.group.isMember === false, '');
  const back = await alice.call('POST', '/api/group/answer', { id: commId, user: bob.user.id, accept: true });
  ok('a left member needs a new request', back.status === 404, String(back.status));

  // an editor may not remove another editor
  await bob.call('POST', '/api/group/join', { id: commId });
  await alice.call('POST', '/api/group/answer', { id: commId, user: bob.user.id, accept: true });
  await alice.call('POST', '/api/group/role', { id: commId, user: bob.user.id, role: 'editor' });
  const stranger = await new Client('stranger');
  await stranger.register('out_' + stamp, 'outsider-pass-1');
  const outsiderKick = await stranger.call('POST', '/api/group/kick', { id: commId, user: bob.user.id });
  ok('a stranger cannot kick editors', outsiderKick.status === 403, String(outsiderKick.status));

  // ---- photo albums
  const p1 = await alice.call('POST', '/api/post', { body: 'album photo one', image: PNG });
  const p2 = await alice.call('POST', '/api/post', { body: 'album photo two', image: PNG });
  ok('two photos posted', p1.status === 200 && p2.status === 200, '');

  const albumNew = await alice.call('POST', '/api/album/save', {
    name: 'Summer', description: 'the test album'
  });
  ok('album created', albumNew.status === 200 && albumNew.data.album.name === 'Summer' &&
    albumNew.data.album.photos === 0, JSON.stringify(albumNew.data && albumNew.data.album));
  const albumId = albumNew.data.album.id;

  const emptyName = await alice.call('POST', '/api/album/save', { name: '   ' });
  ok('an album needs a name', emptyName.status === 400, String(emptyName.status));

  const unfiled = await alice.call('GET', '/api/album/unfiled');
  ok('unfiled photos are listed', unfiled.data.photos.some(x => x.id === p1.data.post.id), '');

  const moved = await alice.call('POST', '/api/album/assign', {
    post: p1.data.post.id, album: albumId
  });
  ok('photo moved into the album', moved.status === 200 && moved.data.album === albumId, '');

  const albumPage = await alice.call('GET', '/api/album?id=' + albumId);
  ok('album shows its photo', albumPage.data.album.photos === 1 &&
    albumPage.data.photos.some(x => x.id === p1.data.post.id), '');
  ok('album cover falls back to the first photo',
    (albumPage.data.album.cover || '').indexOf('/media/') === 0, String(albumPage.data.album.cover));

  const notMineAssign = await bob.call('POST', '/api/album/assign', {
    post: p1.data.post.id, album: albumId
  });
  ok('nobody else can move my photo', notMineAssign.status === 403, String(notMineAssign.status));
  const notMineAlbum = await bob.call('POST', '/api/album/save', { id: albumId, name: 'Hijacked' });
  ok('nobody else can rename my album', notMineAlbum.status === 403, String(notMineAlbum.status));

  const albumsOf = await alice.call('GET', '/api/albums');
  ok('album appears on the shelf', albumsOf.data.albums.some(a => a.id === albumId), '');
  const bobAlbums = await bob.call('GET', '/api/albums?user=' + alice.user.id);
  ok('somebody else sees the album but cannot create one',
    bobAlbums.data.albums.some(a => a.id === albumId) && bobAlbums.data.canCreate === false, '');

  const backOut = await alice.call('POST', '/api/album/assign', {
    post: p1.data.post.id, album: 0
  });
  ok('photo can be taken out of an album', backOut.status === 200 && backOut.data.album === 0, '');

  const deleteAlbum = await alice.call('POST', '/api/album/delete', { id: albumId });
  ok('album deleted', deleteAlbum.status === 200, String(deleteAlbum.status));
  const albumGone = await alice.call('GET', '/api/album?id=' + albumId);
  ok('deleted album is gone', albumGone.status === 404, String(albumGone.status));
  const photoStill = await alice.call('GET', '/api/post?id=' + p1.data.post.id);
  ok('photos survive their album', photoStill.status === 200, String(photoStill.status));

  // ---- per-area privacy
  const closed = await bob.call('POST', '/api/settings', {
    privacyPrefs: { write: 'nobody', call: 'nobody', gifts: 'nobody', albums: 'nobody', friends: 'nobody' }
  });
  ok('privacy saved', closed.status === 200 &&
    closed.data.user && closed.data.user.privacyPrefs &&
    closed.data.user.privacyPrefs.write === 'nobody' &&
    closed.data.user.privacyPrefs.wall === 'all',
    JSON.stringify(closed.data && closed.data.user && closed.data.user.privacyPrefs));

  const stranger3 = await new Client('stranger3');
  await stranger3.register('st3_' + stamp, 'outsider-pass-1');

  const blockedWrite = await stranger3.call('POST', '/api/messages/send', {
    to: bob.user.id, body: 'should not arrive'
  });
  ok('nobody may write when write=nobody', blockedWrite.status === 403, String(blockedWrite.status));

  const blockedCall = await stranger3.call('POST', '/api/call/start', { with: bob.user.id });
  ok('nobody may call when call=nobody', blockedCall.status === 403, String(blockedCall.status));

  const blockedGift = await stranger3.call('POST', '/api/gifts/send', {
    to: bob.user.id, kind: 'heart'
  });
  ok('nobody may gift when gifts=nobody', blockedGift.status === 403, String(blockedGift.status));

  const blockedAlbums = await stranger3.call('GET', '/api/albums?user=' + bob.user.id);
  ok('nobody may see albums when albums=nobody', blockedAlbums.status === 403, String(blockedAlbums.status));

  const blockedFriends = await stranger3.call('GET', '/api/friends?user=' + bob.user.id);
  ok('nobody may see friends when friends=nobody', blockedFriends.status === 403, String(blockedFriends.status));

  // the owner is never locked out of their own settings
  const ownProfile = await bob.call('GET', '/api/profile?user=' + bob.user.id);
  ok('the owner still sees their own privacy switches', ownProfile.status === 200 &&
    ownProfile.data.user.privacyPrefs.write === 'nobody', '');

  // a friend is allowed where the rule says friends
  await bob.call('POST', '/api/settings', { privacyPrefs: { write: 'friends' } });
  const friendWrite = await alice.call('POST', '/api/messages/send', {
    to: bob.user.id, body: 'friends may write'
  });
  ok('friends may write when write=friends', friendWrite.status === 200, String(friendWrite.status));
  const notFriendWrite = await stranger3.call('POST', '/api/messages/send', {
    to: bob.user.id, body: 'still not allowed'
  });
  ok('a stranger still cannot write when write=friends', notFriendWrite.status === 403,
    String(notFriendWrite.status));

  // back to open so the rest of the suite is unaffected
  await bob.call('POST', '/api/settings', { privacyPrefs: {} });
  const reopened = await stranger3.call('POST', '/api/messages/send', {
    to: bob.user.id, body: 'open again'
  });
  ok('clearing the switches reopens everything', reopened.status === 200, String(reopened.status));

  // ---- profile cover and the cosmetic VIP label
  const cover = await alice.call('POST', '/api/profile/cover', { data: PNG });
  ok('cover uploaded', cover.status === 200 && cover.data.cover.indexOf('/media/avatars/') === 0,
    JSON.stringify(cover.data));
  const covered = await alice.call('GET', '/api/profile?user=' + alice.user.id);
  ok('cover comes back on the profile', covered.data.user.cover === cover.data.cover, '');
  const coverCleared = await alice.call('POST', '/api/profile/cover', { data: '' });
  ok('cover can be cleared', coverCleared.status === 200 && coverCleared.data.cover === '', '');
  const badCover = await alice.call('POST', '/api/profile/cover', {
    data: 'data:image/png;base64,' + Buffer.from('<html>nope</html>').toString('base64')
  });
  ok('a fake image is not a cover', badCover.status === 400, String(badCover.status));

  // ---- stories
  const storyNew = await alice.call('POST', '/api/story/add', {
    image: PNG, caption: 'first story'
  });
  ok('story published', storyNew.status === 200 && storyNew.data.story.image.indexOf('/media/stories/') === 0 &&
    storyNew.data.story.caption === 'first story', JSON.stringify(storyNew.data && storyNew.data.story));
  const storyId = storyNew.data.story.id;
  ok('story expires in about a day', storyNew.data.story.expiresIn > 86000 &&
    storyNew.data.story.expiresIn <= 86400, String(storyNew.data.story.expiresIn));

  const emptyStory = await alice.call('POST', '/api/story/add', { caption: 'nothing here' });
  ok('a story needs a photo or a clip', emptyStory.status === 400, String(emptyStory.status));
  const bothStory = await alice.call('POST', '/api/story/add', { image: PNG, video: PNG });
  ok('a story cannot be both photo and clip', bothStory.status === 400, String(bothStory.status));
  const notVideo = await alice.call('POST', '/api/story/add', {
    video: 'data:video/mp4;base64,' + Buffer.from('<html>nope</html>').toString('base64')
  });
  ok('a fake clip is rejected', notVideo.status === 400, String(notVideo.status));

  const tray = await bob.call('GET', '/api/stories');
  const aliceGroup = (tray.data.groups || []).filter(g => g.user && g.user.id === alice.user.id)[0];
  ok('story shows up in the bar', tray.status === 200 && !!aliceGroup &&
    aliceGroup.count >= 1 && aliceGroup.unseen === true, JSON.stringify(tray.data.groups && tray.data.groups.length));
  const ownTray = await alice.call('GET', '/api/stories');
  const ownGroup = (ownTray.data.groups || []).filter(g => g.user && g.user.id === alice.user.id)[0];
  ok('own stories are not "unseen" for the author', !!ownGroup && ownGroup.mine === true &&
    ownGroup.unseen === false, JSON.stringify(ownGroup && ownGroup.unseen));

  const oneStory = await bob.call('GET', '/api/story?id=' + storyId);
  ok('single story fetch', oneStory.status === 200 && oneStory.data.story.id === storyId &&
    oneStory.data.story.seen === false, '');

  const seenStory = await bob.call('POST', '/api/story/seen', { id: storyId });
  ok('story marked as seen', seenStory.status === 200, String(seenStory.status));
  const seenAgain = await bob.call('POST', '/api/story/seen', { id: storyId });
  ok('seeing twice is harmless', seenAgain.status === 200, String(seenAgain.status));

  const viewers = await alice.call('GET', '/api/story/viewers?id=' + storyId);
  ok('author sees who watched', viewers.status === 200 &&
    viewers.data.viewers.some(v => v.id === bob.user.id), JSON.stringify(viewers.data && viewers.data.viewers));
  const foreignViewers = await bob.call('GET', '/api/story/viewers?id=' + storyId);
  ok('nobody else sees the viewer list', foreignViewers.status === 403, String(foreignViewers.status));

  const ownView = await alice.call('POST', '/api/story/seen', { id: storyId });
  ok('the author does not count as a viewer', ownView.status === 200, '');
  const viewersAgain = await alice.call('GET', '/api/story/viewers?id=' + storyId);
  ok('author is not in the viewer list',
    !(viewersAgain.data.viewers || []).some(v => v.id === alice.user.id), '');

  const storySeenByBob = await bob.call('GET', '/api/stories');
  const bobSees = (storySeenByBob.data.groups || []).filter(g => g.user && g.user.id === alice.user.id)[0];
  ok('a watched story is no longer "unseen"', !!bobSees && bobSees.unseen === false, '');

  const foreignStoryDelete = await bob.call('POST', '/api/story/delete', { id: storyId });
  ok("nobody else deletes somebody's story", foreignStoryDelete.status === 403, String(foreignStoryDelete.status));
  const deletedStory = await alice.call('POST', '/api/story/delete', { id: storyId });
  ok('author deletes the story', deletedStory.status === 200, String(deletedStory.status));
  const goneStory = await bob.call('GET', '/api/story?id=' + storyId);
  ok('deleted story is gone', goneStory.status === 404, String(goneStory.status));

  // ---- one search over everything
  const word = 'zebra' + stamp;
  const sPost = await alice.call('POST', '/api/post', { body: 'a post about ' + word });
  const sComment = await bob.call('POST', '/api/comment', { post: sPost.data.post.id, body: 'comment with ' + word });
  const sMessage = await bob.call('POST', '/api/messages/send', { to: alice.user.id, body: 'secret ' + word });
  const sGroup = await alice.call('POST', '/api/group/create', {
    name: 'Club ' + word, description: 'about ' + word, visibility: 'public'
  });
  ok('search fixtures created', sPost.status === 200 && sComment.status === 200 &&
    sMessage.status === 200 && sGroup.status === 200,
    [sPost.status, sComment.status, sMessage.status, sGroup.status].join(','));

  const all = await alice.call('GET', '/api/search/all?q=' + word);
  ok('search finds everything at once', all.status === 200 &&
    all.data.counts.posts >= 1 && all.data.counts.comments >= 1 &&
    all.data.counts.messages >= 1 && all.data.counts.groups >= 1,
    JSON.stringify(all.data.counts));
  ok('search hit carries the comment context',
    (all.data.comments || []).some(c => c.post === sPost.data.post.id && c.postPreview), '');
  ok('message hit knows the other side',
    (all.data.messages || []).some(m => m.with && m.with.id === bob.user.id && m.outgoing === false), '');

  const byKind = await alice.call('GET', '/api/search/all?q=' + word + '&kind=comments');
  ok('kind filter returns only comments', byKind.status === 200 &&
    byKind.data.posts.length === 0 && byKind.data.comments.length >= 1, '');

  const peopleOnly = await bob.call('GET', '/api/search/all?q=' + word + '&kind=people');
  ok('kind=people skips posts', peopleOnly.data.posts.length === 0 && peopleOnly.data.users.length === 0, '');

  // bob's own search must not see alice's other conversations
  const bobMessages = await bob.call('GET', '/api/search/all?q=' + word + '&kind=messages');
  ok('messages are only your own', bobMessages.data.messages.length === 1 &&
    bobMessages.data.messages[0].body.indexOf(word) >= 0, JSON.stringify(bobMessages.data.messages.length));

  const stranger2 = await new Client('stranger2');
  await stranger2.register('st2_' + stamp, 'outsider-pass-1');
  const strangerMessages = await stranger2.call('GET', '/api/search/all?q=' + word + '&kind=messages');
  ok('a stranger cannot search somebody else\'s chat', strangerMessages.data.messages.length === 0, '');

  const noQuery = await alice.call('GET', '/api/search/all');
  ok('an empty search is refused', noQuery.status === 400, String(noQuery.status));

  const miss = await alice.call('GET', '/api/search/all?q=zzz-' + word + '-nope');
  ok('a miss returns empty sections', miss.status === 200 && miss.data.posts.length === 0 &&
    miss.data.comments.length === 0, '');

  const byDate = await alice.call('GET', '/api/search/all?q=' + word + '&since=2099-01-01');
  ok('date filter excludes the future', byDate.data.posts.length === 0, '');

  const byAuthor = await alice.call('GET', '/api/search/all?q=' + word + '&author=' + bob.user.id);
  ok('author filter works', byAuthor.data.comments.length >= 1 && byAuthor.data.posts.length === 0,
    JSON.stringify({ c: byAuthor.data.comments.length, p: byAuthor.data.posts.length }));

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

  // ---- editing the details of a track / clip
  const retitled = await alice.call('POST', '/api/media/edit', {
    id: upTrack.data.item.id, title: 'Renamed track', artist: 'Someone else', description: 'a note'
  });
  ok('track details can be edited', retitled.status === 200 &&
    retitled.data.item.title === 'Renamed track' && retitled.data.item.artist === 'Someone else' &&
    retitled.data.item.description === 'a note',
    JSON.stringify(retitled.data && retitled.data.item));
  const foreignEdit = await bob.call('POST', '/api/media/edit', {
    id: upTrack.data.item.id, title: 'Stolen'
  });
  ok("nobody else edits my track", foreignEdit.status === 403, String(foreignEdit.status));

  // the shared shelf must not be what "my library" shows
  const mineOnly = await alice.call('GET', '/api/media?kind=audio&user=' + alice.user.id);
  ok('asking for my library returns only mine', mineOnly.status === 200 &&
    mineOnly.data.items.every(i => i.owner === alice.user.id), '');
  const everyone = await alice.call('GET', '/api/media?kind=audio');
  ok('the shared shelf names the owner', everyone.status === 200 &&
    everyone.data.items.every(i => typeof i.ownerName === 'string'), '');
  const emptyTitle = await alice.call('POST', '/api/media/edit', { id: upTrack.data.item.id, title: '   ' });
  ok('a track needs a title', emptyTitle.status === 400, String(emptyTitle.status));
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

  // ---- profile rating: derived, never stored
  const rating = prof.data && prof.data.user.rating;
  ok('profile carries a rating', !!rating && typeof rating.percent === 'number' &&
    rating.percent >= 0 && rating.percent <= 100, JSON.stringify(rating));
  ok('rating counts achievements separately', !!rating && rating.badges ===
    badgeIds.filter(b => b.length > 0).length, JSON.stringify(rating));
  ok('the owner sees what to fill in', Array.isArray(rating && rating.missing),
    JSON.stringify(rating && rating.missing));

  // a bare profile scores low, and filling fields raises the number
  const bare = new Client('bare');
  await bare.register('bare_' + stamp, 'bare-password-1', 'Bare Profile');
  const bareProf = await bare.call('GET', '/api/profile?user=' + bare.user.id);
  const bareRating = bareProf.data && bareProf.data.user.rating;
  ok('a fresh profile scores low', bareRating && bareRating.percent <= 20,
    String(bareRating && bareRating.percent));
  ok('a fresh profile has no achievements yet', bareRating && bareRating.badges === 0,
    String(bareRating && bareRating.badges));
  await bare.call('POST', '/api/profile', {
    bio: 'a bio', city: 'Vilnius', country: 'Lithuania', birthday: '1990-01-01',
    gender: 'other', website: 'https://example.com'
  });
  const filledProf = await bare.call('GET', '/api/profile?user=' + bare.user.id);
  const filledRating = filledProf.data && filledProf.data.user.rating;
  ok('filling the profile raises the rating', filledRating && filledRating.percent > bareRating.percent,
    (bareRating && bareRating.percent) + ' -> ' + (filledRating && filledRating.percent));
  ok('a filled field leaves the missing list', filledRating &&
    Array.isArray(filledRating.missing) && filledRating.missing.indexOf('bio') < 0 &&
    filledRating.missing.indexOf('city') < 0, JSON.stringify(filledRating && filledRating.missing));

  // somebody else's rating is a number, but never a to-do list
  const seenByOther = await bob.call('GET', '/api/profile?user=' + bare.user.id);
  const otherRating = seenByOther.data && seenByOther.data.user.rating;
  ok('a stranger sees the percent only', !!otherRating && typeof otherRating.percent === 'number' &&
    otherRating.missing === undefined, JSON.stringify(otherRating));

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

    // ---- janitors: a moderator is no longer all-or-nothing
    const jan = new Client('janitor');
    await jan.register('jan_' + stamp, 'janitor-pass-1', 'Jan Itor');
    await jan.call('POST', '/api/messages/send', { to: alice.user.id, body: 'before the mute' });

    const promoted = await admin.call('POST', '/api/admin/user',
      { id: jan.user.id, action: 'role', role: 'moderator' });
    ok('moderator promoted', promoted.status === 200 && promoted.data.user.role === 'moderator',
      String(promoted.status));
    ok('a fresh moderator keeps every right', Array.isArray(promoted.data.user.perms) &&
      promoted.data.user.perms.length === 8, JSON.stringify(promoted.data.user.perms));

    // narrowing the set must actually take the other tools away
    const narrowed = await admin.call('POST', '/api/admin/user',
      { id: jan.user.id, action: 'perms', perms: ['reports'] });
    ok('rights can be narrowed', narrowed.status === 200 &&
      narrowed.data.user.perms.length === 1 && narrowed.data.user.perms[0] === 'reports',
      JSON.stringify(narrowed.data.user.perms));

    const janLogin = new Client('janitor2', adminBase);
    const janIn = await janLogin.login('jan_' + stamp, 'janitor-pass-1');
    ok('janitor can sign in to the admin board', janIn.status === 200, String(janIn.status));

    const janReports = await janLogin.call('GET', '/api/admin/reports');
    ok('janitor reads the report queue', janReports.status === 200, String(janReports.status));
    const janUsers = await janLogin.call('GET', '/api/admin/users');
    ok('janitor without "users" cannot list users', janUsers.status === 403, String(janUsers.status));
    const janSettings = await janLogin.call('GET', '/api/admin/settings');
    ok('janitor cannot read the settings', janSettings.status === 403, String(janSettings.status));
    const janAi = await janLogin.call('GET', '/api/admin/ai');
    ok('janitor cannot read the AI panel', janAi.status === 403, String(janAi.status));
    const janFeedback = await janLogin.call('GET', '/api/admin/feedback');
    ok('janitor without "feedback" cannot read feedback', janFeedback.status === 403, String(janFeedback.status));
    const janRole = await janLogin.call('POST', '/api/admin/user',
      { id: bob.user.id, action: 'role', role: 'admin' });
    ok('janitor cannot promote anybody', janRole.status === 403, String(janRole.status));
    const janBan = await janLogin.call('POST', '/api/admin/user',
      { id: bob.user.id, action: 'ban', days: 1 });
    ok('janitor cannot ban', janBan.status === 403, String(janBan.status));

    // the "posts" right is what unlocks deleting somebody else's post
    // (a fresh account, so the suite's post rate limit is not in the way)
    const victim = new Client('victim');
    await victim.register('vic_' + stamp, 'victim-pass-1', 'Victim');
    const victimPost = await victim.call('POST', '/api/post', { body: 'delete me if you dare' });
    const victimPostId = victimPost.data && victimPost.data.post ? victimPost.data.post.id : 0;
    ok('prepared a post for the janitor to delete', victimPostId > 0,
      String(victimPost.status) + ' ' + JSON.stringify(victimPost.data && victimPost.data.error));
    const janDeleteNo = await janLogin.call('POST', '/api/post/delete', { id: victimPostId });
    ok('janitor without "posts" cannot delete a post', janDeleteNo.status === 403, String(janDeleteNo.status));
    await admin.call('POST', '/api/admin/user', { id: jan.user.id, action: 'perms', perms: ['posts'] });
    const janDeleteYes = await janLogin.call('POST', '/api/post/delete', { id: victimPostId });
    ok('janitor with "posts" may delete a post', janDeleteYes.status === 200, String(janDeleteYes.status));

    // ---- chat mute
    const muteOn = await admin.call('POST', '/api/admin/mute',
      { id: jan.user.id, hours: 2, reason: 'spamming the suite' });
    ok('admin can mute in chat', muteOn.status === 200 && !!muteOn.data.user.mutedUntil,
      String(muteOn.status));
    const mutedSend = await jan.call('POST', '/api/messages/send',
      { to: alice.user.id, body: 'should not arrive' });
    ok('a muted account cannot send messages', mutedSend.status === 403 && mutedSend.data.error === 'muted',
      String(mutedSend.status));
    const mutedPost = await jan.call('POST', '/api/post', { body: 'a mute is chat only' });
    ok('a muted account may still post', mutedPost.status === 200, String(mutedPost.status));
    const muteSeen = await jan.call('GET', '/api/me');
    ok('the mute is visible to its victim', muteSeen.status === 200 && !!muteSeen.data.me.mutedUntil, '');
    const muteOff = await admin.call('POST', '/api/admin/mute', { id: jan.user.id, hours: 0 });
    ok('admin can lift a mute', muteOff.status === 200 && !muteOff.data.user.mutedUntil,
      String(muteOff.status));
    const afterMute = await jan.call('POST', '/api/messages/send',
      { to: alice.user.id, body: 'unmuted, back to normal' });
    ok('messages flow again after the mute', afterMute.status === 200, String(afterMute.status));

    // put the janitor back where we found them
    await admin.call('POST', '/api/admin/user', { id: jan.user.id, action: 'role', role: 'user' });
    const demotedUsers = await janLogin.call('GET', '/api/admin/users');
    ok('a demoted janitor loses the board', demotedUsers.status === 403, String(demotedUsers.status));
  }

  // ---- guest / kiosk (read-only browsing)
  const guest = new Client('guest');
  const gLogin = await guest.guest('');
  ok('guest session issued', gLogin.status === 200 && !!guest.key && gLogin.data.guest === true,
    String(gLogin.status));

  const gMe = await guest.call('GET', '/api/me');
  ok('guest /me reports a guest', gMe.status === 200 && gMe.data.me && gMe.data.me.guest === true, '');

  const gFeed = await guest.call('GET', '/api/feed');
  ok('guest feed falls back to global', gFeed.status === 200 && gFeed.data.tab === 'global',
    String(gFeed.status) + ' ' + (gFeed.data && gFeed.data.tab));

  const gPost = await guest.call('GET', '/api/post?id=' + postId);
  ok('guest may read a public post', gPost.status === 200 && gPost.data.post.id === postId,
    String(gPost.status));

  const gWrite = await guest.call('POST', '/api/post', { body: 'a guest walks into a bar' });
  ok('guest may not post', gWrite.status === 403 && gWrite.data.error === 'guest-readonly',
    String(gWrite.status));

  const gLike = await guest.call('POST', '/api/like', { id: postId });
  ok('guest may not like', gLike.status === 403, String(gLike.status));

  const gComment = await guest.call('POST', '/api/comment', { post: postId, body: 'hi' });
  ok('guest may not comment', gComment.status === 403, String(gComment.status));

  const gThreads = await guest.call('GET', '/api/messages/threads');
  ok('guest may not read messages', gThreads.status === 403, String(gThreads.status));

  const gSettings = await guest.call('POST', '/api/settings', { theme: 'dark' });
  ok('guest may not change settings', gSettings.status === 403, String(gSettings.status));

  const gUpload = await guest.call('POST', '/api/avatar', { data: PNG });
  ok('guest may not upload an avatar', gUpload.status === 403, String(gUpload.status));

  // a private profile stays private even for a guest
  const hermit = new Client('hermit');
  await hermit.register('hermit_' + stamp, 'hermit-pass-1');
  await hermit.call('POST', '/api/settings', { privacy: 'private' });
  const gPrivate = await guest.call('GET', '/api/profile?user=' + hermit.user.id);
  ok('guest cannot see a private profile', gPrivate.status === 403, String(gPrivate.status));

  // a stranger's wall is readable when it is public, and read-only
  const gWall = await guest.call('GET', '/api/wall?user=' + alice.user.id);
  ok('guest may read a public wall', gWall.status === 200 && gWall.data.canPost === false,
    String(gWall.status));

  const gCanMessage = gWall.data.user && gWall.data.user.canMessage;
  ok('guest is offered no message button', gCanMessage === false, String(gCanMessage));

  const hsGuest = await (await fetch(BASE + '/api/handshake')).json();
  ok('handshake advertises guest mode', hsGuest.site && hsGuest.site.guestMode === true &&
    hsGuest.site.guestPasswordRequired === false, '');

  // a guest session cannot be upgraded into a write by replaying a signed call
  const gReplay = await guest.call('POST', '/api/post', { body: 'still a guest' });
  ok('guest writes stay refused', gReplay.status === 403, String(gReplay.status));

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
