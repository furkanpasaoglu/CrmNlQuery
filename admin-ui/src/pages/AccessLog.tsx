import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button, Group, Select, Stack, Text, TextInput, UnstyledButton, Badge } from '@mantine/core'
import { IconRefresh, IconX } from '@tabler/icons-react'
import { ApiError, get } from '../api/client'
import { useI18n, useTOr } from '../i18n'
import { PageHeader } from '../components/PageHeader'
import { DataGrid, type GridColumn } from '../components/DataGrid'
import { Callout } from '../components/Callout'
import { StatusPill } from '../components/StatusPill'

type Row = { at: string; user: string; userName?: string; source: string; tables?: string[]; rows: number; outcome: string; requestId: string }

const SOURCE_KEYS: Record<string, string> = { ask: 'sourceAsk', result: 'sourceResult', lookup: 'sourceLookup', record_context: 'sourceRecordContext', summary: 'sourceSummary', draft: 'sourceDraft', count: 'sourceCount', other: 'sourceOther' }
const OUTCOME_KEYS: Record<string, string> = { ok: 'outcomeOk', crm_denied: 'outcomeCrmDenied', error: 'outcomeError', cancelled: 'outcomeCancelled' }
const PERIODS = ['1', '24', '168', '720', '2160', '8760']
const TAKE = 500

/** Renders the Access log admin page. */
export default function AccessLog() {
  const { t, locale } = useI18n()
  const tOr = useTOr()
  const [hours, setHours] = useState('24')
  const [tableInput, setTableInput] = useState('')
  const [table, setTable] = useState('')
  const [user, setUser] = useState<{ id: string; name: string } | null>(null)

  const q = useQuery({
    queryKey: ['access-log', hours, table, user?.id ?? ''],
    queryFn: () => {
      let url = `/api/admin/access-log?hours=${encodeURIComponent(hours)}&take=${TAKE}`
      if (user) url += '&user=' + encodeURIComponent(user.id)
      if (table.trim()) url += '&table=' + encodeURIComponent(table.trim())
      return get<Row[]>(url)
    },
    retry: false,
  })

  const sourceText = (c: string) => (SOURCE_KEYS[c] ? tOr('access-log.' + SOURCE_KEYS[c], c) : c)
  const outcomeText = (c: string) => (OUTCOME_KEYS[c] ? tOr('access-log.' + OUTCOME_KEYS[c], c) : c)
  const shortId = (id: string) => String(id).slice(0, 8)
  const nameOf = (r: Row) => r.userName || t('access-log.userFallback', { id: shortId(r.user) })
  const when = (iso: string) => new Date(iso).toLocaleString(locale)

  const columns: GridColumn<Row>[] = [
    { id: 'at', title: t('access-log.colTime'), value: (r) => r.at, render: (r) => <Text size="sm" style={{ whiteSpace: 'nowrap' }}>{when(r.at)}</Text>, sortable: true, filter: 'date', width: 190 },
    {
      id: 'user', title: t('access-log.colUser'), value: (r) => nameOf(r), sortable: true, filter: 'multi', mobile: 'title',
      render: (r) => (
        <UnstyledButton onClick={() => setUser({ id: r.user, name: nameOf(r) })} aria-label={t('access-log.userOnlyTitle')} title={t('access-log.userOnlyTitle')}>
          <Text size="sm" c="cobalt" td="underline" fw={500}>{nameOf(r)}</Text>
        </UnstyledButton>
      ),
    },
    { id: 'source', title: t('access-log.colSource'), value: (r) => sourceText(r.source), sortable: true, filter: 'multi' },
    { id: 'tables', title: t('access-log.colTables'), value: (r) => (r.tables ?? []).join(', '), filter: 'text' },
    { id: 'rows', title: t('access-log.colRows'), value: (r) => r.rows, sortable: true, align: 'right' },
    {
      id: 'outcome', title: t('access-log.colResult'), value: (r) => outcomeText(r.outcome), sortable: true, filter: 'multi',
      render: (r) => <StatusPill tone={r.outcome === 'ok' ? 'good' : r.outcome === 'cancelled' ? 'neutral' : r.outcome === 'crm_denied' ? 'warn' : 'bad'}>{outcomeText(r.outcome)}</StatusPill>,
    },
    { id: 'request', title: t('access-log.colRequest'), value: (r) => r.requestId, render: (r) => <Text size="sm" ff="monospace" c="dimmed">{shortId(r.requestId)}</Text>, filter: 'text', width: 120 },
  ]

  const data = q.data ?? []
  const count = data.length >= TAKE ? t('access-log.countCapped', { n: data.length }) : data.length === 1 ? t('access-log.countOne') : t('access-log.countMany', { n: data.length })

  return (
    <>
      <PageHeader
        title={t('access-log.title')}
        description={q.isSuccess ? count : undefined}
        actions={<Button variant="default" leftSection={<IconRefresh size={16} />} loading={q.isFetching} onClick={() => void q.refetch()}>{t('ui.refresh')}</Button>}
      />
      <Stack gap="md">
        <Callout kind="info">{t('access-log.intro')}</Callout>
        <Group align="flex-end" wrap="wrap" gap="sm" data-tour="al-filters">
          <Select
            label={t('access-log.periodLabel')}
            data={PERIODS.map((p) => ({ value: p, label: t('access-log.period' + p) }))}
            value={hours}
            onChange={(v) => setHours(v ?? '24')}
            allowDeselect={false}
            w={{ base: '100%', xs: 200 }}
          />
          <form
            style={{ display: 'contents' }}
            onSubmit={(e) => { e.preventDefault(); setTable(tableInput) }}
          >
            <TextInput label={t('access-log.tableLabel')} placeholder={t('access-log.tablePlaceholder')} maxLength={64} value={tableInput} onChange={(e) => setTableInput(e.currentTarget.value)} w={{ base: '100%', xs: 240 }} />
            <Button type="submit" variant="default">{t('access-log.show')}</Button>
          </form>
          {user && (
            <Badge size="lg" variant="light" tt="none" rightSection={<IconX size={12} />} style={{ cursor: 'pointer' }} role="button" tabIndex={0}
              title={t('access-log.userChipTitle')}
              onClick={() => setUser(null)}
              onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') setUser(null) }}>
              {t('access-log.userChip', { name: user.name }).replace(/\s*✕\s*$/, '')}
            </Badge>
          )}
        </Group>
        {q.isError ? (
          <Callout kind="bad">{q.error instanceof ApiError && q.error.status === 404 ? t('access-log.notAvailable') : (q.error as Error).message}</Callout>
        ) : (
          <DataGrid<Row>
            storageKey="access-log"
            exportName="access-log"
            records={data}
            loading={q.isLoading}
            getId={(r) => r.requestId + r.at + r.source + (r.tables ?? []).join(',')}
            columns={columns}
            emptyTitle={t('access-log.empty')}
          />
        )}
      </Stack>
    </>
  )
}
