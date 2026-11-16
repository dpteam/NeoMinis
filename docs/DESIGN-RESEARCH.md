# DESIGN-RESEARCH.md — what to borrow, from where, and why

This is the working notebook behind the next visual pass. `docs/DESIGN.md` records
what the interface **is**; this file records what it should **become**, which
techniques are worth taking from the current crop of motion and design references,
and — just as importantly — which ones are traps.

The target is stated in one line:

> **Retro VK / Facebook 2008 chrome, with modern craft.** Dense, three-column,
> blue-bar, 1px-bordered, small-type — but with real spacing, real easing, real
> empty states and real mobile behaviour. Desktop and phone are both first-class.

And the constraint that matters more than any single technique:

> It must not look like AI slop. That is not a matter of taste here — it is a
> checklist at the end of this file (§8).

---

## 1. Sources studied

| Source | What it is | What it was actually good for |
| --- | --- | --- |
| [designbookmark.com/motion](https://designbookmark.com/motion) | ~90 live motion components, each copyable | The vocabulary: names for things we already half-built (morph tooltip, spring dropdown, pull-to-refresh, number roll, tab pill, bottom bar) |
| [transitions.dev/library](https://transitions.dev/library.html) | Production UI transitions with exact specs | The **best source for values**: durations, stagger, what enters from where. Every row below with a duration came from here |
| [smoothui.dev](https://smoothui.dev) | Motion + GSAP React components | Music toggle anatomy, dynamic island, dither/emboss surfaces, and a good accessibility pattern to copy |
| [60fps.design](https://60fps.design) | 2,140 curated app interactions, filterable by gesture/effect | **Mobile behaviour**: bottom sheets, pull to refresh, shared-element opens, splash. This is the phone reference, not the desktop one |
| [designeer.xyz](https://designeer.xyz) | Hand-reviewed catalogue of finds | Signal on what is currently considered *craft* vs. *effect*: embossed paper, dither, pixel work, tactile switches |
| [recent.design](https://recent.design) | Feed of current design work | Confirming the anti-trend: the interesting work is tactile and restrained, not gradient-and-glass |
| [ui.gradientdeck.com](https://ui.gradientdeck.com) | 1,295 `DESIGN.md` files from real sites | The **document format** for `DESIGN.md`, and one directly relevant case (Poolsuite) |
| [getdesign.md](https://getdesign.md) | 550+ `DESIGN.md` analyses | Era-specific references: Dell 1996 and Nintendo 2001 — useful as a **vocabulary**, rejected as a template (see §2.1) |
| [originkit.dev](https://originkit.dev) | Component kit | Did not load (no output). Not used |

Two references did the heavy lifting:

**Poolsuite** (via GradientDeck) is the single most useful case because it proves the
retro-modern combination can be done without irony: 1px black outlines, 2–6px radii,
hard inset bevels, bitmap type held at native size — and then exactly one long soft
shadow under each window so it does not feel flat. Its rule *"do not use soft shadows
on buttons; keys take hard 1px inset bevels, and only windows get the long blur"* is
the same discipline our buttons already half-follow.

**Dell 1996 / Nintendo 2001** (via getdesign.md) are the era in its original form:
flat colour-block ribbon cards, chunky Helvetica Black over Times, beveled metal
plates, halftone-dotted carbon bars. Useful as a **palette of moves**, not as a
template — see §2.1.

### 2.1 Why the literal 1996/2001 look was rejected

The first draft of this plan pointed at Dell 1996 and Nintendo 2001 as the target.
That was wrong, and the reason is worth writing down so it does not come back:

> They are too far back. 1996 and 2001 are not "retro", they are **archaeology**.
> A reproduction reads as a costume, and a costume is the other kind of failure —
> just as bad as looking generated.

What we actually want is a **hybrid**: the density and the chrome vocabulary of
2006–2008, built with 2026 craft. The era is the *reference for structure*, not a
skin to copy:

| Take from the era | Leave in the era |
| --- | --- |
| Three-column density, small type | Bitmap fonts held at 16px |
| 1px borders, card header strips | 3–4 stop 3D bevels |
| Blue gradient bar, tab underline | Table layouts, spacer GIFs |
| File-folder tabs | Word-art, animated GIF stickers |
| Flat colour-block accents | Metal plates and chrome textures |

The practical conclusion was **not** to pick one look, but to make the look a
**template**: `public/assets/css/skins.css` now carries ten skins on `data-skin`,
Stock among them, so the choice is a setting rather than a rewrite. That is the same
move a CMS makes, and it is why the rest of this document is about *craft that works
in any skin* — motion, loading, mobile — instead of about one palette.

---

## 2. What we keep from the current design

Everything in `DESIGN.md` §1. Specifically, these are non-negotiable:

| Keep | Why |
| --- | --- |
| Three-column shell, 222 / 1fr / 258 | The single strongest 2008 signal, and genuinely the best use of a 1200px screen for a feed |
| 1px border on every card | It is what makes the UI crisp at any DPI; shadows alone turn to mush |
| Blue gradient topbar | The brand bar; also the only saturated large surface, which is why the rest can stay calm |
| Small type + density | The product's character. Modernising it means *spacing*, not *size* |
| Card header strip | Cheap hierarchy, instantly scannable, no extra markup |
| Tab underline for "where am I" | Clearer than a pill and more 2008 |
| One easing curve | Consistency reads as intent |
| System fonts | XP support, zero requests, and Tahoma *is* the era |

---

## 3. What to add — the modern half

These are the changes that move it from "retro skin" to "retro craft".

### 3.1 Motion system (the biggest win)

Today: 11 keyframes, 29 transitions, all ad hoc. Target: **one motion scale**, used
everywhere, with rules about what is allowed to move.

```css
:root {
  /* duration: name the job, not the number */
  --t-tap:   90ms;    /* press feedback — must feel instant */
  --t-hover: 150ms;   /* colour, fill, border */
  --t-enter: 220ms;   /* things appearing: modal, popover, toast */
  --t-large: 280ms;   /* a whole panel or post entering */
  --t-meter: 400ms;   /* value bars only */

  /* easing: two curves, not five */
  --ease:      cubic-bezier(.22, .7, .28, 1);   /* the house curve (existing) */
  --ease-out:  cubic-bezier(.16, 1, .3, 1);     /* decelerate: enters */
  --ease-back: cubic-bezier(.34, 1.56, .64, 1); /* 1 small overshoot: "lands" */

  /* distance: never more than this */
  --d-enter: 6px;
}
```

Rules:

- **Only `transform` and `opacity` animate.** Colour and shadow may transition, but
  nothing that triggers layout.
- **Enters come from the thing that caused them.** A popover opens from its button's
  origin (scale + fade, 220ms). A modal comes from the centre. A toast comes from the
  edge it lives on.
- **Exits are faster than enters** (~60%): leaving should feel like a release.
- **Stagger is 24ms per item, capped at 6 items.** More than 6 and it is a loading
  screen, not a flourish.
- **Nothing loops forever except**: spinners, the recording dot, the "joined" dot,
  and the typing indicator. A pulsing anything else is noise.
- **Overshoot only for things that "land"**: a like, a toast arriving, a toggle
  snapping. Never for a modal, never for a menu.

### 3.2 Mobile: a bottom bar and a bottom sheet

This is the largest structural change and the one with the clearest payoff.

Today the mobile sidebar is a horizontally scrolling chip rail. It works, but it
hides destinations off-screen with no affordance. Replace it with:

- **A fixed bottom tab bar** (Feed · People · Messages · Notifications · More),
  56px tall, `env(safe-area-inset-bottom)` padding, 5 items, icon over 10px label.
  Active item gets an accent icon and a 3px top indicator — the same language as the
  desktop tab underline, which is a nice bridge.
- **"More" opens a bottom sheet** with the full destination list. Bottom sheet, not a
  modal: slides up 280ms `--ease-out`, drag handle, backdrop, dismiss on drag-down
  past 25% or on backdrop tap.
- **Every modal on mobile becomes a bottom sheet.** Same content, correct gesture.
- The chip rail survives only *inside* a page (e.g. feed tabs), where it belongs.

### 3.3 Loading and empty states

Spinners everywhere is the cheapest-looking thing in the current UI. Replace the
three most common ones with **skeletons that match the real layout** (post, thread
row, profile header), fading into content on arrival (opacity, 180ms). Keep the
spinner for anything under ~300ms where a skeleton would itself flash.

Empty states get: icon, one bold line, one muted line, and — where it makes sense —
the action that fills the gap. No illustrations.

### 3.4 Micro-details worth the effort

| Detail | Effect |
| --- | --- |
| Sliding tab indicator | The feed-tabs pill and the topbar underline both slide instead of jumping. One shared implementation |
| Number ticker | Like counts, unread badges and stat numbers roll digits instead of swapping |
| Origin-aware popovers | Post menu, emoji and sticker pickers scale from their trigger |
| Shared-element image open | A post image expands into the lightbox from its own rectangle |
| `:focus-visible` everywhere | 2px accent ring, 2px offset, on every custom control |
| Icon swap on state change | Mic on/off, theme sun/moon: scale + blur crossfade, 150ms |
| Error shake | Login and captcha only, 3 × 4px, 260ms — the one place a shake is earned |
| Success check | The saved-toast icon draws itself (stroke-dashoffset, 300ms) |
| Copy affordance | Invite and transfer codes: copy button morphs to a check for 1.2s |
| Pull-to-refresh | Mobile feed only, rubber-band + spinner, 60px threshold |

---

## 4. Techniques, mapped to our screens

Priority: **P1** = next pass, **P2** = after, **P3** = parked.

| # | Technique | Source | Lands in | Effort | P |
| --- | --- | --- | --- | --- | --- |
| 1 | Motion scale + `:focus-visible` | transitions.dev values | `app.css` root | S | P1 |
| 2 | Bottom tab bar + bottom sheet | 60fps, transitions.dev | mobile shell, all modals | M | P1 |
| 3 | Skeletons for post / thread / profile | transitions.dev | feed, messenger, profile | M | P1 |
| 4 | Sliding tab indicator | transitions.dev | `.feed-tabs`, `.hub-tabs`, topbar | S | P1 |
| 5 | Number ticker | transitions.dev, SmoothUI | counters, badges, stats | S | P1 |
| 6 | Origin-aware popovers | transitions.dev | post menu, emoji, sticker | S | P1 |
| 7 | Icon swap (scale+blur) | transitions.dev | theme toggle, mic/cam | S | P1 |
| 8 | Error shake + success check | transitions.dev | login, captcha, saved toast | S | P1 |
| 9 | Shared-element image open | 60fps, SmoothUI | post image → lightbox | M | P2 |
| 10 | Pull-to-refresh | 60fps | mobile feed | S | P2 |
| 11 | Music toggle anatomy (spinning disc + waveform + seek ring) | SmoothUI | music library mini-player | M | P2 |
| 12 | Dynamic-island morph | SmoothUI | call overlay → minimised window | M | P2 |
| 13 | Avatar group distance-falloff hover | transitions.dev | call people strip, friends online | S | P2 |
| 14 | Copy button → check | transitions.dev | invite code, transfer code | S | P2 |
| 15 | Dither / pixel surface | SmoothUI, designeer | cover fallback, 404, boot | M | P2 |
| 16 | Emboss surface (1px, not 3D) | SmoothUI, Poolsuite | primary buttons, active tabs | S | P2 |
| 17 | Tooltip: delayed in, instant out | transitions.dev | `.icon-btn` titles | S | P2 |
| 18 | Accordion with grid-rows | transitions.dev | settings blocks, admin sections | S | P3 |
| 19 | Toggle double-bounce | transitions.dev | settings switches | S | P3 |
| 20 | Reaction chip pop | SmoothUI | message and post reactions | S | P3 |
| 21 | Like particles (once, reduced-motion aware) | transitions.dev | post like | S | P3 |
| 22 | Command palette | transitions.dev | global search (after search rework) | L | P3 |
| 23 | Splash / logo reveal | 60fps | boot screen | S | P3 |
| 24 | Streak / reward moment | 60fps | achievement unlock | M | P3 |

---

## 5. What we skip, and why

Not "we didn't get to it" — deliberately rejected.

| Skipped | Why |
| --- | --- |
| Liquid glass, glass cards, glass player | Needs a rich backdrop to read; on a dense feed it just lowers contrast. Also expensive on old GPUs |
| Shader transitions (aperture, luma, warp) | A social network is not a presentation. Cost with no informational payoff |
| 3D tilt, cursor glare, magnetic buttons | Turns every clickable thing into a toy. Fine on a portfolio, hostile on a feed |
| Goo menus, morph surfaces, Siri orbs, AI orbs | Distinctive once, exhausting everywhere, and instantly reads as a template |
| Gradient text, gradient borders, aurora backgrounds | The literal visual definition of the thing we are avoiding |
| Scroll-driven animation on the feed | Reading must be still. This is the single most important restraint in the list |
| Parallax on cards | Same reason, plus jank on a phone |
| Confetti | Except, maybe, one achievement unlock — and only if reduced-motion is off |
| Holographic foil, noise overlays on text | Cost, and it fights legibility at 12px |
| Desktop-metaphor windows (draggable cards) | Fun for a landing page, unusable for a feed. Poolsuite can; we cannot |
| Emoji as UI | Already banned in `DESIGN.md` §13 |
| A webfont for "character" | Breaks the XP promise and the zero-request rule |

---

## 6. The retro-modern bridge

Where the two halves actually meet. These are the details that make it read as *a
2008 site, done well* rather than *a modern site wearing a costume*:

**Shipped already, in `skins.css`:** the fifteen templates are exactly this bridge
made concrete — `aero` is the gloss tamed to one inset highlight, `metro` is flat
tiles with one solid header block, `paper` is print, `terminal` is density as a
console, `soft` is 2026 restraint, `brutal` is deliberate non-polish, `horror` is
black and blood with a VHS layer on top. Same markup in all fifteen, which is the
proof that the *structure* is the 2008 part and the *personality* is a setting.

1. **One-pixel bevel, not three.** The 2008 button had a 3–4 stop bevel. Ours keeps a
   single `inset 0 1px 0 rgba(255,255,255,.3)` plus a 1px darker border. Same read,
   none of the mush.
2. **File-folder tabs.** The auth card already uses them (active tab loses its bottom
   border and sits 1px lower). Keep, and consider for the profile sections.
3. **Dithered gradient covers.** Instead of a flat `--cover` gradient, a 2×2 ordered
   dither pattern in the same hue — a nod to 8-bit colour without a bitmap asset.
4. **The tab underline as the primary state signal.** It is more period-correct than
   a pill and more legible than a colour change.
5. **Small caps for structure.** Card headers, field labels and stat captions are
   uppercase micro-type; content never is. That contrast is the 2008 voice.
6. **Numbers are tabular.** Timers, counts and bitrates do not jitter.
7. **The mobile bottom bar is a dock.** Same idea as a 2008 desktop dock, modern
   gesture behaviour. It is the clearest retro-modern object in the design.
8. **Rationed colour.** One accent (blue), one secondary key (red for unread/danger),
   one gold for achievements. Poolsuite's lesson: the discipline *is* the style.

---

## 7. Implementation phases

Each phase is independently shippable and independently testable.

| Phase | Scope | Risk | Touches |
| --- | --- | --- | --- |
| **0. Templates** ✅ | Fifteen skins on `data-skin`, per-account choice, admin default, lock, date schedule, server validation, picker in Settings | done | `skins.css`, `ui.js`, `Config.cs`, `ApiRouter*` |
| **0b. Effects** ✅ | Twelve atmosphere layers on `data-fx`, one per template by default, user-overridable, `prefers-reduced-motion` off-switch, pure CSS | done | `effects.css`, `ui.js`, `Config.cs` |
| **1. Tokens** | Spacing scale, type scale, motion scale, `:focus-visible`, `aria-live` toasts. **Zero visual change intended** — values are chosen from what is already there | Low | `app.css` root + a handful of rules |
| **2. Motion primitives** | `UI.motion()` helper (enter/exit/stagger), sliding tab indicator, number ticker, origin-aware popover, icon swap, shake/check | Low | `ui.js`, `app.css` |
| **3. Mobile shell** | Bottom tab bar, bottom sheet, modals → sheets, pull-to-refresh | Medium — new layout | `app.js`, `app.more.js`, `app.css` |
| **4. Loading** | Skeletons for post / thread / profile, empty-state pass | Low | `app.js`, `app.more.js` |
| **5. Character** | Dither covers, 1px emboss pass, music toggle, call island morph | Medium — taste risk | `app.css`, `calls.js`, `app.more.js` |
| **6. Polish** | Shared-element image open, copy button, tooltips, accordion | Low | scattered |

After every phase: bump `?v=`, build, restart, regenerate demo screenshots, run CI.

---

## 8. The anti-slop checklist

A change is rejected if it does any of these. This list exists so the answer does not
depend on mood.

- [ ] Adds a second accent colour
- [ ] Adds a gradient to a content surface
- [ ] Makes a card translucent
- [ ] Uses type above 34px
- [ ] Animates something the user is reading
- [ ] Animates on scroll without a direct user action
- [ ] Exceeds 400ms for any interface animation
- [ ] Uses an easing curve not in the token set
- [ ] Adds a looping animation that is not status
- [ ] Puts an emoji in chrome
- [ ] Relies on colour alone for a state
- [ ] Adds a webfont
- [ ] Adds a new dependency
- [ ] Removes density to "make it breathe"
- [ ] Makes the phone version a shrunken desktop

And the positive test, which is harder but more useful:

> **Squint at it.** Can you still see three columns, a blue bar, and a rhythm of
> cards? If it turns into an even grey field, density was lost. If it turns into
> bright blocks, the accent budget was blown.

---

## 9. Performance budget

The reason the "no shader / no glass / no tilt" rules exist is not only taste.

| Budget | Value |
| --- | --- |
| Animated properties | `transform`, `opacity` only |
| Concurrent animations | ≤ 3 on screen; loops only for status |
| Longest animation | 400ms |
| Layout thrash | none — never animate `top/left/width/height` |
| `will-change` | only while an animation is about to run, then removed |
| JS per interaction | one rAF, no polling loops for visuals |
| Target | 60fps on a mid-range 2019 phone; usable on far less |
| Extra bytes | 0 — no new library, no webfont, no image assets beyond the stickers |

`prefers-reduced-motion` is a first-class path, not an afterthought: every new
primitive must degrade to an instant state change.

---

## 10. Open questions

Decisions that need the owner, not the implementer:

1. **Split `app.css`?** It is 2.1k lines in one file. Splitting needs `@import`
   (extra requests) or a build step (against the "no tooling" rule). Current lean:
   keep one file, but group it strictly by the section order in `DESIGN.md`. Note
   that `skins.css` is now a second file and that is deliberate: it is a *layer*, not
   a fragment, and it has to load after `brand.css`.
2. **A second accent for "social" moments** (reactions, gifts) or keep rationing to
   blue + red + gold? Current lean: ration. Gold already covers celebration. Note the
   skins each define their own accent, so this question is now *per skin* rather
   than global.
3. **Dither covers everywhere, or only as the fallback** when no image is uploaded?
   Current lean: fallback only — it should be a surprise, not a texture.
4. **Bottom bar destinations.** Feed / People / Messages / Notifications / More is the
   obvious set; the open question is whether Messages or Calls deserves the slot.
5. **Does the desktop get a "dock"?** It would be a strong retro move, but it competes
   with the sidebar. Current lean: no.
