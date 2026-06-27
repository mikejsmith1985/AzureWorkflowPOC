# Contract: Theming Design Tokens

Dark-first theme expressed as semantic CSS custom properties so a future light theme is additive with
no per-screen rework (FR-017/SC-005). Defined in `wwwroot/css/design-tokens.css`; mapped to semantic
Tailwind colour names via a runtime `tailwind.config` in `_Host.cshtml`.

## Token set (names are the contract; values are the dark palette)

| Token (CSS var) | Semantic role | Example Tailwind alias |
|-----------------|---------------|------------------------|
| `--bg` | App background | `bg-app` |
| `--surface` | Card/panel background | `bg-surface` |
| `--surface-raised` | Elevated surface (header, popover) | `bg-surface-raised` |
| `--border` | Hairline borders | `border-default` |
| `--text` | Primary text | `text-default` |
| `--text-muted` | Secondary/label text | `text-muted` |
| `--accent` | Primary accent (active nav, links, primary buttons) | `text-accent` / `bg-accent` |
| `--accent-contrast` | Text on accent | `text-on-accent` |
| `--status-ok` / `--status-warn` / `--status-error` | Status dots/badges | `text-status-*` |
| `--focus-ring` | Keyboard focus outline | (used in focus styles) |
| `--text-scale` | Root text-size multiplier (text-size control) | n/a (root style var) |

## Behavioral contract

- **C-THEME-1**: Screens reference **semantic** classes/variables, never raw palette values
  (`gray-950`, `cyan-400`); verified by absence of hard-coded theme colours in redesigned screens
  (SC-005).
- **C-THEME-2**: All token values for the dark theme live in one place (`design-tokens.css` `:root`);
  a future light theme adds a single overriding scope (e.g., `:root[data-theme="light"]`) — no screen
  edits required.
- **C-THEME-3**: The accent colour is applied consistently to active sidebar item, links, and primary
  buttons (FR-021).
- **C-THEME-4**: Status colours (ok/warn/error) drive the consistent status dots/badges and empty-state
  treatments (FR-021/FR-022).

## Acceptance

- Static check / review: redesigned screens use semantic token classes, not raw palette utilities.
- Visual: dark theme applied uniformly across sidebar, top bar, content, and rail (SC-005).
- (Light theme itself is out of scope; only token-structure is asserted here.)
