import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button, Text, Tooltip } from '@mantine/core'
import { IconRefresh } from '@tabler/icons-react'
import { ApiError, get } from '../api/client'
import { useI18n, useTOr } from '../i18n'
import { PageHeader } from '../components/PageHeader'
import { DataGrid, type GridColumn } from '../components/DataGrid'
import { Callout } from '../components/Callout'
import { SegmentedControl } from '@mantine/core'

type Entry = { at: string; actor: string; action: string; target: string; detail: string }

const GUID = /^[0-9a-f]{32}$|^[0-9a-f]{8}-([0-9a-f]{4}-){3}[0-9a-f]{12}$/i

/** Renders the Audit trail admin page listing management changes. */
export default function AuditTrail() {
  const { t, locale } = useI18n()
  const tOr = useTOr()
  const [take, setTake] = useState('200')
  const q = useQuery({ queryKey: ['audit', take], queryFn: () => get<Entry[]>(`/api/admin/audit?take=${take}`) })

  const label = (code: string) => tOr('audit.action.' + code, code)
  const when = (iso: string) => new Date(iso).toLocaleString(locale)

  const columns: GridColumn<Entry>[] = [
    { id: 'at', title: t('audit.colWhen'), value: (r) => r.at, render: (r) => <Text size="sm" style={{ whiteSpace: 'nowrap' }}>{when(r.at)}</Text>, sortable: true, filter: 'date', width: 190, mobile: 'hide' },
    { id: 'what', title: t('audit.colWhat'), value: (r) => label(r.action), sortable: true, filter: 'multi', mobile: 'title' },
    { id: 'who', title: t('audit.colWho'), value: (r) => r.actor, sortable: true, filter: 'multi', width: 200 },
    {
      id: 'target', title: t('audit.colTarget'), value: (r) => r.target, filter: 'text',
      render: (r) => GUID.test(r.target)
        ? <Tooltip label={r.target}><Text size="sm" ff="monospace" c="dimmed">{r.target.slice(0, 8)}…</Text></Tooltip>
        : <Text size="sm">{r.target}</Text>,
    },
    { id: 'detail', title: t('audit.colDetails'), value: (r) => r.detail, filter: 'text' },
    { id: 'when-mobile', title: t('audit.colWhen'), value: (r) => when(r.at), hidden: true },
  ]
  const cols = columns.map((c) => (c.id === 'when-mobile' ? { ...c, hidden: false, mobile: undefined } : c))

  return (
    <>
      <PageHeader
        title={t('audit.title')}
        description={t('audit.sub')}
        actions={
          <>
            <SegmentedControl size="sm" value={take} onChange={setTake} data={[{ value: '100', label: '100' }, { value: '200', label: '200' }, { value: '500', label: '500' }]} aria-label={t('audit.howMany')} />
            <Button variant="default" leftSection={<IconRefresh size={16} />} loading={q.isFetching} onClick={() => void q.refetch()}>{t('ui.refresh')}</Button>
          </>
        }
      />
      {q.isError ? (
        <Callout kind="bad">{q.error instanceof ApiError && q.error.status === 404 ? t('audit.notAvailable') : (q.error as Error).message}</Callout>
      ) : (
        <DataGrid<Entry>
          storageKey="audit"
          exportName="audit-trail"
          records={q.data ?? []}
          loading={q.isLoading}
          getId={(r) => r.at + r.actor + r.action + r.target}
          columns={cols}
          emptyTitle={t('audit.empty')}
        />
      )}
    </>
  )
}
