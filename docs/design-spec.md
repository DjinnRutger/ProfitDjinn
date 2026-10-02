# ProfitDjinn 2.0 port spec: look and feel

Taken from `app/static/css/custom.css`, `app/static/js/sidebar.js` and
`app/templates/base.html` (1.0.0-beta, Bootstrap 5.3.3, Bootstrap Icons 1.11.3). Values
are at `ui_font_scale` 1.0. 1 rem = 16 px × scale; body text is 15 px × scale.

## Two primary colours

- **BrandPrimary** = the `primary_color` setting (default `#2563eb`). It drives:
  - primary buttons and their hover (`mix(P 82%, #000)`, default `#1e51c1`)
  - the sidebar background (`mix(P 22%, #04060f)`, default `#0b1a3f`)
  - the active nav background (P at 28% alpha) and its 3 px left bar
  - focus rings (border P plus 3 px of P at 18% alpha)
  - the avatar
  - chart bars on the Database page and the progress fill
- **BsPrimary** = `#0d6efd`, never configurable. It drives `text-primary`, `bg-primary`
  badges, `btn-outline-primary`, links, and checked checkboxes and switches.
- Mixes are per-channel lerps: `c = P·w + other·(1 − w)`.

## Tokens

| Token | Value |
|---|---|
| Sidebar width | 260, collapsed 70 |
| Top bar height | 64 |
| Radius | 8 (Bootstrap default elsewhere: 6) |
| Shadow XS | 0 1 2 rgba(0,0,0,.06) |
| Shadow SM | 0 1 3 rgba(0,0,0,.10) |
| Shadow MD | 0 4 6 rgba(0,0,0,.10) |
| Shadow LG | 0 10 15 rgba(0,0,0,.10) |
| Transition | 250 ms, cubic-bezier(.4, 0, .2, 1) |
| Font | Inter 400/500/600/700; Terminal uses VT323, letter-spacing .04em |

## Theme palettes

| Key | Light | Dark | Terminal |
|---|---|---|---|
| Page background | #ffffff | #0d1117 | #0d0208 |
| Text | #212529 | #e6edf3 | #00ff41 |
| Muted text | rgba(33,37,41,.75) | rgba(222,226,230,.75) | #008f11 |
| Headings | inherit | inherit | #39ff14, uppercase, glow |
| Surface (cards, top bar, dialogs) | #ffffff | #161b22 | #060106 |
| Secondary bg (card and table headers, hover) | #e9ecef | #343a40 | #060106 (card header #080d08) |
| Tertiary bg (input addons) | #f8f9fa | #2b3035 | #060106 |
| Border | #dee2e6 | #30363d (cards, bars); #495057 (inputs) | rgba(0,255,65,.35) |
| Input background / border | #fff / #dee2e6 | #212529 / #495057 | #030303 / rgba(0,255,65,.45) |
| Link / hover | #0d6efd / #0a58ca | #6ea8fe / #8bb9fe | #39ff14 / #00ff41 |
| Table row hover | rgba(0,0,0,.075) | rgba(255,255,255,.075) | rgba(0,255,65,.05), text #39ff14 |
| Totals row | rgba(0,0,0,.1) | rgba(255,255,255,.1) | rgba(255,255,255,.1) |
| Sidebar | brand mix | brand mix | #040804 with a 1 px green right border |
| Dialog backdrop | rgba(0,0,0,.5) | rgba(1,4,9,.7) | rgba(0,4,0,.85) |

Semantic colours, the same in every theme unless noted:
- success #198754, info #0dcaf0, warning #ffc107, danger #dc3545, secondary #6c757d
- Terminal adds red #ff4136 and gold #ffd700
- Status dots: active #22c55e, inactive #94a3b8

Terminal glow: `0 0 8px #00ff41, 0 0 20px rgba(0,255,65,.3)`. In WPF, a DropShadowEffect
with depth 0, blur about 12 and colour #00ff41.

## Shell

- **Sidebar**, dark in every theme:
  - Brand row:
    - 64 tall, padding 0 20, gap 12.
    - Text white, 700 weight, 16.8 px, letter-spacing −.02em.
    - Icon 22.4 px in #60a5fa (not brand-coloured), or the custom image 1.6 rem tall.
    - Bottom line rgba(255,255,255,.07).
  - Section label: 10.4 px, 700 weight, uppercase, letter-spacing .1em, #475569, padding
    13.6/20/5.6.
  - Nav link:
    - Padding 9.6 × 20 (about 40 tall), gap 11.2.
    - Text 14 px, 500 weight, #94a3b8. Icon 16.8 px in a 19.2 wide box.
    - Hover: text #e2e8f0, background rgba(255,255,255,.05).
    - Active: white text on the active background, with the 3 px brand bar on the left.
  - Divider: 1 px rgba(255,255,255,.07), margin 8 × 20.
  - Logout at the bottom: #64748b; hover #f87171 on rgba(239,68,68,.1).
  - Collapsed (70 wide):
    - Labels fade out; links centred, padding 9.6.
    - The main area's margin animates too (250 ms).
    - The state is remembered.
- **Nav:**
  - Dashboard `speedometer2`, Customers `building`, Invoices `receipt`, Work Orders
    `clipboard-check`, Revenue `graph-up-arrow`, Items `box-seam`.
  - Then, under "Administration": Settings `sliders`, Backup & Restore `database`.
  - Logout becomes Lock, `box-arrow-right`, shown only when the app password is on.
- **Top bar:**
  - 64 tall, padding 0 24, gap 12. Surface background, bottom border, Shadow XS.
  - Toggle button: 1 px border, radius 8, padding 4.8 × 8, `list` icon 20 px.
  - Breadcrumb: "Home / Invoices / INV-0001", "/" divider in muted text.
  - User button:
    - Ghost style.
    - 32 px brand avatar circle, white initials 11.2 px / 700. The 2.0 build shows the
      company initials.
    - `chevron-down` at .65 rem, opacity .6.
- **Content:**
  - Padding 28 × 24.
  - Each page enters over 180 ms: opacity .6→1, y 4→0.
  - Stat rows use 5 equal columns, with a 16 gutter. Detail pages are 8/4 columns with a
    24 gutter.
- **Page header:**
  - h1 21.6 px, 700 weight, letter-spacing −.025em.
  - Lead line 14 px muted.
  - Actions right-aligned, gap 8, bottom margin 24.
  - Detail pages put a large status pill (16 px, padding 8 × 16) next to the title.
- **Footer:** padding 13.6 × 24, top border, 12.8 px muted, `{footer_text} v{version}`.

## Components

- **Card:**
  - Radius 8, 1 px border, Shadow SM, body padding 16.
  - Header: 600 weight, 14.4 px, secondary bg, padding 13.6 × 20, bottom border.
  - Header contents: a leading coloured icon (margin 8), the title, and optionally a badge
    or a small outline-secondary button on the right.
  - Terminal: radius 0, glow 0 0 12 rgba(0,255,65,.12), header text glowing.
- **Stat tile:**
  - Padding 20 × 24, radius 8, Shadow SM.
  - Hover: Shadow MD and up 1 px.
  - Value: 32 px, 700 weight, letter-spacing −.04em, in the semantic colour, or muted when
    the value is zero.
  - Label: 12.8 px, 500 weight, uppercase, letter-spacing .05em, muted.
  - Icon box: 48×48, radius 12, the semantic colour at 10% alpha, icon 22.4 px.
  - Variants: a warning or danger border at 50% when a count is above zero; the revenue tile
    has a 3 px success bottom border.
- **Table:**
  - Text 14 px.
  - Header: 11.2 px, 700 weight, uppercase, letter-spacing .06em, muted on secondary bg,
    padding 12 × 16.
  - Rows: padding 12 × 16 (about 45 tall), bottom border, no zebra striping, hover overlay.
  - Conventions:
    - Money is right-aligned and semibold. Invoice numbers are monospace, semibold links.
    - Dates are muted at 13.6 px. A missing value is "—".
    - Status is a centred badge.
    - Actions are icon-only small outline buttons, gap 4.
  - Totals footer uses the totals-row colour; the grand total is 20 px bold.
- **Badge:**
  - 600 weight, 11.2 px, letter-spacing .02em, padding .35em/.65em, radius 6, white text.
  - Invoice: Paid success, Partial info, Unpaid warning.
  - Line: Billed success, Ready to Bill warning, No Charge info at 75%, Pending secondary.
  - Customer: Active success at 75%, Inactive secondary.
  - "$x ready": warning with dark text. Tags: secondary at 50%.
  - Terminal: transparent with a 1 px green border, green text, radius 0 (danger red).
- **Buttons:**
  - 500 weight, 14 px, radius 8, padding 6 × 12 (about 35 tall).
  - Small: padding 4.8 × 10.4 at 12.8 px (about 29 tall).
  - Icon plus text: icon margin 4.
  - Variants:
    - primary: brand, hover the brand hover colour
    - ghost: transparent with a border, hover secondary bg
    - success #198754 / hover #157347
    - secondary #6c757d / #5c636a
    - outline-*: hover fills
  - Terminal: radius 0. Primary is transparent with a green border; hover
    rgba(0,255,65,.15), bright text, glow.
- **Inputs:**
  - Label 12.8 px / 600, bottom margin 5.6.
  - Input 14 px, padding 6 × 12 (small 4 × 8), radius 8.
  - Focus: brand border plus a 3 px ring at 18%, 250 ms.
  - Addon: tertiary bg, radius 6.
  - Invalid: #dc3545 border (dark #ea868f) and 14 px red message text.
  - Required fields: a red `*`. Hint text: 14 px muted.
  - Colour setting: a 56×36 picker next to a monospace hex box.
  - Terminal: #030303 background, green border and text, radius 0; focus is a bright border
    plus glow.
- **Dialog:**
  - Radius 12, max width 480 (work dialog 560), shadow 0 8 40 rgba(0,0,0,.25).
  - Header and footer padding 16 × 24, each with a border.
  - Footer buttons right-aligned, gap 8: Cancel (outline-secondary), then the confirm
    button.
  - Backdrop click and Esc close it.
  - The settings dialog is 460 wide, radius 8, shadow 0 20 60 rgba(0,0,0,.35).
- **Alert:**
  - Inline at the top of the content, radius 8, Shadow SM, padding 16, X to dismiss.
  - Auto-dismisses after 5 s with a 150 ms fade.
  - Icons: success `check-circle-fill`, danger `exclamation-triangle-fill`, warning
    `exclamation-circle-fill`, info `info-circle-fill`.
  - Colours, background / text / border:
    - Light success #d1e7dd/#0a3622/#a3cfbb · danger #f8d7da/#58151c/#f1aeb5 · warning
      #fff3cd/#664d03/#ffe69c · info #cff4fc/#055160/#9eeaf9
    - Dark success #051b11/#75b798/#0f5132 · danger #2c0b0e/#ea868f/#842029 · warning
      #332701/#ffda6a/#997404 · info #032830/#6edff6/#087990
    - Terminal: radius 0, #060106, 3 px left border (green / #ff4136 / #ffd700 / dim)
- **Empty state:** padding 48 × 16, centred, muted, 40 px icon with 12 below, then the text
  and a link.
- **Theme preview tile:**
  - 2 px border, radius 8, padding 12. Hover: up 2 px plus Shadow MD.
  - Active: brand border plus ring rgba(37,99,235,.2).
  - Preview area 60 tall, radius 6, with a 30% left strip:
    - Light: #f8fafc, strip #0f172a
    - Dark: #0d1117, strip #0a0e14
    - Terminal: #0d0208, strip #040804, plus "> _" in VT323
- **Progress bar:** 10 tall pill on secondary bg, brand fill, 400 ms width animation.
- **Charts:**
  - Grid rgba(0,0,0,.07) light / rgba(255,255,255,.1) dark.
  - Tick labels #6b7280 / #adb5bd.
  - Bars rgba(28,52,88,.85).
  - Doughnut palette: #1c3458 #2563eb #0891b2 #059669 #d97706 #dc2626 #7c3aed #db2777
    #65a30d #0f766e #9333ea #f59e0b #10b981 #3b82f6 #ef4444.
  - Tooltip rgba(15,23,42,.92), text #e2e8f0, 12 px, radius 4.8.

## Terminal theme extras

- Scanlines: a full-window overlay that ignores input, repeating every 4 px (2 clear, 2
  rgba(0,0,0,.07)).
- Flicker: 8 s loop. Opacity 1 until 98%, then .97 / .93 / .98, back to 1.
- Uppercase glowing headings.
- Green active-nav bar with glow; nav text 16 px.
- Avatar: transparent with a 2 px green ring.
- Glow shadows instead of drop shadows. Radius 0 on cards, buttons, inputs, badges, alerts
  and dialogs.
- Lock screen text: "ROBCO INDUSTRIES (TM) TERMLINK PROTOCOL" above the card and a blinking
  "> ENTER PASSWORD" below it (1 s step blink).

## Motion

| What | Timing |
|---|---|
| Sidebar, nav colours, ghost buttons, focus, stat tile hover | 250 ms, the standard curve |
| Page enter | 180 ms ease |
| Button colours | 150 ms |
| Alert fade, after 5 s | 150 ms |
| Collapse (billed history) | 350 ms |
| Progress fill | 400 ms |
| Spinner | 750 ms per turn |
| Terminal cursor blink | 1 s step |

## Icons used

activity, archive, arrow-clockwise, arrow-counterclockwise, arrow-left, arrow-left-right,
arrow-right, arrow-up-circle-fill, arrows-collapse, bar-chart-fill, bar-chart-line, book,
box-arrow-in-right, box-arrow-right, box-arrow-up-right, box-seam, building, building-x,
calendar-x, calendar3, cash-coin, cash-stack, check-circle, check-circle-fill, check-lg,
check-square, check2-circle, check2-square, chevron-down, chevron-right, clipboard-check,
clock-history, cloud-download, cloud-fill, currency-dollar, database, database-fill-gear,
database-fill-x, download, exclamation-circle, exclamation-circle-fill,
exclamation-triangle, exclamation-triangle-fill, eye, eye-slash, file-earmark-pdf, floppy,
folder2-open, gauge, gear, graph-up-arrow, grid-1x2, grid-3x3, hdd, hourglass-split, house,
info-circle, info-circle-fill, journal-text, journal-x, key-fill, lightbulb,
lightning-charge-fill, list, list-ul, lock, palette, pencil, people, people-fill, person,
person-check, person-check-fill, person-circle, person-dash, person-gear, person-plus,
person-plus-fill, person-vcard, pie-chart, pie-chart-fill, plug, plus-circle, plus-lg,
printer, receipt, save, search, shield-check, shield-exclamation, shield-fill, shield-gear,
shield-lock, shield-plus, shield-plus-fill, shield-x, sliders, sliders-fill, slash-circle,
speedometer2, square, sticky, table, toggle-off, toggle-on, trash, trash3, trophy, type,
upload, wallet2, wifi, x, x-circle-fill, x-lg.

`app_icon` is user-configurable, so the full Bootstrap Icons set ships as geometry, not only
the list above.
