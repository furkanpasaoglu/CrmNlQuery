# admin-ui: administration panel (React + Vite + Mantine)

The administration panel is served at `/admin-next/` (`/admin` and `/admin.html` redirect there). It talks to the `/api/admin/...` endpoints.

## Checks

`node tests/widget/admin-ui-i18n.test.js` checks catalog parity, that every key used in code exists, and that every setting, permission and export column has a translation. `node tests/widget/ui-text.test.js` caps the amount of hard-coded user-facing text.

## Commands

```powershell
cd admin-ui
npm install          # first time
npm run dev          # http://127.0.0.1:5173, proxies /api to the local service on http://127.0.0.1:5080
npx tsc -b           # type check: must be clean after every change
npm run build        # writes static files to ../src/CrmNlQuery.Web/wwwroot/admin-next/
```

## Layout

```
src/
  main.tsx            entry: Mantine, React Query, i18n, modals, router
  theme.ts            design principles and the color, size and radius values (pages never hard-code colors)
  styles.css          fonts, page background, sidebar
  nav.tsx             menu groups and permissions. The page file is found by NAME: id "data-access" -> src/pages/DataAccess.tsx (default export)
  layout/AppLayout    sidebar, top bar, search (Ctrl+K), theme, language, user menu; drawer on mobile
  api/client.ts       get/post/put/del + ApiError(status, code, message, hasMessage)
  auth/me.tsx         useMe(): { me, can(permission), refresh }
  i18n/               en.json, tr.json + extra/<name>.en.json, <name>.tr.json (page-specific keys)
  components/         shared components (below)
  pages/              one file per screen
```

## Adding a page

1. Write `src/pages/<PascalCaseId>.tsx` (for example `DataAccess.tsx`) with a default export.
2. Put new text keys in `src/i18n/extra/<name>.en.json` and `<name>.tr.json`: both languages together, same keys, same `{parameters}`. Reuse existing keys first (search `en.json`, for example `"users.title"`) and add new text only when needed.
3. Do not edit shared files such as `nav.tsx`, `main.tsx` or `AppLayout.tsx` for a normal page.

## Shared components (`src/components/`)

| Component | Purpose |
|---|---|
| `PageHeader` | Page title: `title`, a one-sentence `description`, optional collapsed `howTitle` + `how`, and `actions` on the right (one primary button per page) |
| `KpiCard` | Number card: `label`, `value`, `hint`, `tone` (good/warn/bad), `icon` |
| `SectionCard` | Titled section card (`flush`: edge-to-edge content, for tables) |
| `DataGrid<T>` | Table with search, column filters (text/multi/date), sorting, paging, column visibility, row height, CSV export, remembered preferences, and a card list on mobile. Columns are `GridColumn<T>`: `id, title, value(row), render?, sortable?, filter?, width?, hidden?, mobile?` |
| `Callout` | Info / warning / error strip (`kind="info" | "warn" | "bad"`) |
| `EmptyState` | Empty state: title, one sentence, one action |
| `StatusPill` | Status badge (dot and text) |
| `HealthPipeline` | Dashboard only |
| `confirmAction()` | Confirmation dialog for irreversible actions |

Mantine components can be used directly (Drawer, Modal, Tabs, Switch, Select, TextInput, Textarea, Checkbox, Accordion, Stepper, ...). Use `@mantine/form` for forms, `notifications.show` for notifications and `@tanstack/react-query` for data.

## Data patterns

```tsx
const q = useQuery({ queryKey: ['users'], queryFn: () => get<User[]>('/api/admin/users') })
const m = useMutation({
  mutationFn: (body: X) => post('/api/admin/users', body),
  onSuccess: () => { notifications.show({ color: 'teal', message: t('users.added') }); void qc.invalidateQueries({ queryKey: ['users'] }) },
  onError: (e) => notifications.show({ color: 'red', message: errorText(e) }),
})
```

Server errors are `ApiError`: `status`, `code` (for example `"duplicate"`, `"confirm_required"`) and `message` (the server's own text; show it when `hasMessage` is true). Codes are translated in the UI (`t('glossary.reason_' + code)`); an unknown code is shown raw (`useTOr()`).

Permissions: `const { can } = useMe(); can('Users.Manage')`. The server enforces permissions anyway; the UI only hides or disables buttons.

## Design rules

- **Easy to understand.** Every page has one title, one short sentence (`description`) and the main task visible at first glance. Longer explanations sit behind "How it works" (`how`). Show plain names rather than technical keys, ids or code; technical values appear only in a tooltip or an advanced section.
- **One primary button per page** (filled); secondary buttons are outlined (`variant="default"`); dangerous actions are red and use a confirmation dialog (`confirmAction`).
- **Edit and add forms open in a right-hand `Drawer`** (or a `Modal` for short forms) so the list stays in place. Save is `ui.save`, cancel is `ui.cancel`; show a notification on success and the reason under the field or in a `Callout` on failure.
- **Status is never color only:** badges always carry text (`StatusPill`).
- **Sentence case.** No UPPERCASE labels, no dot-joined info lines such as "A · B · C", no small eyebrow label above every heading. Put several facts on separate lines or in separate fields.
- An action keeps its name through the flow (a "Delete" button leads to a "Deleted" notification). An error message says what happened and what to do; it does not apologize and is never vague. An empty screen invites an action.
- Rounding is a hierarchy: controls 6 px, cards 10 px, badges fully round; do not give everything the same shadow and radius. No gradients. Colors and sizes come from `theme.ts`; pages do not hard-code hex values or pixel sizes (use a Mantine color variable such as `var(--mantine-color-red-6)` when needed).
- **Mobile friendly:** grids use `SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }}`, button groups wrap, tables use `DataGrid` (cards on mobile) or `Table.ScrollContainer`. There must be no page-level horizontal scrolling at 390 px. Touch targets are at least 34 px.
- Accessibility: every icon button has an `aria-label`; form fields have a `label`; everything works with the keyboard; `prefers-reduced-motion` is respected globally.
- Loading: `Skeleton` or `Loader`; error: `Callout kind="bad"` with retry; empty: `EmptyState`.

## Security and CSP (do not break)

The server's Content-Security-Policy is `script-src 'self'; style-src 'self' 'nonce-…'` (no inline scripts).

- Do not use `dangerouslySetInnerHTML`, `eval`, `new Function`, inline `<script>`, hand-written `<style>` tags, or HTML strings containing a `style` attribute. The React `style={{...}}` prop is fine.
- Render every value that comes from the server as text only (`{value}` in JSX); wrap values embedded in a URL with `encodeURIComponent`.
- No requests to other origins and no CDN; packages come from npm.
- Never print passwords or keys, log them to the console, or store them in `localStorage`.
- While developing against a live service, use only GET requests; do not change shared data with POST, PUT or DELETE.
