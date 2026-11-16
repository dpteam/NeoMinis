// Builds standalone demo pages that use the real app.css, then screenshots them
// with headless Chrome into docs/img/. Nothing here touches the database: the
// people, posts and numbers are invented, so README screenshots never leak a real
// instance's content.
//
//   node tools/make-demo-screens.mjs            build pages + screenshots
//   node tools/make-demo-screens.mjs --keep     also keep the HTML in bin/demo
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';

const ROOT = process.cwd();
const OUT = path.join(ROOT, 'docs', 'img');
const KEEP = process.argv.includes('--keep');
const WORK = KEEP ? path.join(ROOT, 'bin', 'demo') : fs.mkdtempSync(path.join(os.tmpdir(), 'neominis-demo-'));

const CHROME = [
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe'
].find(p => fs.existsSync(p));

const sleep = ms => new Promise(r => setTimeout(r, ms));
const esc = s => String(s == null ? '' : s)
  .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
  .replace(/"/g, '&quot;');

const icon = name => `<i class="fa-solid fa-${name}"></i>`;

// ------------------------------------------------------------------ people

const USERS = {
  alex: { id: 1, name: 'Alex Morgan', handle: 'alex', color: 0, status: 'Building small things' },
  nina: { id: 2, name: 'Nina Petrova', handle: 'nina', color: 3, status: 'Sunsets and long walks' },
  tom: { id: 3, name: 'Tom Reed', handle: 'tom', color: 1, status: '' },
  mira: { id: 4, name: 'Mira Kaplan', handle: 'mira', color: 4, status: 'Designing in the dark' },
  dan: { id: 5, name: 'Dan Vega', handle: 'dan', color: 2, status: '' },
  yuki: { id: 6, name: 'Yuki Sato', handle: 'yuki', color: 5, status: '' }
};

function initials(name) {
  const parts = String(name || '?').trim().split(/\s+/);
  if (parts.length > 1 && parts[0] && parts[1]) return (parts[0][0] + parts[1][0]).toUpperCase();
  return String(name || '?').charAt(0).toUpperCase();
}

function avatar(user, size, rounded) {
  const px = size || 32;
  const cls = 'avatar a' + (user.color % 8) + (rounded === false ? ' sq' : ' rd');
  const style = `width:${px}px;height:${px}px;font-size:${Math.max(10, Math.round(px * 0.4))}px`;
  return `<span class="${cls}" style="${style}">${esc(initials(user.name))}</span>`;
}

// --------------------------------------------------------------- fake media

// A stand-in "photo": a gradient with a couple of shapes. It is a data URI, so
// the demo pages are self-contained and no real upload is ever shown.
function photo(w, h, from, to, accent) {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}">
  <defs><linearGradient id="g" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="${from}"/><stop offset="1" stop-color="${to}"/>
  </linearGradient></defs>
  <rect width="${w}" height="${h}" fill="url(#g)"/>
  <circle cx="${w * 0.72}" cy="${h * 0.3}" r="${h * 0.16}" fill="${accent}" opacity=".85"/>
  <path d="M0 ${h} L${w * 0.28} ${h * 0.52} L${w * 0.46} ${h * 0.74} L${w * 0.62} ${h * 0.58} L${w} ${h} Z"
        fill="${accent}" opacity=".35"/>
  <rect y="${h * 0.86}" width="${w}" height="${h * 0.14}" fill="#000" opacity=".18"/>
</svg>`;
  return 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svg);
}

// ------------------------------------------------------------------ pieces

function post(p) {
  const a = USERS[p.by];
  const menu = p.menu
    ? `<div class="post-menu">
        <button type="button" class="icon-btn">${icon('thumbtack')}</button>
        <button type="button" class="icon-btn">${icon('pen')}</button>
        <button type="button" class="icon-btn">${icon('trash')}</button>
      </div>`
    : '';
  const reactions = p.reactions
    ? `<div class="post-reactions">${p.reactions.map(r =>
        `<button type="button" class="reaction-chip${r.mine ? ' mine' : ''}"><span class="re-emoji">${r.emoji}</span><b>${r.count}</b></button>`).join('')}</div>`
    : '';
  const media = p.image
    ? `<div class="post-media"><img class="post-img" src="${p.image}" alt=""></div>`
    : '';
  const poll = p.poll
    ? `<div class="poll"><div class="poll-q">${icon('square-poll-vertical')} ${esc(p.poll.question)}</div>` +
      p.poll.options.map(o =>
        `<button type="button" class="poll-opt voted"><span class="poll-bar" style="width:${o.percent}%"></span>` +
        `<span class="poll-text">${esc(o.text)}</span><span class="poll-pct">${o.percent}%</span></button>`).join('') +
      `<div class="poll-foot">${icon('check')} Voted · ${p.poll.votes} votes · <span class="poll-hint">change</span></div></div>`
    : '';
  const repost = p.repost
    ? `<a class="repost-card" href="#">
        <div class="repost-head">${avatar(USERS[p.repost.by], 24)}<span class="name">${esc(USERS[p.repost.by].name)}</span><span class="when">${esc(p.repost.when)}</span></div>
        <div class="repost-body">${esc(p.repost.body)}</div>
        <div class="repost-foot">${icon('heart')} ${p.repost.likes} · ${icon('comment')} ${p.repost.comments} · ${icon('share')} ${p.repost.reposts}</div>
      </a>`
    : '';
  return `<article class="post">
    <div class="post-head">${avatar(a, 38)}
      <div class="post-who"><div class="meta">
        <a class="name" href="#">${esc(a.name)}</a><span>${esc(p.when)}</span>
        ${p.pinned ? `<span class="pin-mark">${icon('thumbtack')} Pinned</span>` : ''}
      </div></div>
      ${menu}
    </div>
    ${p.body ? `<div class="post-body">${p.body}</div>` : ''}
    ${media}${repost}${poll}${reactions}
    <div class="post-actions">
      <button type="button" class="pa${p.liked ? ' liked' : ''}">${icon('heart')} <span>${p.liked ? 'Liked' : 'Like'}</span> <b>${p.likes || 0}</b></button>
      <button type="button" class="pa">${icon('face-smile')} <span>React</span></button>
      <button type="button" class="pa">${icon('comment')} <span>Comment</span> <b>${p.comments || 0}</b></button>
      <button type="button" class="pa">${icon('share')} <span>Share</span> <b>${p.reposts || 0}</b></button>
      <button type="button" class="pa">${icon('bookmark')} <span>Save</span></button>
      <a class="pa" href="#">${icon('up-right-from-square')}</a>
    </div>
  </article>`;
}

function composer(me) {
  return `<form class="composer">
    <div class="composer-top">${avatar(me, 36)}
      <textarea placeholder="What's new?"></textarea>
    </div>
    <div class="composer-bar">
      <button type="button" class="btn btn-sm">${icon('image')} Photo</button>
      <button type="button" class="btn btn-sm">${icon('video')} Video</button>
      <button type="button" class="btn btn-sm">${icon('square-poll-vertical')} Poll</button>
      <button type="button" class="btn btn-sm">${icon('clock')} Schedule</button>
      <button type="button" class="btn btn-sm">${icon('file-pen')} Draft</button>
      <select class="select" style="width:auto;padding:4px 24px 4px 8px;font-size:11.5px">
        <option>Public</option><option>Friends only</option><option>Private</option>
      </select>
      <button type="submit" class="btn btn-primary btn-sm right">${icon('paper-plane')} Post</button>
    </div>
  </form>`;
}

function sideLink(active, key, iconName, label, count) {
  return `<a class="side-link${active === key ? ' active' : ''}" href="#">${icon(iconName)} ${esc(label)}` +
    (count ? `<span class="count">${count}</span>` : '') + `</a>`;
}

function shell(opts) {
  const me = opts.me || USERS.alex;
  const site = { name: 'NeoMinis', short: 'NM', tagline: 'Your own social network' };
  const top = `<div class="topbar"><div class="topbar-inner">
    <a class="brand" href="#">
      <span class="brand-mark">${site.short}</span>
      <span class="brand-text"><b>${site.name}</b><small>${site.tagline}</small></span>
    </a>
    <form class="topsearch" role="search">${icon('magnifying-glass')}
      <input type="search" placeholder="Search people and posts…" autocomplete="off"></form>
    <nav class="tabs">
      <a class="tab${opts.tab === 'feed' ? ' active' : ''}" href="#">${icon('house')} Feed</a>
      <a class="tab" href="#">${icon('id-card')} My page</a>
      <a class="tab" href="#">${icon('bell')} Notifications<span class="badge badge-notif">3</span></a>
      <a class="tab" href="#">${icon('gear')} Settings</a>
    </nav>
    <div class="top-right">
      <button type="button" class="icon-btn">${icon('circle-half-stroke')}</button>
      <a class="top-user" href="#">${avatar(me, 24)}<span class="name">${esc(me.name)}</span></a>
    </div>
  </div></div>`;

  const left = `<aside class="col-left">
    <div class="card"><div class="mini-profile">
      ${avatar(me, 64, false)}
      <a class="name" href="#">${esc(me.name)}</a>
      <div class="status">${esc(me.status || '@' + me.handle)}</div>
      <div class="mini-stats">
        <div><b>128</b><span>Friends</span></div>
        <div><b>46</b><span>Posts</span></div>
        <div><b>1.2K</b><span>Likes</span></div>
      </div>
    </div></div>
    <div class="card side-nav">
      ${sideLink(opts.side, 'feed', 'house', 'Feed')}
      ${sideLink(opts.side, 'friends', 'user-group', 'Friends', 2)}
      ${sideLink(opts.side, 'im', 'envelope', 'Messages', 3)}
      ${sideLink(opts.side, 'people', 'users', 'People')}
      ${sideLink(opts.side, 'groups', 'people-group', 'Groups')}
      ${sideLink(opts.side, 'notifications', 'bell', 'Notifications', 3)}
      ${sideLink(opts.side, 'bookmarks', 'bookmark', 'Bookmarks')}
      ${sideLink(opts.side, 'drafts', 'file-pen', 'Drafts')}
      ${sideLink(opts.side, 'music', 'music', 'Music')}
      ${sideLink(opts.side, 'video', 'film', 'Video')}
      ${sideLink(opts.side, 'calls', 'phone', 'Calls')}
      ${sideLink(opts.side, 'gifts', 'gift', 'Gifts')}
      ${sideLink(opts.side, 'apps', 'puzzle-piece', 'Apps')}
      <div class="side-sep"></div>
      ${sideLink(opts.side, 'profile', 'id-card', 'My page')}
      ${sideLink(opts.side, 'settings', 'gear', 'Settings')}
      <div class="side-sep"></div>
      <button type="button" class="side-link" style="width:100%;border:0;background:transparent;text-align:left">${icon('comment-dots')} Feedback</button>
      <button type="button" class="side-link" style="width:100%;border:0;background:transparent;text-align:left">${icon('right-from-bracket')} Log out</button>
    </div>
    <div class="card online-card"><div class="card-h">${icon('signal')} Now online</div>
      <div class="online-list">
        ${onlineUser(USERS.nina)}${onlineUser(USERS.tom)}
      </div>
    </div>
  </aside>`;

  const right = `<aside class="col-right">
    <div class="card"><div class="card-h">${icon('hashtag')} Tags</div>
      <div class="card-b"><div class="tag-cloud">
        <a class="tag-chip" href="#">#design <b>12</b></a>
        <a class="tag-chip" href="#">#music <b>9</b></a>
        <a class="tag-chip" href="#">#retro <b>7</b></a>
        <a class="tag-chip" href="#">#harbour <b>5</b></a>
        <a class="tag-chip" href="#">#weekend <b>4</b></a>
      </div></div></div>
    <div class="card"><div class="card-h">${icon('user-group')} Friends online</div>
      <div class="online-list">${onlineUser(USERS.nina)}${onlineUser(USERS.mira)}</div></div>
    <div class="card"><div class="card-h">${icon('chart-simple')} About this server</div>
      <div class="card-b">
        <div class="stat-grid" style="grid-template-columns:1fr 1fr">
          <div class="stat-card"><b>412</b><span>Users</span></div>
          <div class="stat-card"><b>7</b><span>Online</span></div>
          <div class="stat-card"><b>1.8K</b><span>Posts</span></div>
          <div class="stat-card"><b>24.6K</b><span>Visits</span></div>
        </div>
        <div style="display:flex;gap:2px;margin-top:10px">
          ${[14, 22, 18, 30, 26, 34, 28].map(h =>
            `<div style="flex:1;display:flex;align-items:flex-end;height:36px"><div style="width:100%;height:${h}px;background:var(--accent);opacity:.65;border-radius:3px 3px 0 0"></div></div>`).join('')}
        </div>
      </div></div>
    <div class="card"><div class="card-h">${icon('comments')} Now online</div>
      <div class="online-list">${onlineUser(USERS.nina)}${onlineUser(USERS.tom)}${onlineUser(USERS.mira)}</div></div>
    <div class="card"><div class="card-f" style="text-align:center;border-top:0">NeoMinis · self-hosted</div></div>
  </aside>`;

  return doc(opts.theme || 'dark', `<div id="app">${top}<div class="shell">${left}
    <main class="col-main">${opts.main}</main>
    ${right}</div></div>`, opts.skin, opts.fx);
}

function onlineUser(u) {
  return `<a class="online-user" href="#">${avatar(u, 24)}<span class="name">${esc(u.name)}</span><span class="presence"></span></a>`;
}

function doc(theme, body, skin, fx) {
  // an effect needs the overlay element, which the real SPA creates from ui.js;
  // the demo pages are static, so it is written here instead
  const layer = fx && fx !== 'none' ? '<div class="fx-layer" aria-hidden="true"></div>' : '';
  return `<!DOCTYPE html>
<html lang="en" data-theme="${theme}" data-skin="${skin || 'branded'}" data-fx="${fx || 'none'}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>NeoMinis</title>
<link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.5.2/css/all.min.css">
<!-- root-absolute on purpose: a page under /skins/ would otherwise resolve
     these against /skins/ and load nothing, which looks like a broken skin -->
<link rel="stylesheet" href="/app.css">
<link rel="stylesheet" href="/skins.css">
<link rel="stylesheet" href="/effects.css">
<style>body{margin:0}#app{min-height:100vh}</style>
</head>
<body>${body}${layer}</body></html>`;
}

// ------------------------------------------------------------------- pages

const feedMain = `
  <div class="card" style="padding:12px">${composer(USERS.alex)}</div>
  <div class="feed-tabs">
    <a class="tab2 active" href="#">My feed</a>
    <a class="tab2" href="#">Everything</a>
    <a class="tab2" href="#">Friends only</a>
  </div>
  ${post({
    by: 'nina', when: '12 min ago', pinned: true,
    body: 'Sunset over the harbour tonight. No filter needed. 🌇',
    image: photo(760, 420, '#2b4a6f', '#d98a5f', '#ffd9a8'),
    likes: 128, comments: 14, reposts: 3, liked: true,
    reactions: [{ emoji: '❤️', count: 24, mine: true }, { emoji: '🔥', count: 9 }, { emoji: '😍', count: 5 }]
  })}
  ${post({
    by: 'alex', when: '1 h ago', menu: true,
    body: 'We are picking the next feature. What would you actually use?',
    poll: {
      question: 'What should we build next?', votes: 96,
      options: [
        { text: 'Voice channels', percent: 44 },
        { text: 'Stickers in chats', percent: 33 },
        { text: 'A games hub', percent: 23 }
      ]
    },
    likes: 41, comments: 12, reposts: 2
  })}
  ${post({
    by: 'tom', when: '3 h ago',
    body: 'This one is worth reading twice.',
    repost: {
      by: 'mira', when: 'yesterday',
      body: 'Good design is mostly deciding what not to put on the screen.',
      likes: 210, comments: 18, reposts: 44
    },
    likes: 17, comments: 3, reposts: 1
  })}
  ${post({
    by: 'mira', when: '5 h ago',
    body: 'Spent the evening rewriting the colour tokens. Everything is a variable now, which means <b>#design</b> finally scales.',
    likes: 63, comments: 8, reposts: 4,
    reactions: [{ emoji: '👍', count: 11 }, { emoji: '💡', count: 6 }]
  })}`;

const profileMain = `
  <div class="card" style="overflow:visible">
    <div class="profile-cover has-image" style="background-image:url('${photo(1000, 260, '#1d3557', '#457b9d', '#a8dadc')}')"></div>
    <div class="profile-head" style="padding:0 16px 14px">
      ${avatar(USERS.nina, 96, false)}
      <div class="profile-head-main">
        <h1>Nina Petrova <span class="badge vip">${icon('crown')} VIP</span></h1>
        <div class="username">@nina · Online</div>
        <div class="status">Sunsets and long walks</div>
      </div>
      <div class="profile-actions">
        <button type="button" class="btn btn-primary btn-sm">${icon('envelope')} Message</button>
        <button type="button" class="btn btn-sm">${icon('phone')} Call</button>
        <button type="button" class="btn btn-sm">${icon('user-check')} Friends</button>
      </div>
    </div>
    <div class="rating-strip full" style="margin:0 16px 16px">
      <div class="rating-bar"><i style="width:100%"></i></div>
      <div class="rating-text">${icon('circle-check')} Profile 100% filled <span class="tiny muted">· 14 achievements</span></div>
    </div>
  </div>
  <div class="profile-grid">
    <div>
      <div class="card"><div class="card-h">${icon('circle-info')} About</div>
        <div class="card-b">
          <div class="stat-grid" style="margin-bottom:10px">
            <div class="stat-card"><b>128</b><span>Friends</span></div>
            <div class="stat-card"><b>46</b><span>Posts</span></div>
            <div class="stat-card"><b>1.2K</b><span>Likes</span></div>
          </div>
          <table class="info-table">
            <tr><td>City</td><td>Lisbon</td></tr>
            <tr><td>Website</td><td><a href="#">nina.example</a></td></tr>
            <tr><td>Favourite music</td><td>Ambient, jazz, anything with a Rhodes</td></tr>
            <tr><td>Joined</td><td>Mar 2, 2024</td></tr>
          </table>
          <div style="margin-top:10px;font-size:12.5px;white-space:pre-wrap">Photographer. I collect harbours and old cameras.</div>
        </div></div>
      <div class="card"><div class="card-h">${icon('images')} Photos <span class="right"><a href="#">All</a></span></div>
        <div class="card-b"><div class="photo-grid">
          ${['#2b4a6f|#d98a5f|#ffd9a8', '#35597f|#8fbcbb|#e5e9f0', '#4a3f6b|#c39a6b|#f0d9b5', '#1d3557|#457b9d|#a8dadc', '#5b3a3a|#c77f7f|#ffd9d9', '#2f4858|#86b3a8|#d9f0e3']
            .map(c => { const [a, b, d] = c.split('|'); return `<button type="button" class="photo-cell"><img src="${photo(300, 300, a, b, d)}" alt=""></button>`; }).join('')}
        </div></div></div>
    </div>
    <div>
      ${post({
        by: 'nina', when: '2 h ago',
        body: 'Two hours of walking, one photo. That is the whole trick.',
        image: photo(700, 400, '#274b6d', '#e0955f', '#ffe0b2'),
        likes: 214, comments: 22, reposts: 6, liked: true,
        reactions: [{ emoji: '❤️', count: 41, mine: true }, { emoji: '🌅', count: 12 }]
      })}
    </div>
  </div>`;

const messagesMain = `
  <div class="card"><div class="card-h">${icon('envelope')} Messages
    <span class="right"><a href="#">${icon('user-plus')} New message</a></span></div>
    <div class="msg-layout">
      <div class="threads">
        <form class="thread-search">${icon('magnifying-glass')}<input type="search" placeholder="Search messages"></form>
        <div>
          <div class="thread active">${avatar(USERS.nina, 34)}
            <div class="thread-main"><div class="name">Nina Petrova</div><div class="last">See you at seven then 🌇</div></div>
            <div class="thread-side"><span class="thread-time">2 min</span><span class="unread">2</span></div></div>
          <div class="thread">${avatar(USERS.tom, 34)}
            <div class="thread-main"><div class="name">Tom Reed</div><div class="last">${icon('image')} Photo</div></div>
            <div class="thread-side"><span class="thread-time">28 min</span></div></div>
          <div class="thread">${avatar(USERS.mira, 34)}
            <div class="thread-main"><div class="name">Mira Kaplan</div><div class="last">The tokens are in, take a look</div></div>
            <div class="thread-side"><span class="thread-time">1 h</span></div></div>
          <div class="thread">${avatar(USERS.dan, 34)}
            <div class="thread-main"><div class="name">Dan Vega</div><div class="last">Typing…</div></div>
            <div class="thread-side"><span class="thread-time">3 h</span></div></div>
        </div>
      </div>
      <div class="chat">
        <div class="chat-head">${avatar(USERS.nina, 32)}
          <div><b>Nina Petrova</b><div class="tiny muted">online</div></div></div>
        <div class="chat-body">
          <div class="bubble-row">${avatar(USERS.nina, 26)}
            <div class="bubble"><div class="msg-text">Did you see the harbour photo?</div>
              <div class="msg-foot"><span class="time">18:41</span></div></div></div>
          <div class="bubble-row mine">${avatar(USERS.alex, 26)}
            <div class="bubble"><div class="msg-text">Just liked it. That light is unreal.</div>
              <div class="msg-foot"><span class="time">18:42</span><span class="tick read">${icon('check-double')}</span></div></div></div>
          <div class="bubble-row">${avatar(USERS.nina, 26)}
            <div class="bubble">
              <img class="msg-img" src="${photo(320, 200, '#2b4a6f', '#d98a5f', '#ffd9a8')}" alt="">
              <div class="msg-foot"><span class="time">18:44</span></div></div></div>
          <div class="bubble-row mine">${avatar(USERS.alex, 26)}
            <div class="bubble">
              <div class="msg-quote"><b>Nina</b><span>Did you see the harbour photo?</span></div>
              <div class="msg-text">Saved it. Wallpaper material.</div>
              <div class="msg-foot"><span class="time">18:45</span><span class="tick read">${icon('check-double')}</span></div></div></div>
          <div class="bubble-row">${avatar(USERS.nina, 26)}
            <div class="bubble"><div class="msg-text">See you at seven then 🌇</div>
              <div class="msg-foot"><span class="time">18:47</span></div></div></div>
        </div>
        <form class="chat-form">
          <div class="chat-row">
            <button type="button" class="chat-btn">${icon('microphone')}</button>
            <button type="button" class="chat-btn">${icon('image')}</button>
            <button type="button" class="chat-btn">${icon('paperclip')}</button>
            <button type="button" class="chat-btn">${icon('face-smile')}</button>
            <textarea rows="1" placeholder="Write a message…"></textarea>
            <button class="chat-send" type="submit">${icon('paper-plane')}</button>
          </div>
        </form>
      </div>
    </div>
  </div>`;

const appsMain = `
  <div class="feed-tabs hub-tabs">
    <a class="tab2" href="#">${icon('gamepad')} Games</a>
    <a class="tab2 active" href="#">${icon('puzzle-piece')} Apps</a>
    <a class="tab2" href="#">${icon('key')} My apps</a>
  </div>
  <div class="card"><div class="card-h">${icon('puzzle-piece')} Apps</div>
    <div class="card-b">
      <div class="app-grid">
        <div class="app-card">
          <div class="app-head">${icon('globe')} <b>Harbour Weather</b></div>
          <div class="app-desc tiny muted">Tide and wind for the bay, updated hourly.</div>
          <div class="app-meta tiny muted">by Nina Petrova · 214 installs</div>
          <div class="app-actions"><span class="badge ok">${icon('check')} Allowed</span>
            <button type="button" class="btn btn-xs btn-danger">Revoke</button>
            <a class="btn btn-xs" href="#">Open</a></div>
        </div>
        <div class="app-card">
          <div class="app-head">${icon('gamepad')} <b>Pixel Racer</b></div>
          <div class="app-desc tiny muted">A tiny racing game with a leaderboard.</div>
          <div class="app-meta tiny muted">by Tom Reed · 507 installs</div>
          <div class="app-actions"><button type="button" class="btn btn-xs btn-primary">Install</button></div>
        </div>
        <div class="app-card">
          <div class="app-head">${icon('list-check')} <b>Habit Log</b></div>
          <div class="app-desc tiny muted">One tap a day, a chart at the end of the month.</div>
          <div class="app-meta tiny muted">by Mira Kaplan · 96 installs</div>
          <div class="app-actions"><button type="button" class="btn btn-xs btn-primary">Install</button></div>
        </div>
        <div class="app-card">
          <div class="app-head">${icon('music')} <b>Setlist</b></div>
          <div class="app-desc tiny muted">Shared playlists for a night out.</div>
          <div class="app-meta tiny muted">by Dan Vega · 61 installs</div>
          <div class="app-actions"><span class="badge ok">${icon('check')} Allowed</span>
            <button type="button" class="btn btn-xs btn-danger">Revoke</button>
            <a class="btn btn-xs" href="#">Open</a></div>
        </div>
      </div>
    </div></div>
  <div class="card"><div class="card-h">${icon('key')} What this account allowed</div>
    <div class="card-b">
      <div class="online-user">${icon('user')} <span class="name">Harbour Weather</span>
        <span class="tiny muted">profile, storage</span></div>
      <div class="online-user">${icon('user')} <span class="name">Setlist</span>
        <span class="tiny muted">profile</span></div>
    </div></div>`;

const callsMain = `
  <div class="chan-layout">
    <aside class="chan-side">
      <div class="chan-side-head"><span>${icon('hashtag')} Channels</span>
        <button type="button" class="icon-btn">${icon('plus')}</button></div>
      <div class="chan-list">
        <div class="chan-row busy">
          <button type="button" class="chan-join">${icon('volume-high')}<span class="chan-name">general</span><span class="chan-count">3</span></button>
          <div class="chan-avatars">
            <span>${avatar(USERS.nina, 20)}</span><span>${avatar(USERS.tom, 20)}</span><span>${avatar(USERS.mira, 20)}</span>
          </div>
          <div class="chan-actions"><button type="button" class="icon-btn">${icon('video')}</button></div>
        </div>
        <div class="chan-row busy">
          <button type="button" class="chan-join">${icon('volume-high')}<span class="chan-name">music</span><span class="chan-count">2</span></button>
          <div class="chan-avatars"><span>${avatar(USERS.dan, 20)}</span><span>${avatar(USERS.yuki, 20)}</span></div>
          <div class="chan-actions"><button type="button" class="icon-btn">${icon('video')}</button></div>
        </div>
        <div class="chan-row">
          <button type="button" class="chan-join">${icon('hashtag')}<span class="chan-name">gaming</span></button>
          <div class="chan-actions"><button type="button" class="icon-btn">${icon('video')}</button></div>
        </div>
        <div class="chan-row">
          <button type="button" class="chan-join">${icon('hashtag')}<span class="chan-name">study-room</span></button>
          <div class="chan-actions"><button type="button" class="icon-btn">${icon('video')}</button></div>
        </div>
        <div class="chan-row">
          <button type="button" class="chan-join">${icon('hashtag')}<span class="chan-name">late-night</span></button>
          <div class="chan-actions"><button type="button" class="icon-btn">${icon('video')}</button></div>
        </div>
      </div>
      <div class="chan-side-foot tiny muted">Click a channel to join the voice room.</div>
    </aside>
    <section class="chan-main">
      <div class="card"><div class="card-h">${icon('phone')} Calls</div>
        <div class="card-b">
          <div class="empty">${icon('hashtag')}<b>Pick a channel</b>
            <span>Channels are always-on rooms. Join with one click, no ringing.</span></div>
        </div></div>
      <div class="card"><div class="card-h">${icon('clock-rotate-left')} History</div>
        <div class="card-b flush">
          <div class="online-user">${icon('phone')} <span class="name">#general · 3 people</span>
            <span class="tiny muted">42:18</span></div>
          <div class="online-user">${icon('video')} <span class="name">Nina Petrova</span>
            <span class="tiny muted">6:02</span></div>
          <div class="online-user">${icon('phone')} <span class="name">#music</span>
            <span class="tiny muted">18:44</span></div>
        </div></div>
    </section>
  </div>`;

const loginPage = () => doc('dark', `<div id="app"><div class="landing">
  <div class="landing-hero">
    <div class="landing-brand"><span class="brand-mark">NM</span><b>NeoMinis</b></div>
    <h1>Your own social network</h1>
    <p class="lead">A small, fast, self-hosted network: a feed, messages, calls, groups and games in one place.</p>
    <div class="landing-features">
      <div class="landing-feature">${icon('comments')}<div><b>Feed</b><span>Posts, polls, photos and reactions</span></div></div>
      <div class="landing-feature">${icon('user-group')}<div><b>Friends</b><span>Requests, walls, per-area privacy</span></div></div>
      <div class="landing-feature">${icon('envelope')}<div><b>Messages</b><span>Live delivery, files, voice notes</span></div></div>
      <div class="landing-feature">${icon('moon')}<div><b>Theme</b><span>Light / dark / oled</span></div></div>
    </div>
  </div>
  <div>
    <div class="auth-card">
      <div class="auth-head"><h2>Sign in</h2><p>Welcome back.</p></div>
      <div class="auth-tabs">
        <button type="button" class="auth-tab active">Sign in</button>
        <button type="button" class="auth-tab">Register</button>
        <button type="button" class="auth-tab">Recover</button>
      </div>
      <div class="auth-body">
        <div class="field"><label>Username</label><input class="input" value="alex"></div>
        <div class="field"><label>Password</label><input class="input" type="password" value="password"></div>
        <button type="button" class="btn btn-primary btn-block">Sign in</button>
      </div>
      <div class="auth-foot">NeoMinis · self-hosted</div>
    </div>
  </div>
</div></div>`).replace('body{margin:0}#app{min-height:100vh}',
  'body{margin:0}#app{min-height:100vh;display:flex;flex-direction:column;justify-content:center}');

// ------------------------------------------------------------------- build

const pages = {
  'screenshot-feed.png': shell({ theme: 'dark', tab: 'feed', side: 'feed', main: feedMain }),
  'screenshot-feed-light.png': shell({ theme: 'light', tab: 'feed', side: 'feed', main: feedMain }),
  'screenshot-profile.png': shell({ theme: 'dark', tab: 'profile', side: 'profile', main: profileMain, me: USERS.alex }),
  'screenshot-messages.png': shell({ theme: 'dark', tab: 'feed', side: 'im', main: messagesMain }),
  'screenshot-apps.png': shell({ theme: 'dark', tab: 'feed', side: 'apps', main: appsMain }),
  'screenshot-calls.png': shell({ theme: 'dark', tab: 'feed', side: 'calls', main: callsMain }),
  'screenshot-login.png': loginPage()
};

// ------------------------------------------------------------- skin gallery
// One feed page per template. `default` is dark like the README hero; the rest
// are rendered in whichever theme they were designed for, so each one shows its
// own palette instead of fighting the theme it was dropped into.
const SKIN_THEMES = {
  branded: 'dark', aero: 'light', metro: 'light', paper: 'light', terminal: 'dark',
  soft: 'light', brutal: 'light', neon: 'dark', warm: 'light', slate: 'light',
  matrix: 'dark', cyberpunk: 'dark', newyear: 'dark', autumn: 'light', horror: 'dark'
};
const SKIN_LABELS = {
  branded: 'Branded', aero: 'Aero', metro: 'Metro', paper: 'Paper', terminal: 'Terminal',
  soft: 'Soft', brutal: 'Brutal', neon: 'Neon', warm: 'Warm', slate: 'Slate',
  matrix: 'Matrix', cyberpunk: 'Cyberpunk', newyear: 'New Year', autumn: 'Autumn', horror: 'Horror'
};
// the effect each template ships with, so the screenshots show the real pairing
const SKIN_FX = {
  branded: 'none', aero: 'sheen', metro: 'none', paper: 'grain',
  terminal: 'scanlines', soft: 'aurora', brutal: 'paper-cut', neon: 'glow',
  warm: 'grain', slate: 'none', matrix: 'matrix', cyberpunk: 'glitch',
  newyear: 'snow', autumn: 'leaves', horror: 'vhs'
};

for (const [id, theme] of Object.entries(SKIN_THEMES)) {
  pages[`skins/${id}.png`] = shell({
    theme, skin: id, fx: SKIN_FX[id], tab: 'feed', side: 'feed', main: feedMain
  });
}

fs.mkdirSync(WORK, { recursive: true });
fs.mkdirSync(OUT, { recursive: true });
for (const [name, html] of Object.entries(pages)) {
  const target = path.join(WORK, name.replace(/\.png$/, '.html'));
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, html, 'utf8');
}
console.log('pages written to', WORK);

// a tiny static server: /app.css and /skins.css from the repo, the rest from WORK
const server = http.createServer((req, res) => {
  const url = req.url.split('?')[0];
  if (url === '/app.css' || url === '/skins.css' || url === '/effects.css') {
    res.writeHead(200, { 'content-type': 'text/css; charset=utf-8' });
    res.end(fs.readFileSync(path.join(ROOT, 'public', 'assets', 'css', url.slice(1))));
    return;
  }
  // a stylesheet request that falls through here means a 404 and an unstyled page,
  // which is easy to mistake for a broken skin: fail loudly instead
  if (/\.css$/.test(url)) { res.writeHead(404); res.end('missing stylesheet ' + url); return; }
  // the contact sheet lives in WORK but shows PNGs that were already written to docs
  if (url.indexOf('/shot/') === 0) {
    const png = path.join(OUT, url.slice('/shot/'.length));
    if (!png.startsWith(OUT) || !fs.existsSync(png)) { res.writeHead(404); res.end('no shot'); return; }
    res.writeHead(200, { 'content-type': 'image/png' });
    res.end(fs.readFileSync(png));
    return;
  }
  const file = path.join(WORK, url === '/' ? 'screenshot-feed.html' : url.replace(/^\//, ''));
  if (!file.startsWith(WORK) || !fs.existsSync(file)) { res.writeHead(404); res.end('nope'); return; }
  res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
  res.end(fs.readFileSync(file));
});
await new Promise(r => server.listen(0, '127.0.0.1', r));
const port = server.address().port;

// ------------------------------------------------------------------- shots

const PORT = 9344;
const profile = path.join(os.tmpdir(), 'neominis-shots-' + Date.now());
const proc = spawn(CHROME, [
  '--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run',
  '--no-default-browser-check', '--remote-debugging-port=' + PORT,
  '--user-data-dir=' + profile, '--window-size=1440,960', 'about:blank'
], { stdio: 'ignore' });

async function firstPage() {
  for (let i = 0; i < 80; i++) {
    try {
      const list = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json();
      const page = list.find(t => t.type === 'page');
      if (page) return page;
    } catch (e) { }
    await sleep(250);
  }
  throw new Error('devtools did not come up');
}

const target = await firstPage();
const ws = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
let seq = 0;
const waiting = new Map();
ws.onmessage = ev => {
  const m = JSON.parse(ev.data);
  if (m.id && waiting.has(m.id)) {
    const { res, rej } = waiting.get(m.id);
    waiting.delete(m.id);
    m.error ? rej(new Error(JSON.stringify(m.error))) : res(m.result);
  }
};
const send = (method, params) => new Promise((res, rej) => {
  const id = ++seq;
  waiting.set(id, { res, rej });
  ws.send(JSON.stringify({ id, method, params: params || {} }));
});

await send('Page.enable');
await send('Emulation.setDeviceMetricsOverride',
  { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false });

for (const [name, html] of Object.entries(pages)) {
  const file = name.replace(/\.png$/, '.html');
  await send('Page.navigate', { url: `http://127.0.0.1:${port}/${file}` });
  // fonts and icons come from a CDN, so give them a moment
  await sleep(1800);
  const shot = await send('Page.captureScreenshot', { format: 'png' });
  const target = path.join(OUT, name);
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, Buffer.from(shot.data, 'base64'));
  console.log(name, Math.round(fs.statSync(target).size / 1024) + ' KB');
}

// ---- one contact sheet so all ten can be compared in a single look ----
const sheet = `<!DOCTYPE html><html><head><meta charset="utf-8"><style>
  body { margin: 0; background: #0b0f16; font: 13px "Segoe UI", Tahoma, sans-serif; padding: 18px; }
  .grid { display: grid; grid-template-columns: repeat(5, 1fr); gap: 12px; }
  .cell { background: #141a24; border: 1px solid #26313f; border-radius: 10px; overflow: hidden; }
  .cell img { width: 100%; display: block; }
  .cell b { display: block; padding: 7px 9px; color: #dfe8f4; font-size: 12.5px; letter-spacing: .2px; }
  .cell span { display: block; padding: 0 9px 8px; color: #7f8fa4; font-size: 10.5px; text-transform: uppercase; letter-spacing: .6px; }
</style></head><body><div class="grid">
${Object.keys(SKIN_THEMES).map(id => `<div class="cell">
  <img src="/shot/skins/${id}.png" alt="">
  <b>${SKIN_LABELS[id]}</b><span>${id}</span></div>`).join('')}
</div></body></html>`;
fs.writeFileSync(path.join(WORK, 'contact.html'), sheet, 'utf8');

await send('Emulation.setDeviceMetricsOverride',
  { width: 1900, height: 1180, deviceScaleFactor: 1, mobile: false });
await send('Page.navigate', { url: `http://127.0.0.1:${port}/contact.html` });
await sleep(2200);
const contact = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true });
fs.writeFileSync(path.join(OUT, 'skins', 'contact.png'), Buffer.from(contact.data, 'base64'));
console.log('skins/contact.png', Math.round(fs.statSync(path.join(OUT, 'skins', 'contact.png')).size / 1024) + ' KB');

try { ws.close(); } catch (e) { }
try { proc.kill(); } catch (e) { }
server.close();
if (!KEEP) { try { fs.rmSync(WORK, { recursive: true, force: true }); } catch (e) { } }
console.log('done');
