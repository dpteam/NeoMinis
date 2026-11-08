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

  const group = await alice.call('POST', '/api/group/create', { name: 'Test Group ' + stamp, description: 'x', visibility: 'public' });
  ok('group created', group.status === 200 && group.data.group.id > 0, String(group.status));
  const gid = group.data && group.data.group.id;
  const join = await bob.call('POST', '/api/group/join', { id: gid });
  ok('group joined', join.status === 200 && join.data.group.isMember === true, '');
  const gpost = await bob.call('POST', '/api/post', { body: 'group post', group: gid });
  ok('group post created', gpost.status === 200, String(gpost.status));
  const gfeed = await alice.call('GET', '/api/group/feed?id=' + gid);
  ok('group feed has post', gfeed.data && gfeed.data.posts.length === 1, '');

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
