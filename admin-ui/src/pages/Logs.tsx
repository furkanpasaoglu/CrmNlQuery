import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button, Code, Drawer, Group, Select, Stack, Text, TextInput } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { IconRefresh, IconSearch } from '@tabler/icons-react'
import { ApiError, get } from '../api/client'
import { useI18n, useTOr } from '../i18n'
import { PageHeader } from '../components/PageHeader'
import { DataGrid, type GridColumn } from '../components/DataGrid'
import { Callout } from '../components/Callout'
import { StatusPill } from '../components/StatusPill'
import { SysCopyButton } from '../components/SysCopyButton'

type Entry = { at: string; level: string; message: string; exception: string | null; requestId: string | null }
type Row = Entry & { n: number }

/** Maps a log level to a badge tone of bad, warn or neutral. */
const tone = (level: string) => (level === 'Error' || level === 'Fatal' ? 'bad' : level === 'Warning' ? 'warn' : 'neutral')

/** Renders the admin Logs page with level, time range and text filters over the stored log entries. */
export default function Logs() {
  const { t, locale } = useI18n()
  const tOr = useTOr()
  const [level, setLevel] = useState('Warning')
  const [hours, setHours] = useState('24')
  const [text, setText] = useState('')
  const [term] = useDebouncedValue(text.trim(), 300)
  const [open, setOpen] = useState<Row | null>(null)

  const q = useQuery({
    queryKey: ['logs', level, hours, term],
    queryFn: () => get<Entry[]>(`/api/admin/logs?level=${encodeURIComponent(level)}&hours=${encodeURIComponent(hours)}&q=${encodeURIComponent(term)}&take=200`),
  })
  const rows = useMemo<Row[]>(() => (q.data ?? []).map((e, n) => ({ ...e, n })), [q.data])

  const levelText = (l: string) => tOr('logs.level' + l, l)
  const when = (iso: string) => new Date(iso).toLocaleString(locale)

  const columns: GridColumn<Row>[] = [
    { id: 'at', title: t('logs.colTime'), value: (r) => r.at, render: (r) => <Text size="sm" style={{ whiteSpace: 'nowrap' }}>{when(r.at)}</Text>, sortable: true, filter: 'date', width: 190, mobile: 'hide' },
    { id: 'level', title: t('logs.colLevel'), value: (r) => levelText(r.level), render: (r) => <StatusPill tone={tone(r.level)}>{levelText(r.level)}</StatusPill>, sortable: true, filter: 'multi', width: 130 },
    {
      id: 'message', title: t('logs.colMessage'), value: (r) => r.message, filter: 'text', mobile: 'title',
      render: (r) => (
        <div>
          <Text size="sm" lineClamp={2} style={{ overflowWrap: 'anywhere' }}>{r.message}</Text>
          {r.requestId && <Text size="xs" c="dimmed">{t('logs.request', { id: r.requestId })}</Text>}
        </div>
      ),
    },
    { id: 'when-mobile', title: t('logs.colTime'), value: (r) => when(r.at), hidden: true },
  ]
  const cols = columns.map((c) => (c.id === 'when-mobile' ? { ...c, hidden: false, mobile: undefined } : c))

  const detail = open
    ? [
        `${t('logs.colTime')}: ${open.at}`,
        `${t('logs.colLevel')}: ${open.level}`,
        open.requestId ? `${t('logs.detailRequest')}: ${open.requestId}` : '',
        '',
        open.message,
        '',
        open.exception ?? '',
      ].join('\n').trim()
    : ''

  const count = q.data?.length ?? 0

  return (
    <>
      <PageHeader
        title={t('logs.title')}
        description={t('logs.intro')}
        actions={<Button variant="default" leftSection={<IconRefresh size={16} />} loading={q.isFetching} onClick={() => void q.refetch()}>{t('logs.refresh')}</Button>}
      />
      <Stack gap="sm">
        <Group gap="sm" wrap="wrap" align="flex-end" data-tour="logs-filters">
          <Select
            label={t('logs.minLevel')} allowDeselect={false} w={{ base: '100%', xs: 220 }} value={level} onChange={(v) => v && setLevel(v)}
            data={[{ value: 'Error', label: t('logs.optErrors') }, { value: 'Warning', label: t('logs.optWarnings') }, { value: 'Information', label: t('logs.optEverything') }]}
          />
          <Select
            label={t('logs.timeRange')} allowDeselect={false} w={{ base: '100%', xs: 180 }} value={hours} onChange={(v) => v && setHours(v)}
            data={[['1', 'logs.range1h'], ['24', 'logs.range24h'], ['168', 'logs.range7d'], ['720', 'logs.range30d'], ['2160', 'logs.range90d']].map(([value, k]) => ({ value, label: t(k) }))}
          />
          <TextInput
            label={t('logs.searchLabel')} placeholder={t('logs.searchPlaceholder')} leftSection={<IconSearch size={16} />}
            value={text} onChange={(e) => setText(e.currentTarget.value)} style={{ flex: '1 1 240px' }}
          />
        </Group>

        {q.isError ? (
          <Callout kind="bad" action={<Button mt="xs" size="xs" variant="default" onClick={() => void q.refetch()}>{t('ui.retry')}</Button>}>
            {q.error instanceof ApiError
              ? (q.error.status === 404 ? t('logs.notAvailable') : t('logs.loadFailed', { status: q.error.status }))
              : t('logs.unreachable', { msg: (q.error as Error).message })}
          </Callout>
        ) : (
          <>
            {count > 0 && <Text size="sm" c="dimmed">{count >= 200 ? t('logs.showingLatest', { count }) : t('logs.entries', { count })}</Text>}
            <DataGrid<Row>
              storageKey="logs"
              exportName="logs"
              records={rows}
              loading={q.isLoading}
              getId={(r) => String(r.n)}
              columns={cols}
              emptyTitle={t('logs.noMatch')}
              onRowClick={setOpen}
            />
          </>
        )}
      </Stack>

      <Drawer opened={open !== null} onClose={() => setOpen(null)} position="right" size="lg" title={t('logs.detailTitle')}>
        {open && (
          <Stack gap="md">
            <Group gap="sm" wrap="wrap">
              <StatusPill tone={tone(open.level)}>{levelText(open.level)}</StatusPill>
              <Text size="sm" c="dimmed">{when(open.at)}</Text>
            </Group>
            {open.requestId && (
              <div>
                <Text size="sm" c="dimmed">{t('logs.detailRequest')}</Text>
                <Text ff="monospace" size="sm" style={{ overflowWrap: 'anywhere' }}>{open.requestId}</Text>
              </div>
            )}
            <div>
              <Text size="sm" c="dimmed" mb={2}>{t('logs.colMessage')}</Text>
              <Text size="sm" style={{ overflowWrap: 'anywhere' }}>{open.message}</Text>
            </div>
            <div>
              <Text size="sm" c="dimmed" mb={2}>{t('logs.detailException')}</Text>
              {open.exception
                ? <Code block style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere', maxHeight: '50vh', overflow: 'auto' }}>{open.exception}</Code>
                : <Text size="sm" c="dimmed">{t('logs.detailNone')}</Text>}
            </div>
            <Group><SysCopyButton value={detail} /></Group>
          </Stack>
        )}
      </Drawer>
    </>
  )
}
