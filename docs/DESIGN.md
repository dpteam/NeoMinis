# DESIGN.md — the visual system of NeoMinis

This file is the **source of truth for how the interface looks**, and the backup of
the design as it stands today. If `app.css` ever gets lost, rewritten or restyled,
everything needed to rebuild it faithfully is here: every colour, every size, every
shadow, every animation, and the reasoning behind the ones that are not obvious.

It is written for whoever touches the CSS next — human or agent. Read the intent
section first; the tables after it are only useful once the intent is clear.

| | |
| --- | --- |
| **Files** | `public/assets/css/app.css` (~2.1k lines), `skins.css` (the template layer), `effects.css` (the atmosphere layer), `public/assets/js/ui.js` (render helpers), `Core/Branding.cs` (generated palette) |
| **Themes** | `light` (default), `dark`, `oled` — all three are first-class |
| **Templates** | 15 skins on `data-skin`: `branded`, `aero`, `metro`, `paper`, `terminal`, `soft`, `brutal`, `neon`, `warm`, `slate`, `matrix`, `cyberpunk`, `newyear`, `autumn`, `horror` (see §12.1) |
| **Effects** | 12 atmosphere layers on `data-fx` — grain, scanlines, VHS, matrix rain, glitch, snow, leaves, aurora, glow, sheen, print offset (see §12.2) |
| **Font sets** | 7 typography sets on `data-font` — system, serif, mono, humanist, compact, classic, elegant. Only fonts that ship with Windows, so nothing is downloaded and nothing shifts (see §12.5) |
| **Sound sets** | 6 standalone sound sets plus "follow template" — classic, arcade, acoustic, minimal, chiptune, deep. Independent of the look (see §12.3) |
| **Icons** | Font Awesome 6.5.2 from a CDN, `<i class="fa-solid fa-name">` |
| **Fonts** | system stack only — no webfont, no network dependency, no FOUT |
| **Target** | Windows XP SP3 and up, so: ES5, no CSS grid subgrid, no `:has()`, no container queries |
| **Screens** | desktop 1200px+ down to a 320px phone, and phone landscape |

---

## 1. Intent

The visual language is **Facebook/VK ca. 2008 chrome rebuilt with modern spacing**:
blue gradient bars, small type, squared blocks, a 1px border on everything — but
with the generous whitespace, soft shadows and real easing that 2008 did not have.

The reference point is not "retro for the joke". It is the era when a social network
looked like a *dense information tool*: three columns, a compact feed, a sidebar you
could scan in one glance, controls that looked like controls. Modern sites traded
that density for full-bleed emptiness. We keep the density and modernise the craft.

Three rules follow from that and they explain most of the CSS:

1. **Chrome is a surface, not a decoration.** Bars and card headers are gradients,
   because that is how 2008 told you "this is a strip of UI, not content". The
   gradient is always low-contrast: two stops, ~6% apart.
2. **A border beats a shadow.** Every card has `1px solid var(--border)`. Shadows
   are a faint second signal, never the primary one. This is why the interface reads
   crisp at any zoom instead of turning into floating frosted blobs.
3. **Density is the feature.** 12–13px text, 7–9px vertical padding, 10–12px card
   padding. Nothing is 24px tall unless it is a primary action.

What we deliberately do **not** have: glassmorphism, gradient text, giant hero
typography, floating orbs, scroll-jacking, parallax on the feed, 999px pills
everywhere, or dark-mode-only styling. See §13.

---

## 2. Colour

Everything is a CSS custom property on `html[data-theme="…"]`. No component
hardcodes a hex value except where noted (white on gradients, status bevels).

### 2.1 Global, theme-independent

```css
--font: "Segoe UI", Tahoma, Verdana, Arial, sans-serif;
--font-display: "Segoe UI Semibold", "Segoe UI", Tahoma, sans-serif;
--font-mono: Consolas, "Lucida Console", "Courier New", monospace;
--radius: 10px;      /* cards, modals, big panels */
--radius-sm: 7px;    /* buttons, inputs, small panels */
--radius-xs: 4px;    /* tiny chips */
--ease: cubic-bezier(.22, .7, .28, 1);   /* the one easing curve */
--top-h: 48px;       /* topbar height; sticky offsets derive from it */
--blue-1: #6f9bc9;   /* brand gradient: light stop */
--blue-2: #4a76a8;   /* brand gradient: mid */
--blue-3: #3a5f8c;   /* brand gradient: dark */
```

`--ease` is used by **every** transition in the file. If a new animation needs a
different curve, that is a decision, not a detail — justify it in a comment.

### 2.2 Light theme

| Token | Value | Role |
| --- | --- | --- |
| `--bg` | `#e9edf3` | Page background — a cool grey, never white, so white cards lift off it |
| `--bg-glow` | `radial-gradient(1100px 520px at 50% -10%, #fbfdff, transparent 72%)` | A single soft light from above the fold; the only gradient on the page itself |
| `--panel` | `#ffffff` | Cards, modals, inputs |
| `--panel-2` | `#f4f7fb` | Card footers, action bars, thread list, table headers |
| `--panel-3` | `#edf2f8` | Nested wells (inset blocks inside a card) |
| `--head` | `linear-gradient(#fbfcfe, #eef3f9)` | Card header strip |
| `--head-strong` | `linear-gradient(#6f9bc9, #4a76a8 55%, #3f6a99)` | The "blue bar" — profile head, active section titles |
| `--border` | `#d7e0ea` | Every 1px outline |
| `--border-2` | `#c3d0df` | Hover / stronger divider |
| `--text` | `#223044` | Body and headings |
| `--text-soft` | `#3c4d63` | Secondary body, nav labels |
| `--muted` | `#6d7e93` | Metadata, timestamps, placeholders |
| `--link` | `#35679e` | Links |
| `--link-hi` | `#2b5a8f` | Link hover |
| `--accent` | `#4472a4` | Primary action, active nav, focus ring |
| `--accent-hi` | `#5b8dc4` | Gradient top stop |
| `--accent-lo` | `#345d8a` | Gradient bottom stop, button border |
| `--accent-soft` | `rgba(68,114,164,.11)` | Active row fill, focus halo |
| `--ok` | `#2f8f57` | Success, online dot, speaking ring |
| `--warn` | `#b0741a` | Warning text |
| `--err` | `#b93a2c` | Errors, unread badge, hang up |
| `--info` | `#3d7fb8` | Informational toast |
| `--gold` | `#c99b1f` | VIP, achievements |
| `--shadow` | `0 1px 1px rgba(23,43,77,.05), 0 4px 14px rgba(23,43,77,.07)` | Card elevation |
| `--shadow-hi` | `0 14px 38px rgba(23,43,77,.17)` | Modals, hovered composer, boot logo |
| `--inset` | `inset 0 1px 0 rgba(255,255,255,.85)` | The 2008 bevel highlight, on light buttons |
| `--topbar` | `linear-gradient(#6f9bc9, #4a76a8 55%, #3a5f8c)` | The blue bar |
| `--topbar-text` | `#ffffff` | Text on it |
| `--log-bg` | `#fbfcfe` | Admin log viewer |
| `--scrim` | `rgba(18,30,46,.45)` | Modal backdrop |
| `--field` | `#ffffff` | Input fill |
| `--field-border` | `#c9d5e3` | Input border (a touch darker than `--border`) |
| `--hover` | `#f1f5fa` | Row / button hover |
| `--bubble-in` | `#f0f4f9` | Incoming message bubble |
| `--bubble-out` | `#dbe9f8` | Outgoing message bubble |
| `--cover` | `linear-gradient(120deg, #5d88bb, #41699a 45%, #35597f)` | Profile cover fallback |

### 2.3 Dark theme

Same roles, different values. Note the accent **lifts** (`#4f80b8`) because a
mid-blue that reads as "confident" on white reads as "muddy" on `#12161d`.

| Token | Value |
| --- | --- |
| `--bg` | `#12161d` |
| `--bg-glow` | `radial-gradient(1100px 520px at 50% -10%, #1d2734, transparent 72%)` |
| `--panel` | `#1a212b` |
| `--panel-2` | `#1f2833` |
| `--panel-3` | `#151b23` |
| `--head` | `linear-gradient(#232d3a, #1c242e)` |
| `--head-strong` | `linear-gradient(#33506f, #263c54 55%, #1f3145)` |
| `--border` | `#2b3644` |
| `--border-2` | `#3a4759` |
| `--text` | `#dbe5f0` |
| `--text-soft` | `#c0cddd` |
| `--muted` | `#8ea0b6` |
| `--link` | `#7db0e4` |
| `--link-hi` | `#9cc7f2` |
| `--accent` | `#4f80b8` |
| `--accent-hi` | `#6b9fd4` |
| `--accent-lo` | `#2f5378` |
| `--accent-soft` | `rgba(107,159,212,.14)` |
| `--ok` | `#51b579` |
| `--warn` | `#d3a148` |
| `--err` | `#e0695b` |
| `--info` | `#5fa8e0` |
| `--gold` | `#e0b53f` |
| `--shadow` | `0 1px 2px rgba(0,0,0,.35), 0 6px 20px rgba(0,0,0,.28)` |
| `--shadow-hi` | `0 16px 42px rgba(0,0,0,.52)` |
| `--inset` | `inset 0 1px 0 rgba(255,255,255,.04)` |
| `--topbar` | `linear-gradient(#243a52, #1b2c3f 60%, #162534)` |
| `--topbar-text` | `#e8f0f9` |
| `--log-bg` | `#10151b` |
| `--scrim` | `rgba(0,0,0,.62)` |
| `--field` | `#151c25` |
| `--field-border` | `#33404f` |
| `--hover` | `#212b37` |
| `--bubble-in` | `#202a36` |
| `--bubble-out` | `#263c54` |
| `--cover` | `linear-gradient(120deg, #243a52, #1b2c3f 55%, #162534)` |

### 2.4 OLED theme

Pure black, no glow, and the accent goes **brighter** (`#4f8fd0`) — on a true-black
panel a mid-tone accent looks like a stain. Shadows become rings, because a shadow
on `#000` is invisible.

| Token | Value |
| --- | --- |
| `--bg` / `--log-bg` | `#000000` |
| `--bg-glow` | `none` |
| `--panel` | `#07090c` |
| `--panel-2` | `#0b0e12` |
| `--panel-3` | `#050708` |
| `--head` | `linear-gradient(#0c1015, #080b0e)` |
| `--head-strong` | `linear-gradient(#14324d, #0c2338 55%, #091a2a)` |
| `--border` | `#171d25` |
| `--border-2` | `#26303c` |
| `--text` | `#e7eef7` |
| `--text-soft` | `#cbd7e5` |
| `--muted` | `#8597ab` |
| `--link` | `#86bcf5` |
| `--link-hi` | `#a7d2ff` |
| `--accent` | `#4f8fd0` |
| `--accent-hi` | `#78b4f0` |
| `--accent-lo` | `#14324d` |
| `--accent-soft` | `rgba(120,180,240,.13)` |
| `--ok` | `#57c07f` |
| `--warn` | `#d9aa52` |
| `--err` | `#ea6d5f` |
| `--info` | `#6bb2ea` |
| `--gold` | `#e5bb45` |
| `--shadow` | `0 0 0 1px rgba(120,170,255,.05), 0 10px 30px rgba(0,0,0,.85)` |
| `--shadow-hi` | `0 0 0 1px rgba(120,170,255,.12), 0 18px 46px rgba(0,0,0,.95)` |
| `--inset` | `inset 0 1px 0 rgba(255,255,255,.02)` |
| `--topbar` | `linear-gradient(#0b1a27, #06101a 70%)` |
| `--topbar-text` | `#dceaf8` |
| `--scrim` | `rgba(0,0,0,.8)` |
| `--field` | `#050708` |
| `--field-border` | `#26303c` |
| `--hover` | `#0d1117` |
| `--bubble-in` | `#0b0f14` |
| `--bubble-out` | `#0f2438` |
| `--cover` | `linear-gradient(120deg, #0b1a27, #06101a 70%)` |

### 2.5 Branding overrides

`[branding]` in `config.ini` is compiled into `/brand.css` by `Core/Branding.cs` and
linked **after** `app.css`, so it wins by cascade order without touching the repo.
Only base colours are given; everything else is derived by mixing:

| Given | Derived by | Rule |
| --- | --- | --- |
| `accent` | `accent-hi` | mix 22% toward white (light theme) |
| `accent` | `accent-lo` | mix 28% toward black |
| `accent` | `link` / `link-hi` | accent, or a lighter mix in dark themes |
| `accent` | `accent-soft` | accent at 11% (light) / 16% (dark) alpha |
| `accent` | `topbar`, `head-strong`, `cover`, `boot` | 3-stop gradient from hi → accent → lo |
| `panel` | `head` | mix 6% toward white (dark) or black (light) |
| `panel-2` | `panel-3`, `hover` | mix 12% / 7% |

An empty `[branding]` section produces
`/* neutral palette: config.ini [branding] is empty */` and nothing else — the stock
palette stays. This is why the repository is branding-neutral and an instance is not.

---

## 3. Typography

**Stack:** system only. `"Segoe UI", Tahoma, Verdana, Arial, sans-serif` for body,
`"Segoe UI Semibold"` first for display, `Consolas` for monospace. On XP this
degrades to Tahoma, which is exactly the 2008 look; on Windows 10+ it is Segoe UI,
which is the modern note. No webfont means no layout shift, no FOIT, and no 200KB
download on a phone.

**Base:** `body { font: 13px/1.45 var(--font) }`. Line height 1.45, not 1.6 — dense
paragraphs (posts, comments) read better slightly tight.

| Role | Size | Weight | Where |
| --- | --- | --- | --- |
| Page heading | 34px / 1.15, `-0.4px` tracking | 700 display | Landing `h1` only |
| Section heading | 18px | 700 display | Auth card `h2` |
| Brand name | 15px, `+0.2px` | 700 | Topbar, with `text-shadow: 0 -1px 0 rgba(0,0,0,.35)` |
| Brand tagline | 9.5px, `1.4px` tracking, uppercase, 70% opacity | 400 | Under the brand name |
| Profile name | 14px | 700 display | Mini profile, profile head `h1` |
| Card header | 12px, uppercase, `+0.2px` | 700 | Every `.card-h` |
| Post body | 13.5px / 1.5 | 400 | The reading size — the one place we go above base |
| Body | 13px | 400 | Comments, chat, forms |
| UI label | 12.5px | 600 | Sidebar links, post action buttons |
| Button | 12px (11px `-sm`, 10.5px `-xs`) | 600 | All buttons |
| Metadata | 11px | 400 | Timestamps, counts, hints |
| Micro label | 10–10.5px, uppercase, `+0.35–0.4px` | 700 | Field labels, mini-stat captions |
| Badge counter | 9–10px | 700 | Red unread pills |

Rules:

- **Uppercase is reserved for structure**, never for content: card headers, field
  labels, stat captions, brand tagline. Posts and messages are never uppercase.
- **Letter-spacing only goes up** on uppercase micro-labels (+0.2 to +1.4px) and
  **down** on large display text (-0.4px). Body text is always 0.
- **Text on gradients gets `text-shadow: 0 -1px 0 rgba(0,0,0,.25–.35)`** — the 2008
  trick that keeps white type legible on a blue bar without a heavier weight.
- Numbers that tick (timers, counts, bitrate) use `font-variant-numeric: tabular-nums`.

---

## 4. Spacing and radius

There is **no spacing token scale** — this is the honest state, and the most
obvious piece of debt. Values in use are consistent by convention:

| Use | Value |
| --- | --- |
| Page gutter | 16px desktop, 10px ≤820px, 8px ≤560px |
| Column gap | 14px |
| Card padding | 12px (`.card-b`), 11px top on post heads, 8–9px in compact bars |
| Card gap | 12px (`margin-bottom`) |
| Inline gap in a row | 6–9px |
| Control padding | `6px 12px` buttons, `7px 9px` inputs, `4px 8px` small |
| Section rhythm | 10–14px between blocks inside a card |

Radius: `10px` cards/modals, `7px` buttons/inputs/small panels, `4px` chips,
`16px` the feed tab pills, `999px` counters and avatars in a strip, `50%` avatars in
lists and chat, `9–16px` the brand mark depending on context.

> **Rule of thumb:** if it holds content, 10px. If you press it, 7px. If it is a
> dot or a counter, fully round. Nothing is square (0) — that reads as "unfinished
> table", which is the one 2008 habit we skip.

---

## 5. Layout

### 5.1 Shell

```
┌────────────────────────── topbar 48px, sticky, max-width 1200 ─────────────┐
│ brand │ search 230px │ tabs (flex) │ theme │ user                          │
├──────────────┬───────────────────────────────────┬─────────────────────────┤
│ left 222px   │ main 1fr                          │ right 258px             │
│ sticky       │ feed / profile / messenger         │ sticky                  │
└──────────────┴───────────────────────────────────┴─────────────────────────┘
```

```css
.shell {
  display: grid;
  grid-template-columns: 222px minmax(0, 1fr) 258px;
  gap: 14px;
  max-width: 1200px; margin: 0 auto;
  padding: 14px 16px 40px;
  align-items: start;
}
.col-left, .col-right { position: sticky; top: calc(var(--top-h) + 14px); }
```

`minmax(0, 1fr)` on the middle column is load-bearing: without it a long unbroken
string in a post pushes the grid wider than the viewport.

### 5.2 Breakpoints

| Width | What happens |
| --- | --- |
| `> 1200` | Full three columns |
| `≤ 1200` | Topbar tab padding drops to 10px (labels stay) |
| `≤ 1080` | Right column hidden; shell becomes 210px + 1fr; landing goes single-column |
| `≤ 820` | Search hidden; shell single column; **left column turns into a horizontal chip rail**; mini-profile and online card hidden; messenger stacks; profile head wraps |
| `≤ 700` | Topbar tab **labels** hidden, icons only |
| `≤ 560` | Brand tagline and user name hidden, avatar becomes a circle; cover 120px; stats stack; landing `h1` 27px; game grid 2-up |
| `≤ 720` | (guest) notice button stops floating right |
| `height ≤ 480` | Phone landscape: story and call overlays go edge-to-edge, call grid capped at 50vh |

### 5.3 Mobile is a layout, not a shrink

The 820px block is the real mobile design. It does three specific things:

1. **Sidebar → chip rail.** `.side-nav` becomes `display:flex; overflow-x:auto` with
   `999px` pills and hidden scrollbar. The feed starts at the top of the screen
   instead of after 14 nav rows. This is the single most important mobile decision
   in the file.
2. **Messenger → one column.** Threads above, chat below, chat `min-height: 60vh`.
   Thread list capped at `45vh` at 560px so the composer stays reachable.
3. **Finger targets.** `.btn { min-height: 38px }`, `.icon-btn, .chat-btn { 40×40 }`,
   and inputs jump to `font-size: 16px` — that last one is not a style choice, it
   stops iOS Safari zooming the page when a field gets focus.

---

## 6. Surfaces and depth

Four levels, in order:

| Level | Surface | Depth |
| --- | --- | --- |
| Page | `--bg` + `--bg-glow` | none |
| Card | `--panel`, `1px --border`, `--radius`, `--shadow` | faint shadow, 1px outline |
| Card strip | `--head` or `--panel-2` | separated by a 1px border, not a shadow |
| Well | `--panel-3` / `--field` | `inset` highlight or a slightly darker fill |

Rules:

- **Never** stack a heavy shadow on a bordered element — pick one. Cards do border +
  faint shadow; modals do border + `--shadow-hi`.
- **The bevel is one pixel.** `--inset` is a single top highlight. There is no
  multi-stop 3D bevel anywhere except `.btn-primary`, which gets one inner
  highlight plus one outer glow.
- **Blue bars are gradients, everything else is flat.** A gradient on a content
  surface is what makes an interface look dated rather than retro.
- Hover on a surface is a **fill change** (`--hover`), not a lift. Only `.composer`
  lifts on `:focus-within`.

---

## 7. Components

### 7.1 Topbar

`position: sticky; top: 0; z-index: 40;` with `--topbar` gradient, a 1px bottom
border at 35% black, and `0 2px 12px rgba(10,20,35,.25)`.

- `.brand-mark` — 31×31, radius 9, `rgba(255,255,255,.16)→.03` gradient, 1px white
  28% border, inner highlight, 14px bold letter. It is a *plate*, not a logo image,
  so `shortName` from the config can be any 1–3 characters.
- `.topsearch` — 230px max, 28px tall, radius 15, white at 92%, inset shadow, icon
  absolutely positioned at `left: 11px; top: 8px`. Focus brightens to solid white
  and adds a `0 0 0 3px rgba(255,255,255,.2)` halo — a white halo, because the
  accent is invisible on a blue bar.
- `.tab` — 12.5px, 600, `rgba(255,255,255,.8)`, text-shadow. Active: 14% white fill
  plus a **3px white underline** inset 10px from each side, radius `3px 3px 0 0`.
  Hover: 9% fill. The underline is the main "where am I" signal.
- `.tab .count` — 17px tall red pill, `#e0554a`, 10px bold. Hidden when empty.
- `.icon-btn` — 30×30, radius 8, transparent; hover 13% white; `:active` scales to
  .94. Its `.dot` is a 14px red pill at `top:4; right:4`.
- `.top-user` — pill, 9% white fill, 1px 14% white border, avatar 24px round, name
  12px 600 truncated at 170px.

### 7.2 Card

```html
<div class="card">
  <div class="card-h"><i class="fa-solid fa-hashtag"></i> TAGS <span class="right">…</span></div>
  <div class="card-b">…</div>
  <div class="card-f">…</div>
</div>
```

- `.card-h` — `--head` gradient, 8px 12px padding, 12px uppercase 700, `--text-soft`;
  icons in `--accent`; `.right` escapes the uppercase.
- `.card-b` — 12px padding. `.card-b.flush` = 0, for lists that own their padding.
- `.card-f` — `--panel-2`, 1px top border, 11.5px muted.

### 7.3 Sidebar

- `.side-link` — 7px 9px, radius 7, 12.5px 600, icon 16px wide centred in `--muted`.
  Active: `--accent-soft` fill + `--accent` text and icon. Count pill on the right.
- `.side-sep` — 1px line with 6px 8px margins.
- `.mini-profile` — centred, 64px avatar radius 14, name 14px, status 11.5px muted,
  then a 3-cell stat row with a 1px top border.
- `.online-user` — 24px round avatar, 12px name, and a 7px presence dot pushed right
  with `margin-left: auto`, ringed by `0 0 0 2px var(--panel)` so it stays legible
  over any fill.

### 7.4 Buttons

```css
.btn { padding: 6px 12px; border: 1px solid var(--field-border); border-radius: 7px;
       background: linear-gradient(var(--panel), var(--panel-2));
       box-shadow: var(--inset), 0 1px 2px rgba(0,0,0,.05); font: 600 12px; }
.btn:hover  { border-color: var(--border-2); box-shadow: var(--inset), 0 2px 6px rgba(0,0,0,.08); }
.btn:active { transform: translateY(1px); }
```

Variants: `-primary` (accent gradient + white text + text-shadow), `-ok` (green),
`-danger` (red), `-ghost` (no fill, no border, muted). Sizes `-sm` (4/8, 11px),
`-xs` (2/6, 10.5px), `-block` (full width).

**The 1px `translateY` on `:active` is the whole "this is a physical key" feeling.**
Do not replace it with a scale.

### 7.5 Forms

- `.input`, `.select`, `.textarea` — 7px 9px, 1px `--field-border`, radius 7,
  `--field` fill. Focus: accent border + `0 0 0 3px var(--accent-soft)`.
- `.select` is `appearance:none` with **two CSS gradient triangles** as the caret —
  no background image, no extra request.
- `.field > label` — 11px uppercase 700 muted with 3px gap. `.hint` under it at 11px.
- `.check` — flex, checkbox nudged 2px, `accent-color: var(--accent)`.
- `.alert` — 8px 10px, radius 7, tinted fill + matching 30% border; `.err`, `.ok`,
  `.info`. Never a solid saturated block.

### 7.6 Feed

- `.composer` — card that lifts on `:focus-within` to `--shadow-hi`. Top row is a
  36px round avatar plus a textarea that auto-grows to 220px. Bar is `--panel-2`
  with a 1px top border, wrapping on narrow screens.
- `.feed-tabs .tab2` — 16px radius pills; active is a solid accent fill with a
  `0 2px 8px rgba(68,114,164,.3)` glow. This is the only place a pill is a tab.
- `.post` — card, `animation: postIn .28s` (opacity 0→1, translateY 5px→0). **New
  posts animate in; existing ones never do on scroll.**
- `.post-head` — 38px round avatar, name 13px 700, meta 11px with a flex-wrap row of
  icon+label pairs (`pinned`, `edited`, `friends only`, `on wall`).
- `.post-body` — 13.5px/1.5, `overflow-wrap: break-word`.
- `.post-media` — images capped at `max-height: 520px`, radius 7, 1px border,
  `cursor: zoom-in`, centred.
- `.post-actions` — `--panel-2` strip, 6px 8px, buttons 12px 600 muted with a 6px
  icon gap; hover fills `--hover`; `.liked` turns `--err` and the heart runs
  `@keyframes pop` (scale 1.35 at 50%, .3s). **One burst, no particles.**
- `.comments` — 26px avatars, each comment in a `--panel-2` bubble with radius 8.

### 7.7 Messenger

- `.msg-layout` — `240px 1fr`, `min-height: 520px`.
- `.threads` — `--panel-2`, right border, `max-height: 640px`, scrolls.
- `.thread.active` — `--accent-soft` fill.
- `.chat` — flex column; `.chat-body` scrolls; composer is a `--panel` strip with a
  1px top border.
- Bubbles: `.bubble-in` / `.bubble-out`, radius 12px, `max-width: 72%`, 7px 10px
  padding. Outgoing bubbles drop the border. A sticker message uses
  `.bubble.sticker-bubble` — **no fill, no border, no shadow**: the picture is the
  message.
- `.msg-foot` — 10–11px time + tick, right-aligned, muted; ticks turn `--accent`
  when read.
- Typing indicator — three 4px dots running `@keyframes typing` in a staggered loop.
- Voice recording — `.rec-dot` pulses with `@keyframes recdot` (opacity 1 → .25).

### 7.8 Profile

- `.profile-cover` — 170px, `background-size: cover`, gradient fallback from
  `--cover`. `.cover-edit` is a 32px circular button at `right/bottom: 10px` with a
  62% black fill.
- `.profile-head` — 96px square avatar pulled up with a negative margin, name row
  with role/VIP badges, `@username · online`, status line, then action buttons.
- `.rating-strip` — a 6px meter plus a one-line caption. Full = green. The hint line
  (what is missing) is only rendered for the owner.
- `.profile-grid` — 2 columns (info left, wall right), collapsing at 820px.

### 7.9 Modal, toast, scrim

- `.scrim` — fixed, `--scrim`, `fadeIn .18s`, grid-centred, 18px padding.
- `.modal` — max 480px (`.wide` 760), radius 10, `modalIn .22s` (opacity +
  translateY 10px + scale .985). Header/footer strips, scrolling body.
- `.toasts` — fixed bottom-right, 16px, 340px max, `z-index: 120`.
- `.toast` — `toastIn .25s` from `translateX(20px)`; `.out` reverses it. Icon colour
  carries the meaning (`--ok` / `--err` / `--info`). **No toast is ever a solid
  block of colour** — a white card with a coloured icon.

### 7.10 Badges and status

- `.badge` — 10px uppercase pill. `.admin` (accent), `.mod` (green), `.vip`
  (`linear-gradient(135deg,#d8b34a,#b8860b)`, dark ink text), `.ok` (green).
- Achievement badges are icon tiles, `--gold`, with the icon catalogue served from
  `SiteJson.badges` so the SPA never hardcodes it.
- `.presence-dot` — 8px; `.on` = `--ok` with a 2px panel-coloured ring.

### 7.11 Calls

- `.call-overlay` — fixed inset, 72% scrim + `backdrop-filter: blur(3px)`, z 80.
- `.call-panel` — `min(880px, 100%)`, `max-height: 92vh`, radius 14, flex column.
- `.call-grid.solo` — one centred tile, `minmax(0, 560px)`, `aspect-ratio: 4/3`.
  `.duo` — two equal columns. Anything more uses `auto-fit minmax(220px, 1fr)`.
- `.call-tile.speaking` — `inset 0 0 0 3px var(--ok)` and a green name plate. Driven
  by a real `AnalyserNode` RMS > 0.045, sampled every 220ms.
- `.call-mic-off` — 22px red circle, top-right, `hidden` class when unmuted.
- `.call-overlay.mini` — a 240px floating card bottom-right; grid collapses to one
  column, camera, screen and audio buttons hide, people strip keeps only the count.
- `.call-audio-panel` — a small popover above the controls with the three processing
  switches. Changing one re-captures the microphone and swaps the track in every
  peer through `RTCRtpSender.replaceTrack`, which needs no renegotiation.
- `.call-tile.screen video` — `object-fit: contain` and no mirror, because a shared
  screen is letterboxed: cropping a slide hides the slide.
- `.call-dot` — 8px `#4c9dff` dot with `call-dot-pulse` (scale 1→1.35, opacity
  1→.65, 1.4s) marking "somebody joined". The same idea appears as `.call-live` on
  the sidebar entry.

### 7.12 Landing and auth

- `.landing` — 2 columns `1.15fr / minmax(340px,420px)`, 34px gap, centred, 1060px
  max. At 1080px it goes single column at 560px max.
- Hero: 48px brand plate, 22px brand name, 34px `h1`, 14.5px lead, then a 2×2 grid
  of `.landing-feature` cards (icon + bold label + muted line).
- `.auth-card` — radius 10, `--shadow-hi`, header strip, tab row where the active tab
  is a *file-folder tab* (bottom border removed, `margin-bottom: -1px`) — a direct
  2008 reference, kept because it still reads instantly.
- Captcha: SVG in a bordered box, 90px input with 3px letter-spacing.

### 7.13 Stories, gifts, games, apps, admin

- Stories — a horizontal bar of gradient rings above the feed; the ring fills with
  `@keyframes story-fill` over the 5s clip; the overlay is a 9:16 frame.
- Gifts — a catalogue grid of icon tiles, each with its own colour; a sent gift is a
  row with the icon, sender, note and time.
- Games — `.game-grid`, 2-up at 560px; cards are covers with a play affordance.
- Apps — `.app-grid` of `.app-card`s: icon + name, description, author + installs,
  then a permission badge and actions.
- Admin — dense tables, a log viewer on `--log-bg` with monospace, and a settings
  form built from `.settings-block` panels.

---

## 8. Motion

One curve (`--ease`), short durations, and **no animation on content that is merely
being scrolled into view**. Full inventory:

| Animation | Duration | What it does | Where |
| --- | --- | --- | --- |
| `postIn` | .28s | opacity 0→1, translateY 5px→0 | A newly created post |
| `pop` | .3s | scale 1→1.35→1 at 50% | Heart on like |
| `spin` | .8s linear ∞ | rotate 360 | Spinners |
| `fadeIn` | .18s | opacity | Scrim |
| `modalIn` | .22s | opacity + translateY 10 + scale .985 | Modal |
| `toastIn` | .25s | opacity + translateX 20 | Toast in; `.out` reverses |
| `typing` | 1.1s ∞ | staggered dot bounce | Typing indicator |
| `recpulse` / `recdot` | — | recording badge | Voice recorder |
| `call-dot-pulse` | 1.4s ∞ | scale + opacity | "Somebody joined" dot |
| `story-fill` | 5s linear | width 0→100% | Story progress |
| `boot` fade | .35s | opacity 1→0 | Boot screen |

Transitions: `.15–.18s` for hover/focus colour and fill; `.2–.25s` for shadows and
transforms; `.4s` for the profile rating meter. Nothing exceeds 400ms.

`@media (prefers-reduced-motion: reduce)` clamps **every** animation and transition
to `.001ms`. That block is global and unconditional — keep it that way.

---

## 9. Iconography

Font Awesome 6.5.2 from `cdnjs`, loaded once in `index.html` with `crossorigin` and
`referrerpolicy="no-referrer"`. Rendered through one helper:

```js
UI.icon('hashtag')  // → <i class="fa-solid fa-hashtag"></i>
```

- Sizes: 12px in bars and buttons, 13px in post actions, 16px column in the sidebar.
- Colour is inherited except in `.card-h` (accent) and status icons (semantic).
- Icons are always paired with text except in `.icon-btn`, which is why every
  `.icon-btn` carries a `title`.
- **No emoji as UI.** Emoji appear only inside user content, reactions and the
  picker. The one exception is the sticker set, which is drawn SVG, not emoji.

If the CDN is unreachable the icons vanish and the layout holds — nothing depends on
an icon for meaning.

---

## 10. Imagery

- **Avatars** — 8 deterministic gradient buckets (`.a0`–`.a7`) chosen by
  `user.id % 8`, so the same person is always the same colour. Initials in white
  700. Uploaded avatars replace the gradient, never both.
- **Covers** — 170px strip, `object-fit: cover`, gradient fallback.
- **Post images** — capped at 520px tall, `loading="lazy"`, click to zoom.
- **Media** — clips and audio always get native controls; no custom player chrome
  except the music library, which has one.
- Photo grids are square cells with a 2–3px gap; albums use a 4-up mosaic.

---

## 11. Accessibility

Present today:

- `prefers-reduced-motion` honoured globally.
- Every `.icon-btn` and icon-only control has a `title` or `aria-label`.
- Focus is visible on all inputs (accent border + soft halo).
- Contrast: body `#223044` on `#ffffff` ≈ 12:1; muted `#6d7e93` on white ≈ 4.6:1;
  white on the topbar gradient ≥ 4.5:1 at the mid stop.
- Semantic landmarks: `aside`, `main`, `nav`, `article`, `form`, `label`.
- Touch targets ≥ 38px on mobile.

Known gaps (see §14):

- Focus rings exist on inputs but not on every custom button — `:focus-visible` is
  not styled globally yet.
- Toasts are not `aria-live`.
- The sticker and emoji pickers are not keyboard-traversable.
- Colour alone carries a few states (presence dot, unread dot).

---

## 12. Theming and the shell

- Theme is `data-theme` on `<html>`, set before paint from `localStorage`, falling
  back to `site.defaultTheme`. No flash of the wrong theme.
- **Template (skin)** is `data-skin` on the same element, and it is the layer that
  changes *personality*: palette plus shape language. See §12.1.
- `UI.applyTheme`, `UI.cycleTheme`, `UI.getTheme` in `ui.js`; the picker is a set of
  `.theme-dot` swatches in Settings.
- `--top-h` is the single source for the sticky offset; the shell and both columns
  derive from it. Change the bar height in one place.
- `?v=` on every asset in `index.html` is a **manual cache-buster**. Bump the JS
  version and the CSS version on every asset change or browsers serve the old file.

### 12.1 Templates (`skins.css`)

Fifteen templates ship in `public/assets/css/skins.css`, loaded **after** `brand.css`
so a skin wins by cascade order. A skin overrides custom properties, plus a small
number of rules where the *form* differs (metro has no radii, brutal has hard offsets,
paper uses serif headings). **No markup changes between skins** — that is what makes
this a template layer rather than fifteen forks.

| id | Name | Shape | Palette | Designed for |
| --- | --- | --- | --- | --- |
| `branded` | Branded | stock | stock + `[branding]` | The 2008 chrome. **The only skin `[branding]` styles.** |
| `aero` | Aero | 12px, glossy inset | blue glass | Vista-era gloss, tamed |
| `metro` | Metro | 0px, no shadows | flat blue on white | 2012 tiles, solid header blocks |
| `paper` | Paper | 2px, no shadows | cream + oxblood, serif | print, the reading skin |
| `terminal` | Terminal | 0px, mono | amber-green on black | a console, dense by design |
| `soft` | Soft | 16px, borderless | indigo on near-white | 2026 restraint |
| `brutal` | Brutal | 0px, 2px borders, hard offset shadows | orange on bone | deliberate non-polish |
| `neon` | Neon | 6px, 1px glow rings | violet/cyan on black | night mode as a skin |
| `warm` | Warm | 10px | terracotta + cream, serif | mid-century warmth |
| `slate` | Slate | 6px, hairlines | teal on cool grey | the professional tool |
| `matrix` | Matrix | 2px, mono | phosphor green on black | the terminal taken seriously |
| `cyberpunk` | Cyberpunk | 2px, hard neon rules | magenta + cyan on indigo | the three-way neon split |
| `newyear` | New Year | 12px | gold on midnight blue | festive without tinsel |
| `autumn` | Autumn | 8px, serif | burnt orange + parchment | October evening |
| `horror` | Horror | 3px, serif | blood on black | the VHS skin |

Rules a new skin must follow:

1. **Tokens only, plus form overrides.** No new layout, no new markup.
2. **Both themes must be usable.** Every skin needs a `[data-theme="dark"]` block
   except the inherently dark ones (`terminal`, `neon`, `matrix`, `cyberpunk`,
   `horror`), which still need their light-theme tokens to not be broken if someone
   switches.
3. **No animation, no glass, no gradient on a content surface.** The §13 list applies
   to every skin. Atmosphere belongs in `effects.css`, not here.
4. **System fonts only**, and no font that is not on Windows XP.
5. **The id must be added to three places**: `Config.Skins` in C#, `SKINS` in
   `ui.js`, and `SKIN_FX` in `ui.js` (its default effect). The server refuses an
   unknown id (it lands in a DOM attribute), so a skin missing from the C# list
   simply cannot be selected.
6. **Add a `.skin-row[data-skin="name"]` swatch** at the bottom of `skins.css`, or the
   picker shows an empty tile.
7. **Add it to a group** in `SKIN_GROUPS` (`ui.js`), and optionally a `VOICES` entry in
   `sound.js` for its own synth character.

Precedence, highest first: the browser's own choice (`localStorage['social.skin']`) →
the account's choice (`user.skin`, so it follows the user to another device) → the
site default (`ui.skin` in `config.ini`, with an admin dropdown).

`[branding]` defines an accent for the **branded** template only. A skin brings its own
accent; layering a brand accent over `neon` would fight it. If a deployment wants a
branded skin, the honest path is a new skin with those colours, not a global override.

The picker in Settings is a grid of `.skin-dot` swatches — bar gradient, page fill and
an accent chip — which is enough to tell fifteen skins apart at 64×40. It degrades to
3-up at 820px and 2-up at 560px.

### 12.2 Effects (`effects.css`)

The atmosphere layer is a **second, independent axis**: a template is a look, an
effect is what the look *does*. Applied by `data-fx` on `<html>`, rendered as one
fixed `.fx-layer` element created by `ui.js` only when an effect is active, so a
plain page pays nothing.

| id | Effect | Mechanism |
| --- | --- | --- |
| `none` | — | no layer at all |
| `grain` | Film grain | one static SVG `feTurbulence` tile, multiply/screen blend |
| `scanlines` | CRT raster | repeating 3px gradient + a slow opacity flicker |
| `vhs` | VHS tape | 4px raster, a rolling bright band, a 1px chroma split on headings, a 2px horizontal wobble every few seconds |
| `matrix` | Matrix rain | vertical repeating gradient scrolling down, masked at the edges |
| `glitch` | Glitch | permanent 1px chroma on chrome; cards glitch 160ms on hover |
| `snow` | Snow | two tiled radial-gradient layers at different speeds |
| `leaves` | Falling leaves | same idea, warmer, with a sideways drift |
| `aurora` | Aurora | two soft colour fields drifting, 26s |
| `glow` | Neon glow | pulsing rim on the bar, `drop-shadow` on accent icons |
| `sheen` | Glass sheen | one diagonal highlight sweeping the bar, Vista-style |
| `paper-cut` | Print offset | no motion: a hard offset shadow on the bar and cards |
Each skin declares its default effect in `SKIN_FX` (`ui.js`) and `Config.DefaultEffect`
(C#), and **choosing a template resets the effect to that template's default** —
otherwise picking `horror` while a manual `glow` was stored would give a horror
palette with a neon glow, which is nobody's intention. The user can then override the
effect independently from the Settings dropdown, and `none` is always available.

Hard rules, because these run on old hardware:

- **CSS only.** No WebGL, no canvas, no per-frame JavaScript.
- **Only `background-position`, `transform` and `opacity` animate.**
- Every effect is `pointer-events: none`, `contain: strict`, and sits at `z-index: 90`
  (above content, below the modal scrim at 80 — the layer is decorative, modals win).
- `prefers-reduced-motion` switches every one of them off in a single block at the
  bottom of the file.
- No effect may reduce text contrast below the skin's own baseline.

**Adding an effect** is three steps and no component work:

1. A block in `effects.css` — `html[data-fx="name"] .fx-layer::before { … }`.
2. Add the id to `EFFECTS` in `ui.js` and an `fx.name` key to `i18n.js`.
3. Add a line to the `prefers-reduced-motion` block at the bottom of the file.

**Adding a template** is five steps:

1. A token block in `skins.css` — `html[data-skin="name"] { --bg: …; }` plus a
   `[data-theme="dark"]` variant.
2. Form overrides only where the shape language really differs.
3. The id in `Config.Skins` (C#), `SKINS` and `SKIN_FX` (`ui.js`).
4. A `.skin-dot[data-skin="name"]` swatch so the picker has a preview.
5. A `skin.name` key in `i18n.js`.

Neither touches a component, a view or an endpoint — which is the property that makes
future visual work cheap.

### 12.3 Sound follows the template

Every template has its own **synth voice** (`VOICES` in `sound.js`), applied on top
of the event presets: `transpose` (semitones), `filter` (a multiplier on the low-pass
cutoff), `type` (waveform override), `decay` (note length), `gain`, `detune` (cents on
the second oscillator) and `wobble` (slow pitch drift). So Paper is a dull, low,
sustained triangle; Cyberpunk is a bright, sharp sawtooth a fourth up; Horror is an
octave down with 26 cents of detune and a tape wobble; Matrix is short and square.
Still no audio files — it is the same synth with different parameters.

`UI.applySkin` calls `SoundFX.setSkin`, so the sound changes with the look. Volume,
the master switch and the per-event toggles are untouched and stay the user's.

Adding a template therefore means adding a `VOICES` entry as well, or it falls back
to the `branded` voice — which is a safe default, not a break.

### 12.4 The template picker

Settings shows the templates as a **grouped, scrollable list**, not a wall of tiles:
a swatch, the name, a one-line description and a tick on the active one. The groups
are Classic / Modern / Dark / Seasonal, defined in `SKIN_GROUPS` in `ui.js`, with
sticky headers so fifteen entries stay scannable while scrolling. Each row is a real
`<button role="option">` inside a `role="listbox"`, so it is reachable by keyboard.

### 12.5 Font sets (`fonts.css`)

A **third independent axis**, next to template and effect:

```
<html data-skin="paper" data-fx="grain" data-font="serif">
```

A template may suggest a typeface (paper and warm are serif, terminal is mono), but
`fonts.css` loads after `skins.css` and therefore wins — which is the point: keep the
Paper palette and still choose a monospace face.

| id | Set | Face |
| --- | --- | --- |
| `system` | System | Segoe UI, or Tahoma on old Windows |
| `serif` | Serif | Georgia |
| `mono` | Monospace | Consolas, or Lucida Console on XP |
| `humanist` | Humanist | Trebuchet MS |
| `compact` | Compact | Tahoma, set tight |
| `classic` | Classic | Verdana |
| `elegant` | Elegant | Palatino Linotype / Book Antiqua |

Every face ships with Windows, including XP, so the promise that the server runs there
still holds: no webfont, no network request, no layout shift. Each set also carries a
small `--font-scale` (0.95–1.03) so a narrower face does not read as smaller text.

The picker is a list of `.font-row`s whose specimen (`Ag`) is set **in the face it
describes**, which is the only honest way to preview a typeface.

Adding a set is three steps: a token block in `fonts.css`, the id in `Config.Fonts`
(C#) and `FONTS` (`ui.js`), and a `.font-row[data-font="…"] .spec` rule for the
specimen.

### 12.6 Settings layout

Settings are **tabbed** — Profile / Appearance / Notifications / Privacy / Account —
rather than one two-column wall. The old layout squeezed the right column to
unreadable width below ~1100px, which is exactly the bug the screenshot showed. Tab
switching does not re-render the form, so a half-typed password survives a look at
another panel, and the active tab is remembered in `localStorage['social.settingsTab']`.

Each panel is a `.settings-grid` of `.settings-block`s; a block that is alone in its
panel spans both columns via `:only-child`.

### 12.7 Holiday themes without a release

`ui.skinSchedule` takes `skin:MM-DD:MM-DD` entries, comma separated, and a range may
cross the new year:

```ini
[ui]
skin=branded
skinSchedule=horror:10-25:11-02,newyear:12-20:01-10,autumn:09-20:10-20
```

While a range is active it **wins over `ui.skin`**, so a holiday theme needs no client
release, no code change and no restart (it is evaluated per request against
`DateTime.Now`). A malformed entry is skipped rather than killing the whole list, and
`Validate()` adds a warning when the value clearly is not in that shape.

`GET /api/theme` exposes the result publicly and unauthenticated, so a launcher, a
widget, a kiosk or a deployment script can ask what the instance looks like today:

```json
{ "ok": true, "skin": "horror", "configured": "branded", "active": "horror",
  "locked": false, "effects": true, "schedule": "horror:10-25:11-02",
  "skins": [ { "id": "branded", "effect": "none" } ] }
```

`active` is empty unless a holiday range is actually in force, which makes it the
field to check. The endpoint leaks nothing but template ids — no users, no counts.

### 12.8 Locking the look

`ui.skinLocked=true` makes the template **staff-only**: a regular account gets `403
skin-locked` from `POST /api/settings`, and the picker in Settings renders disabled
with an explanation. The admin dropdown still works, so the instance can be restyled
without editing the file. This exists for deployments that want one consistent look
rather than fifteen.

---

## 13. Do not

These are the specific ways this design could be ruined. Each one has already been
considered and rejected.

- **No glass.** No `backdrop-filter` on cards, no translucent white panels, no
  frosted nav. The one blur is behind the call overlay, where it separates a live
  video surface from the page.
- **No gradient text**, no gradient borders on cards, no "aurora" backgrounds.
- **No giant type.** Nothing above 34px, and that only on the landing `h1`.
- **No pill-everything.** Pills are for counters, feed tabs, mobile nav chips and
  the top-user plate. Buttons stay 7px.
- **No scroll animation on the feed.** Posts do not fade in as you scroll. Reading
  must be still.
- **No 3D tilt, parallax cards, magnetic buttons, cursor followers, goo menus or
  shader transitions.** They cost frames and read as a portfolio, not a tool.
- **No emoji in chrome.** Not in buttons, not in headings, not in empty states.
- **No colour-only meaning** for anything destructive or stateful.
- **No dark-mode-only design.** Light is the default and the reference.
- **No webfont.** It would break the XP promise and add a render-blocking request.
- **No shadows on text except on gradients**, where the shadow is 1px and black.

---

## 14. Known weaknesses (honest list)

These are real, and worth fixing during the next pass rather than pretending the
system is finished:

1. **No spacing scale.** Padding values are consistent but ad hoc (8/9/10/11/12/14).
   A 4px-based token set (`--sp-1: 4px` … `--sp-6: 24px`) would remove ~40 magic
   numbers without changing a single pixel if chosen from the values already in use.
2. **Type scale is implicit.** Sizes live in rules, not tokens. Same fix, same
   benefit.
3. **`app.css` is one 2.1k-line file** with 50+ sections. It loads fine, but editing
   it means scrolling. A split by concern (tokens / layout / components / screens)
   would help; a build step would be needed to concatenate, which the "no tooling"
   rule forbids — so the split has to be by `@import`, which costs requests. Worth a
   decision, not a reflex.
4. **No `:focus-visible` system.** Keyboard users get browser defaults on some
   buttons and nothing on others.
5. **Toasts are silent to screen readers.** Needs `role="status"` + `aria-live`.
6. **The mobile nav rail is a scroll-only strip.** No indication that it scrolls,
   no "more" affordance. On a 320px screen the last chips are effectively hidden.
7. **`--head-strong` is used for two different jobs** (a page banner and an active
   section title) with no size rule attached, so each usage re-decides its own
   padding.
8. **Emoji rendering differs per OS.** The picker is fine; reaction chips look
   different on Windows 7 vs 11. Acceptable, but worth knowing.
9. **No dark variant of the brand gradient** beyond `[branding]` overrides — an
   unbranded dark instance keeps a fairly light blue bar.
10. **The 2008 density has a floor.** Below ~11px labels stop being readable on a
    phone; we are one step away from that floor and should not go further.

---

## 15. Changing the design safely

1. Edit `public/assets/css/app.css` (and `ui.js` if a helper renders the markup).
2. `node --check` any JS you touched.
3. Bump **both** `?v=` values in `public/index.html` (scripts and `app.css`).
4. Stop the server (`Get-Process SocialServer | Stop-Process -Force`) — the build
   cannot overwrite a locked exe.
5. `cmd /c build.cmd` — copies `public/` into `bin/public`.
6. Start it again and check the health endpoint.
7. `node tools/make-demo-screens.mjs` regenerates the README screenshots from demo
   pages that use the real `app.css`, so a design change is visible in the docs
   without exposing a real instance.
8. `powershell -File tools/ci-test.ps1` for the 483 API checks.

**Visual regressions are cheap to catch here**: the demo pages cover feed (dark and
light), profile, messenger, calls, apps and login in one run.
