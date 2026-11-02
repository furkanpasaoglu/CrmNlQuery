import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ActionIcon, Button, Drawer, Group, ScrollArea, SegmentedControl, Skeleton, Stack, Switch, Text, Tooltip } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import { IconLock, IconRefresh, IconShieldCheck } from '@tabler/icons-react'
import { ApiError, get, post, put } from '../api/client'
import { useI18n, useTOr } from '../i18n'
import { PageHeader } from '../components/PageHeader'
import { SectionCard } from '../components/SectionCard'
import { DataGrid, type GridColumn } from '../components/DataGrid'
import { Callout } from '../components/Callout'
import { StatusPill } from '../components/StatusPill'

type Table = { entity: string; label: string; open: boolean; source: string; autoClosedFields: number | null; adminClosedFields: number | null }
type TablesResponse = { enabled: boolean; persistent: boolean; decisions: number; defaultTables: string[]; tables: Table[] }
type Field = { attribute: string; label: string; type: string; open: boolean; state: string; locked: boolean }
type ReviewRow = { entity: string; entityLabel: string; attribute: string; label: string }
type CheckResult = { outcome: string; message: string }
type Tab = 'open' | 'closed' | 'all'

/** Turns an unknown error into a readable message. */
const errText = (e: unknown) => (e instanceof ApiError ? e.message : e instanceof Error ? e.message : String(e))
/** Builds the admin API path for a table's field policy. */
const fieldPath = (entity: string, attribute: string) => `/api/admin/data-policy/tables/${encodeURIComponent(entity)}/fields/${encodeURIComponent(attribute)}`

/** Renders the Data access admin page where admins open or close tables and fields for questions. */
export default function DataAccess() {
  const { t } = useI18n()
  const tOr = useTOr()
  const qc = useQueryClient()
  const mobile = useMediaQuery('(max-width: 48em)')

  const [tab, setTab] = useState<Tab | null>(null)
  const [selected, setSelected] = useState<string | null>(null)
  const [pendingTables, setPendingTables] = useState<Record<string, boolean>>({})
  const [pendingFields, setPendingFields] = useState<Record<string, boolean>>({})
  const [checks, setChecks] = useState<Record<string, CheckResult>>({})

  const tablesQ = useQuery({ queryKey: ['dp-tables'], queryFn: () => get<TablesResponse>('/api/admin/data-policy/tables') })
  const reviewQ = useQuery({ queryKey: ['dp-review'], queryFn: () => get<ReviewRow[]>('/api/admin/data-policy/review'), enabled: tablesQ.isSuccess })

  const tables = (tablesQ.data?.tables ?? []).map((x) => (x.entity in pendingTables ? { ...x, open: pendingTables[x.entity] } : x))
  const openCount = tables.filter((x) => x.open).length
  const counts = { open: openCount, closed: tables.length - openCount, all: tables.length }
  const activeTab: Tab = tab ?? (openCount > 0 ? 'open' : 'all')
  const shown = tables.filter((x) => activeTab === 'all' || (activeTab === 'open' ? x.open : !x.open))
  const current = tables.find((x) => x.entity === selected) ?? null

  const fieldsQ = useQuery({
    queryKey: ['dp-fields', selected],
    queryFn: () => get<Field[]>(`/api/admin/data-policy/tables/${encodeURIComponent(selected as string)}/fields`),
    enabled: Boolean(current?.open),
  })

  /** Reloads the table and field data of the page. */
  function refreshAll() {
    void qc.invalidateQueries({ queryKey: ['dp-tables'] })
    void qc.invalidateQueries({ queryKey: ['dp-review'] })
    void qc.invalidateQueries({ queryKey: ['dp-fields'] })
  }
  const fail = (e: unknown) => notifications.show({ color: 'red', message: errText(e), autoClose: 9000 })

  const tableMut = useMutation({
    mutationFn: (v: { table: Table; allowed: boolean | null }) => put(`/api/admin/data-policy/tables/${encodeURIComponent(v.table.entity)}`, { allowed: v.allowed }),
    onMutate: (v) => { if (v.allowed !== null) setPendingTables((p) => ({ ...p, [v.table.entity]: v.allowed as boolean })) },
    onSuccess: async (_d, v) => {
      const key = v.allowed === null ? 'data-access.backToDefault' : v.allowed ? 'data-access.nowOpen' : 'data-access.nowClosed'
      notifications.show({ color: 'teal', message: t(key, { name: v.table.label }) })
      await qc.refetchQueries({ queryKey: ['dp-tables'] })
      refreshAll()
    },
    onError: fail,
    onSettled: (_d, _e, v) => setPendingTables((p) => { const n = { ...p }; delete n[v.table.entity]; return n }),
  })

  const fieldMut = useMutation({
    mutationFn: (v: { entity: string; field: { attribute: string; label: string }; allowed: boolean | null }) => put(fieldPath(v.entity, v.field.attribute), { allowed: v.allowed }),
    onMutate: (v) => { if (v.allowed !== null) setPendingFields((p) => ({ ...p, [v.entity + '.' + v.field.attribute]: v.allowed as boolean })) },
    onSuccess: async (_d, v) => {
      const key = v.allowed === null ? 'data-access.backToDefault' : v.allowed ? 'data-access.nowOpen' : 'data-access.nowClosed'
      notifications.show({ color: 'teal', message: t(key, { name: v.field.label }) })
      await Promise.all([qc.refetchQueries({ queryKey: ['dp-fields'] }), qc.refetchQueries({ queryKey: ['dp-tables'] }), qc.refetchQueries({ queryKey: ['dp-review'] })])
    },
    onError: fail,
    onSettled: (_d, _e, v) => setPendingFields((p) => { const n = { ...p }; delete n[v.entity + '.' + v.field.attribute]; return n }),
  })

  const checkMut = useMutation({
    mutationFn: (entity: string) => post<CheckResult>(`/api/admin/data-policy/tables/${encodeURIComponent(entity)}/check`),
    onSuccess: (r, entity) => setChecks((c) => ({ ...c, [entity]: r })),
    onError: (e, entity) => setChecks((c) => ({ ...c, [entity]: { outcome: 'error', message: errText(e) } })),
  })

  const source = (s: string) => tOr('data-access.src.' + s, s)

  const columns: GridColumn<Table>[] = [
    {
      id: 'table', title: t('data-access.colTable'), value: (r) => r.label + ' ' + r.entity, sortable: true, mobile: 'title',
      render: (r) => <Tooltip label={r.entity} openDelay={400}><Text size="sm" fw={500}>{r.label}</Text></Tooltip>,
    },
    {
      id: 'status', title: t('data-access.colStatus'), value: (r) => (r.open ? t('data-access.open') : t('data-access.closed')), sortable: true, filter: 'multi', width: 130,
      render: (r) => <StatusPill tone={r.open ? 'good' : 'neutral'}>{r.open ? t('data-access.open') : t('data-access.closed')}</StatusPill>,
    },
    { id: 'source', title: t('data-access.sourceLabel'), value: (r) => source(r.source), sortable: true, filter: 'multi', width: 200 },
    {
      id: 'fields', title: t('data-access.colFieldDecisions'), mobile: 'hide',
      value: (r) => fieldSummary(r),
      render: (r) => <Text size="sm" c="dimmed">{fieldSummary(r)}</Text>,
    },
    {
      id: 'switch', title: t('data-access.colSwitch'), value: (r) => (r.open ? 1 : 0), width: 90, align: 'center',
      render: (r) => (
        <div onClick={(e) => e.stopPropagation()}>
          <Switch
            checked={r.open}
            disabled={r.entity in pendingTables}
            aria-label={t('data-access.switchLabel', { name: r.label })}
            onChange={(e) => tableMut.mutate({ table: r, allowed: e.currentTarget.checked })}
          />
        </div>
      ),
    },
  ]

  /** Builds a short summary text of a table's field decisions. */
  function fieldSummary(r: Table): string {
    if (r.autoClosedFields === null || r.autoClosedFields === undefined) return ''
    const parts: string[] = []
    if (r.autoClosedFields > 0) parts.push(t('data-access.autoClosed', { n: r.autoClosedFields }))
    if ((r.adminClosedFields ?? 0) > 0) parts.push(t('data-access.adminClosed', { n: r.adminClosedFields ?? 0 }))
    return parts.length ? parts.join(', ') : t('data-access.allFieldsOpen')
  }

  if (tablesQ.isError) {
    const e = tablesQ.error
    return (
      <>
        <PageHeader title={t('data-access.title')} description={t('data-access.howTitle')} />
        <Callout kind="bad" action={<Button mt="sm" size="xs" variant="default" onClick={() => void tablesQ.refetch()}>{t('ui.retry')}</Button>}>
          {e instanceof ApiError && e.status === 404 ? t('data-access.notAvailable') : e instanceof ApiError && e.hasMessage ? e.message : t('data-access.loadFailed')}
        </Callout>
      </>
    )
  }

  const data = tablesQ.data
  const review = reviewQ.data ?? []

  return (
    <>
      <PageHeader
        title={t('data-access.title')}
        description={t('data-access.intro')}
        actions={<Button variant="default" leftSection={<IconRefresh size={16} />} loading={tablesQ.isFetching} onClick={refreshAll}>{t('ui.refresh')}</Button>}
      />

      <Stack gap="md">
        {data && !data.enabled && <Callout kind="bad">{t('data-access.switchedOff')}</Callout>}
        {data && !data.persistent && <Callout kind="warn">{t('data-access.memoryOnly')}</Callout>}
        {data && data.decisions === 0 && <Callout kind="info">{t('data-access.noDecision')}</Callout>}

        {review.length > 0 && (
          <SectionCard title={review.length === 1 ? t('data-access.reviewTitleOne') : t('data-access.reviewTitle', { n: review.length })} description={t('data-access.reviewText')}>
            <ScrollArea.Autosize mah={240} aria-label={t('data-access.reviewAria')}>
              <Stack gap={0}>
                {review.map((row) => {
                  const busy = (row.entity + '.' + row.attribute) in pendingFields
                  return (
                    <Group key={row.entity + '.' + row.attribute} justify="space-between" wrap="nowrap" gap="sm" py={8} style={{ borderTop: '1px solid var(--mantine-color-default-border)' }}>
                      <div style={{ minWidth: 0 }}>
                        <Text size="sm" fw={500}>{row.label}</Text>
                        <Tooltip label={row.entity + '.' + row.attribute} openDelay={400}><Text size="xs" c="dimmed">{row.entityLabel}</Text></Tooltip>
                      </div>
                      <Tooltip label={t('data-access.openItTip')}>
                        <Button size="xs" variant="default" loading={busy} onClick={() => fieldMut.mutate({ entity: row.entity, field: row, allowed: true })}>{t('data-access.openIt')}</Button>
                      </Tooltip>
                    </Group>
                  )
                })}
              </Stack>
            </ScrollArea.Autosize>
          </SectionCard>
        )}

        {tablesQ.isLoading ? (
          <Skeleton height={320} radius="md" />
        ) : (
          <DataGrid<Table>
            storageKey="data-access"
            exportName="data-access-tables"
            records={shown}
            columns={columns}
            getId={(r) => r.entity}
            onRowClick={(r) => setSelected(r.entity)}
            emptyTitle={t('data-access.noTables')}
            toolbarExtra={
              <SegmentedControl
                size="sm"
                value={activeTab}
                onChange={(v) => setTab(v as Tab)}
                data={[
                  { value: 'open', label: t('data-access.tabOpen', { n: counts.open }) },
                  { value: 'closed', label: t('data-access.tabClosed', { n: counts.closed }) },
                  { value: 'all', label: t('data-access.tabAll', { n: counts.all }) },
                ]}
              />
            }
          />
        )}
      </Stack>

      <Drawer
        opened={current !== null}
        onClose={() => setSelected(null)}
        position="right"
        size={mobile ? '100%' : 680}
        title={<Text fw={600}>{current?.label}</Text>}
      >
        {current && (
          <TableDetail
            table={current}
            sourceText={source(current.source)}
            summary={fieldSummary(current)}
            busy={current.entity in pendingTables}
            onToggle={(allowed) => tableMut.mutate({ table: current, allowed })}
            onReset={() => tableMut.mutate({ table: current, allowed: null })}
            onCheck={() => checkMut.mutate(current.entity)}
            checking={checkMut.isPending && checkMut.variables === current.entity}
            check={checks[current.entity]}
            fields={fieldsQ.data}
            fieldsLoading={fieldsQ.isLoading}
            fieldsError={fieldsQ.isError ? errText(fieldsQ.error) : null}
            pendingFields={pendingFields}
            onField={(f, allowed) => fieldMut.mutate({ entity: current.entity, field: f, allowed })}
            sourceOf={source}
          />
        )}
      </Drawer>
    </>
  )
}

type DetailProps = {
  table: Table
  sourceText: string
  summary: string
  busy: boolean
  onToggle: (allowed: boolean) => void
  onReset: () => void
  onCheck: () => void
  checking: boolean
  check?: CheckResult
  fields?: Field[]
  fieldsLoading: boolean
  fieldsError: string | null
  pendingFields: Record<string, boolean>
  onField: (f: Field, allowed: boolean | null) => void
  sourceOf: (s: string) => string
}

/** Renders the detail panel for a selected table with its fields. */
function TableDetail(p: DetailProps) {
  const { t } = useI18n()
  const { table } = p
  const ok = p.check?.outcome === 'readable'

  const fields = (p.fields ?? []).map((f) => (table.entity + '.' + f.attribute in p.pendingFields ? { ...f, open: p.pendingFields[table.entity + '.' + f.attribute] } : f))

  const columns: GridColumn<Field>[] = [
    {
      id: 'field', title: t('data-access.colField'), value: (r) => r.label + ' ' + r.attribute, sortable: true, mobile: 'title',
      render: (r) => <Tooltip label={r.attribute} openDelay={400}><Text size="sm" fw={500}>{r.label}</Text></Tooltip>,
    },
    { id: 'type', title: t('data-access.colType'), value: (r) => r.type, sortable: true, filter: 'multi', width: 110 },
    {
      id: 'status', title: t('data-access.colStatus'), value: (r) => (r.open ? t('data-access.open') : t('data-access.closed')), sortable: true, filter: 'multi', width: 150,
      render: (r) => (
        <Stack gap={2} align="flex-start">
          <StatusPill tone={r.open ? 'good' : 'neutral'}>{r.open ? t('data-access.open') : t('data-access.closed')}</StatusPill>
          <Text size="xs" c="dimmed">{r.locked ? t('data-access.lockedOpen') : p.sourceOf(r.state)}</Text>
        </Stack>
      ),
    },
    {
      id: 'switch', title: t('data-access.colSwitch'), value: (r) => (r.open ? 1 : 0), width: 130,
      render: (r) => {
        if (r.locked) return <Tooltip label={t('data-access.lockedTip')}><IconLock size={18} aria-label={t('data-access.lockedOpen')} /></Tooltip>
        return (
          <Group gap={6} wrap="nowrap">
            <Switch
              checked={r.open}
              disabled={table.entity + '.' + r.attribute in p.pendingFields}
              aria-label={t('data-access.fieldSwitchLabel', { name: r.label })}
              onChange={(e) => p.onField(r, e.currentTarget.checked)}
            />
            {r.state.includes('administrator') && (
              <Tooltip label={t('data-access.resetTip')}>
                <ActionIcon variant="subtle" size="md" aria-label={t('data-access.reset')} onClick={() => p.onField(r, null)}><IconRefresh size={16} /></ActionIcon>
              </Tooltip>
            )}
          </Group>
        )
      },
    },
  ]

  return (
    <Stack gap="md">
      <Group gap="sm" wrap="wrap">
        <StatusPill tone={table.open ? 'good' : 'neutral'}>{table.open ? t('data-access.open') : t('data-access.closed')}</StatusPill>
        <Text size="sm" c="dimmed">{p.sourceText}</Text>
      </Group>
      {p.summary && table.open && <Text size="sm" c="dimmed">{p.summary}</Text>}

      <Group gap="xs" wrap="wrap">
        <Tooltip label={table.open ? t('data-access.toggleCloseTip') : t('data-access.toggleOpenTip')}>
          <Button
            variant={table.open ? 'default' : 'filled'}
            loading={p.busy}
            onClick={() => p.onToggle(!table.open)}
          >
            {table.open ? t('data-access.closeTable') : t('data-access.openTable')}
          </Button>
        </Tooltip>
        {table.source.includes('administrator') && (
          <Tooltip label={t('data-access.resetTip')}><Button variant="default" onClick={p.onReset}>{t('data-access.reset')}</Button></Tooltip>
        )}
        <Tooltip label={t('data-access.checkTip')} multiline w={280}>
          <Button variant="default" leftSection={<IconShieldCheck size={16} />} loading={p.checking} onClick={p.onCheck}>{t('data-access.checkAccess')}</Button>
        </Tooltip>
      </Group>

      {p.check && (
        <Callout kind={ok ? 'info' : 'bad'}>
          {t(ok ? 'data-access.readable' : p.check.outcome === 'not_readable' ? 'data-access.notReadable' : 'data-access.couldNotCheck', { msg: p.check.message })}
        </Callout>
      )}

      {table.open ? (
        <Stack gap="xs">
          <div>
            <Text fw={600}>{t('data-access.fieldsHeading', { name: table.label })}</Text>
            <Text size="sm" c="dimmed">{t('data-access.fieldsText')}</Text>
          </div>
          {p.fieldsError ? (
            <Callout kind="bad">{p.fieldsError}</Callout>
          ) : p.fieldsLoading ? (
            <Skeleton height={240} radius="md" />
          ) : (
            <DataGrid<Field>
              storageKey="data-access-fields"
              exportName={`data-access-${table.entity}`}
              records={fields}
              columns={columns}
              getId={(r) => r.attribute}
              pageSize={25}
              emptyTitle={t('data-access.noTables')}
            />
          )}
        </Stack>
      ) : (
        <Text size="sm" c="dimmed">{t('data-access.closedFieldsHint')}</Text>
      )}
    </Stack>
  )
}
