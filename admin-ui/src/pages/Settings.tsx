import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Card, Group, NavLink, Select, Skeleton, Stack, Switch, Text, TextInput, Tooltip } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import { IconRotate, IconSearch } from '@tabler/icons-react'
import { ApiError, del, get, put } from '../api/client'
import { useI18n, useTOr } from '../i18n'
import { PageHeader } from '../components/PageHeader'
import { SectionCard } from '../components/SectionCard'
import { Callout } from '../components/Callout'
import { StatusPill } from '../components/StatusPill'
import { EmptyState } from '../components/EmptyState'
import { confirmAction } from '../components/confirm'

type Item = {
  key: string
  description: string
  kind: string
  min: number
  max: number
  choices: string[] | null
  value: string
  source: string
  default: string
}
type Group_ = { name: string; items: Item[] }
type State = { askEnabled: boolean; groups: Group_[] }

const ASK_KEY = 'Limits:AskEnabled'
const GROUP_ORDER = ['Features', 'Access', 'Capacity', 'Planning', 'Content', 'Conversation', 'Knowledge', 'Retention', 'Interface']

/** Returns the message text of an error, or its string form for non-errors. */
const errorText = (e: unknown) => (e instanceof Error ? e.message : String(e))
/** Returns the first sentence of a description, shown in the row, with the rest behind a More link. */
function splitFirst(text: string): [string, string] {
  const m = /^(.*?[.!?])\s+([\s\S]+)$/.exec(text)
  return m ? [m[1], m[2]] : [text, '']
}

/** Renders the Settings admin page with grouped runtime settings, search and the pause switch. */
export default function Settings() {
  const { t } = useI18n()
  const tOr = useTOr()
  const qc = useQueryClient()
  const mobile = useMediaQuery('(max-width: 48em)')
  const [group, setGroup] = useState<string | null>(null)
  const [search, setSearch] = useState('')

  const q = useQuery({ queryKey: ['settings'], queryFn: () => get<State>('/api/admin/settings') })

  const save = useMutation({
    mutationFn: (a: { key: string; value: string; message: string }) => put(`/api/admin/settings?key=${encodeURIComponent(a.key)}`, { value: a.value }),
    onSuccess: (_d, a) => { notifications.show({ color: 'teal', message: a.message }); void qc.invalidateQueries({ queryKey: ['settings'] }) },
    onError: (e) => { notifications.show({ color: 'red', title: t('settings.saveFailed'), message: errorText(e) }); void qc.invalidateQueries({ queryKey: ['settings'] }) },
  })
  const reset = useMutation({
    mutationFn: (key: string) => del(`/api/admin/settings?key=${encodeURIComponent(key)}`),
    onSuccess: (_d, key) => { notifications.show({ color: 'teal', message: t('settings.resetDone', { key }) }); void qc.invalidateQueries({ queryKey: ['settings'] }) },
    onError: (e) => notifications.show({ color: 'red', message: errorText(e) }),
  })

  const groups = useMemo(() => {
    const list = (q.data?.groups ?? [])
      .map((g) => ({ name: g.name, items: g.items.filter((i) => i.key !== ASK_KEY) }))
      .filter((g) => g.items.length > 0)
    list.sort((a, b) => {
      const ia = GROUP_ORDER.indexOf(a.name), ib = GROUP_ORDER.indexOf(b.name)
      return (ia < 0 ? 99 : ia) - (ib < 0 ? 99 : ib)
    })
    return list
  }, [q.data])

  const describe = (i: Item) => tOr('setting.' + i.key, i.description)
  const groupName = (n: string) => tOr('settings.group' + n, n)
  const active = groups.find((g) => g.name === group) ?? groups[0]
  const term = search.trim().toLowerCase()
  const hits = term
    ? groups.flatMap((g) => g.items.filter((i) => describe(i).toLowerCase().includes(term) || i.key.toLowerCase().includes(term)).map((i) => ({ group: g.name, item: i })))
    : []

  const onSave = (item: Item, value: string) => save.mutate({ key: item.key, value, message: t('settings.saved', { key: item.key }) })
  const busy = save.isPending || reset.isPending

  /** Handles the pause-asking toggle with a two-step confirmation. */
  function pause() {
    confirmAction({
      title: t('settings.pause'), message: t('settings.pauseConfirm'), confirmLabel: t('settings.pause'), cancelLabel: t('ui.cancel'), danger: true,
      onConfirm: () => save.mutateAsync({ key: ASK_KEY, value: 'false', message: t('settings.pausedDone') }).then(() => undefined, () => undefined),
    })
  }

  if (q.isError) {
    return (
      <>
        <PageHeader title={t('settings.title')} />
        <Callout kind="bad" action={<Button mt="xs" size="xs" variant="default" onClick={() => void q.refetch()}>{t('ui.retry')}</Button>}>
          {q.error instanceof ApiError && q.error.status === 404 ? t('settings.notAvailable') : errorText(q.error)}
        </Callout>
      </>
    )
  }

  const askOn = q.data?.askEnabled ?? true

  const list = term ? (
    <Stack gap="md">
      <Text fw={600}>{t('settings.results', { n: hits.length, q: search.trim() })}</Text>
      {hits.length === 0 && <EmptyState title={t('settings.noResults')} />}
      {hits.length > 0 && (
        <Card padding={0} className="grid-card">
          {hits.map((h) => (
            <SettingRow key={h.item.key} item={h.item} text={describe(h.item)} groupLabel={groupName(h.group)} busy={busy} onSave={onSave} onReset={(k) => reset.mutate(k)} />
          ))}
        </Card>
      )}
    </Stack>
  ) : active ? (
    <Card padding={0} className="grid-card" data-tour="set-list">
      <Stack gap={2} p="md" pb="sm">
        <Text fw={600}>{groupName(active.name)}</Text>
        <Text size="sm" c="dimmed">{tOr('settings.groupDesc' + active.name, '')}</Text>
      </Stack>
      {active.items.map((item) => (
        <SettingRow key={item.key} item={item} text={describe(item)} busy={busy} onSave={onSave} onReset={(k) => reset.mutate(k)} />
      ))}
    </Card>
  ) : (
    <Text c="dimmed">{t('settings.pickGroup')}</Text>
  )

  return (
    <>
      <PageHeader title={t('settings.title')} description={t('settings.sub')} />

      {q.isLoading ? (
        <Stack><Skeleton h={90} radius="md" /><Skeleton h={300} radius="md" /></Stack>
      ) : (
        <Stack gap="md">
          <SectionCard
            title={t('settings.askingQuestions')}
            description={askOn ? t('settings.askOnText') : t('settings.askPausedText')}
            right={
              <Group gap="sm" wrap="wrap" justify="flex-end">
                <StatusPill tone={askOn ? 'good' : 'bad'}>{askOn ? t('settings.on') : t('settings.paused')}</StatusPill>
                {askOn
                  ? <Button data-tour="set-pause" color="red" variant="light" loading={busy} onClick={pause}>{t('settings.pause')}</Button>
                  : <Button loading={busy} onClick={() => save.mutate({ key: ASK_KEY, value: 'true', message: t('settings.resumed') })}>{t('settings.resume')}</Button>}
              </Group>
            }
          />

          <TextInput
            wrapperProps={{ 'data-tour': 'set-search' }}
            placeholder={t('settings.searchPlaceholder')} aria-label={t('settings.searchPlaceholder')}
            leftSection={<IconSearch size={16} />} value={search} onChange={(e) => setSearch(e.currentTarget.value)}
          />

          {mobile ? (
            <Stack gap="md">
              {!term && (
                <Select
                  aria-label={t('settings.groupsAria')} allowDeselect={false}
                  data={groups.map((g) => ({ value: g.name, label: groupName(g.name) }))}
                  value={active?.name ?? null} onChange={(v) => setGroup(v)}
                />
              )}
              {list}
            </Stack>
          ) : (
            <Group align="flex-start" gap="lg" wrap="nowrap">
              <Card padding="xs" w={240} style={{ flex: 'none' }} className="grid-card" data-tour="set-groups">
                <nav aria-label={t('settings.groupsAria')}>
                  {groups.map((g) => {
                    const changed = g.items.filter((i) => i.source === 'panel').length
                    return (
                      <NavLink
                        key={g.name} label={groupName(g.name)} description={t('settings.count', { n: g.items.length })}
                        active={!term && g.name === active?.name} onClick={() => { setGroup(g.name); setSearch('') }}
                        rightSection={changed > 0 ? <Tooltip label={t('settings.changedCount', { n: changed })}><Badge size="sm" circle variant="light">{changed}</Badge></Tooltip> : null}
                        style={{ borderRadius: 'var(--mantine-radius-sm)' }}
                      />
                    )
                  })}
                </nav>
              </Card>
              <div style={{ flex: 1, minWidth: 0 }}>{list}</div>
            </Group>
          )}
        </Stack>
      )}
    </>
  )
}

/** Renders one setting row with a short description and technical name on the left and the input on the right. */
function SettingRow({ item, text, groupLabel, busy, onSave, onReset }: {
  item: Item; text: string; groupLabel?: string; busy: boolean
  onSave: (item: Item, value: string) => void
  onReset: (key: string) => void
}) {
  const { t } = useI18n()
  const [more, setMore] = useState(false)
  const [draft, setDraft] = useState(item.value)
  const [first, rest] = splitFirst(text)
  const changed = item.source === 'panel'

  const [seen, setSeen] = useState(item.value)
  if (seen !== item.value) { setSeen(item.value); setDraft(item.value) }

  const isNumber = item.kind === 'integer' || item.kind === 'decimal'
  const clean = draft.trim()
  const num = item.kind === 'decimal' ? Number(clean.replace(',', '.')) : Number(clean)
  const valid = !isNumber || (clean !== '' && Number.isFinite(num) && (item.kind === 'decimal' || Number.isInteger(num)) && num >= item.min && num <= item.max)
  const dirty = clean !== item.value
  const sourceText = item.source === 'panel' ? t('settings.sourcePanel') : item.source === 'file' ? t('settings.sourceFile') : t('settings.sourceDefault')

  let control
  if (item.kind === 'boolean') {
    control = (
      <Switch
        checked={item.value === 'true'} disabled={busy} aria-label={item.key}
        onLabel={t('settings.on')} offLabel={t('settings.off')} size="md"
        onChange={(e) => onSave(item, e.currentTarget.checked ? 'true' : 'false')}
      />
    )
  } else if (item.kind === 'choice') {
    control = (
      <Group gap="xs" wrap="nowrap">
        <Select aria-label={item.key} w={170} allowDeselect={false} data={item.choices ?? []} value={draft} onChange={(v) => { if (v) setDraft(v) }} />
        <Button disabled={!dirty || busy} onClick={() => onSave(item, clean)}>{t('settings.save')}</Button>
      </Group>
    )
  } else {
    control = (
      <Group gap="xs" wrap="nowrap" align="flex-start">
        <TextInput
          aria-label={item.key} w={item.kind === 'text' ? 280 : 120} inputMode={item.kind === 'text' ? undefined : item.kind === 'decimal' ? 'decimal' : 'numeric'}
          value={draft} onChange={(e) => setDraft(e.currentTarget.value)}
          error={!valid && dirty ? t('settings.allowed', { min: item.min, max: item.max }) : undefined}
          description={item.kind === 'text' ? undefined : valid || !dirty ? `${item.min} – ${item.max}` : undefined}
        />
        <Button disabled={!dirty || !valid || busy} onClick={() => onSave(item, item.kind === 'decimal' ? clean.replace(',', '.') : clean)}>{t('settings.save')}</Button>
      </Group>
    )
  }

  return (
    <Group justify="space-between" align="flex-start" gap="md" p="md" wrap="wrap" style={{ borderTop: '1px solid var(--mantine-color-default-border)' }}>
      <Stack gap={4} style={{ flex: '1 1 280px', minWidth: 0 }}>
        <Group gap={8} wrap="wrap">
          {changed && (
            <Tooltip label={sourceText}>
              <span role="img" aria-label={sourceText} style={{ width: 8, height: 8, borderRadius: '50%', background: 'var(--mantine-primary-color-filled)', display: 'inline-block' }} />
            </Tooltip>
          )}
          {groupLabel && <Badge variant="light" size="sm" tt="none">{groupLabel}</Badge>}
          <Text size="sm" fw={500} style={{ overflowWrap: 'anywhere' }}>{first}</Text>
        </Group>
        {rest && (
          <>
            {more && <Text size="sm" c="dimmed" className="readable">{rest}</Text>}
            <Text
              component="button" type="button" size="xs" c="cobalt" onClick={() => setMore((m) => !m)} aria-expanded={more}
              style={{ background: 'none', border: 0, padding: 0, cursor: 'pointer', width: 'fit-content' }}
            >
              {more ? t('settings.less') : t('ui.more')}
            </Text>
          </>
        )}
        <Group gap={8} wrap="wrap">
          <Text size="xs" c="dimmed" ff="monospace" style={{ overflowWrap: 'anywhere' }}>{item.key}</Text>
          {item.source !== 'default' && <Text size="xs" c="dimmed">{sourceText}</Text>}
          {item.source !== 'default' && <Text size="xs" c="dimmed">{t('settings.defaultValue', { value: item.default })}</Text>}
          {changed && (
            <Button size="compact-xs" variant="subtle" leftSection={<IconRotate size={12} />} title={t('settings.resetTitle')} disabled={busy} onClick={() => onReset(item.key)}>
              {t('settings.reset')}
            </Button>
          )}
        </Group>
      </Stack>
      <div style={{ flex: '0 0 auto', maxWidth: '100%' }}>{control}</div>
    </Group>
  )
}
