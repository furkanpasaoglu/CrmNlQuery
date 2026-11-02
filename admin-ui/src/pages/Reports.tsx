import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ActionIcon, Button, Drawer, Group, Select, Stack, Switch, Text, Textarea, TextInput, Tooltip } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { IconPencil, IconPlus, IconTrash } from '@tabler/icons-react'
import { ApiError, get, post, put, del } from '../api/client'
import { useT, useTOr } from '../i18n'
import { PageHeader } from '../components/PageHeader'
import { DataGrid, type GridColumn } from '../components/DataGrid'
import { Callout } from '../components/Callout'
import { SectionCard } from '../components/SectionCard'
import { confirmAction } from '../components/confirm'

type Entry = {
  id: string; name: string; description: string | null; keywords: string[]; crmReportId: string | null; url: string | null
  recordEntity: string | null; recordParam: string | null; enabled: boolean; updatedBy: string
}
type CatalogData = { enabled: boolean; persistent: boolean; maxEntries: number; entries: Entry[] }
type CrmReport = { id: string; name: string; description: string | null }
type TestResult = { isReportQuestion: boolean; matches: { id: string; name: string; description: string | null }[] }

/** Translates a server error code to a message, falling back to the server's own message. */
function useReason() {
  const tOr = useTOr()
  return (e: unknown) => (e instanceof ApiError ? (e.code ? tOr('reports.reason_' + e.code, e.code) : e.message) : String(e))
}

/** Renders the Reports admin page for the report catalog. */
export default function Reports() {
  const t = useT()
  const reason = useReason()
  const qc = useQueryClient()
  const [editing, setEditing] = useState<Entry | 'new' | null>(null)

  const q = useQuery({ queryKey: ['reports-catalog'], queryFn: () => get<CatalogData>('/api/admin/reports') })
  const crm = useQuery({ queryKey: ['reports-crm'], queryFn: () => get<CrmReport[]>('/api/admin/reports/crm'), staleTime: 300_000 })
  const crmNames = useMemo(() => Object.fromEntries((crm.data ?? []).map((r) => [r.id.toLowerCase(), r.name])), [crm.data])
  const refresh = () => void qc.invalidateQueries({ queryKey: ['reports-catalog'] })

  const toggle = useMutation({
    mutationFn: (v: { entry: Entry; enabled: boolean }) => put(`/api/admin/reports/${encodeURIComponent(v.entry.id)}`, toBody(v.entry, v.enabled)),
    onSuccess: (_d, v) => { notifications.show({ color: 'teal', message: t(v.enabled ? 'reports.nowEnabled' : 'reports.nowDisabled') }); refresh() },
    onError: (e) => notifications.show({ color: 'red', message: reason(e) }),
  })
  const remove = useMutation({
    mutationFn: (id: string) => del(`/api/admin/reports/${encodeURIComponent(id)}`),
    onSuccess: () => { notifications.show({ color: 'teal', message: t('reports.deleted') }); refresh() },
    onError: (e) => notifications.show({ color: 'red', message: reason(e) }),
  })

  const data = q.data

  const columns: GridColumn<Entry>[] = [
    { id: 'name', title: t('reports.colName'), value: (r) => r.name + ' ' + (r.description ?? ''), sortable: true, mobile: 'title',
      render: (r) => (<Stack gap={0}><Text size="sm" fw={600}>{r.name}</Text>{r.description && <Text size="xs" c="dimmed">{r.description}</Text>}</Stack>) },
    { id: 'target', title: t('reports.colTarget'), value: (r) => (r.crmReportId ? crmNames[r.crmReportId.toLowerCase()] ?? r.crmReportId : r.url ?? ''), width: 260,
      render: (r) => (
        <Stack gap={0}>
          <Text size="sm">{t(r.crmReportId ? 'reports.targetCrm' : 'reports.targetExternal')}</Text>
          <Text size="xs" c="dimmed" style={{ overflowWrap: 'anywhere' }}>{r.crmReportId ? crmNames[r.crmReportId.toLowerCase()] ?? r.crmReportId : r.url}</Text>
        </Stack>
      ) },
    { id: 'keywords', title: t('reports.colKeywords'), value: (r) => r.keywords.join(', '), width: 220 },
    { id: 'record', title: t('reports.colRecord'), value: (r) => r.recordEntity ?? '', sortable: true, width: 130 },
    {
      id: 'actions', title: t('reports.colActions'), value: () => '', width: 170,
      render: (r) => (
        <Group gap={6} wrap="nowrap">
          <Switch aria-label={r.enabled ? t('reports.disable') : t('reports.enable')} checked={r.enabled} disabled={toggle.isPending} onChange={(e) => toggle.mutate({ entry: r, enabled: e.currentTarget.checked })} />
          <Tooltip label={t('reports.edit')}><ActionIcon variant="default" size="lg" aria-label={t('reports.edit')} onClick={() => setEditing(r)}><IconPencil size={16} /></ActionIcon></Tooltip>
          <Tooltip label={t('reports.delete')}>
            <ActionIcon variant="default" color="red" size="lg" aria-label={t('reports.delete')}
              onClick={() => confirmAction({ title: t('reports.delete'), message: t('reports.deleteMsg'), confirmLabel: t('reports.delete'), cancelLabel: t('ui.cancel'), danger: true, onConfirm: () => remove.mutate(r.id) })}>
              <IconTrash size={16} />
            </ActionIcon>
          </Tooltip>
        </Group>
      ),
    },
  ]

  return (
    <>
      <PageHeader
        title={t('reports.title')}
        description={t('reports.sub')}
        actions={<Button leftSection={<IconPlus size={16} />} onClick={() => setEditing('new')}>{t('reports.add')}</Button>}
      />

      {q.isError ? (
        <Callout kind="bad" action={<Button mt="xs" size="xs" variant="default" onClick={() => void q.refetch()}>{t('ui.retry')}</Button>}>{t('reports.loadError')}</Callout>
      ) : (
        <Stack gap="sm">
          {data && !data.persistent && <Callout kind="warn">{t('reports.notPersistent')}</Callout>}
          {data && !data.enabled && <Callout kind="info">{t('reports.switchedOff')}</Callout>}
          {crm.isSuccess && crm.data.length === 0 && <Callout kind="info">{t('reports.noCrmReports')}</Callout>}
          <TryCard />
          <DataGrid<Entry>
            storageKey="reports"
            exportName="reports"
            records={data?.entries ?? []}
            loading={q.isLoading}
            getId={(r) => r.id}
            columns={columns}
            emptyTitle={t('reports.empty')}
            emptyAction={<Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => setEditing('new')}>{t('reports.add')}</Button>}
            toolbarExtra={data ? <Text size="sm" c="dimmed">{t('reports.count', { count: data.entries.length, max: data.maxEntries })}</Text> : null}
          />
        </Stack>
      )}

      <Drawer opened={editing !== null} onClose={() => setEditing(null)} position="right" size="lg" title={<Text fw={600}>{editing === 'new' ? t('reports.formNew') : t('reports.formEdit')}</Text>}>
        {editing !== null && (
          <EntryForm
            key={editing === 'new' ? 'new' : editing.id}
            existing={editing === 'new' ? null : editing}
            crmReports={crm.data ?? []}
            onClose={() => setEditing(null)}
            onSaved={() => { setEditing(null); refresh(); notifications.show({ color: 'teal', message: t('reports.saved') }) }}
          />
        )}
      </Drawer>
    </>
  )
}

/** Builds the request body from the report form values. */
function toBody(e: Entry, enabled: boolean) {
  return { name: e.name, description: e.description, keywords: e.keywords, crmReportId: e.crmReportId, url: e.url, recordEntity: e.recordEntity, recordParam: e.recordParam, enabled }
}

/** Renders the try panel that shows which reports a typed question would find, using the admin's permissions. */
function TryCard() {
  const t = useT()
  const reason = useReason()
  const [question, setQuestion] = useState('')
  const test = useMutation({
    mutationFn: () => post<TestResult>('/api/admin/reports/test', { question }),
    onError: (e) => notifications.show({ color: 'red', message: reason(e) }),
  })
  const r = test.data
  return (
    <SectionCard title={t('reports.tryTitle')}>
      <Stack gap="xs">
        <Text size="sm" c="dimmed">{t('reports.tryHelp')}</Text>
        <Group gap="xs" align="flex-end" wrap="wrap">
          <TextInput style={{ flex: 1, minWidth: 220 }} aria-label={t('reports.tryTitle')} placeholder={t('reports.tryPlaceholder')} value={question} maxLength={300}
            onChange={(e) => { setQuestion(e.currentTarget.value); test.reset() }}
            onKeyDown={(e) => { if (e.key === 'Enter' && question.trim()) test.mutate() }} />
          <Button variant="default" disabled={!question.trim()} loading={test.isPending} onClick={() => test.mutate()}>{t('reports.tryRun')}</Button>
        </Group>
        {r && !r.isReportQuestion && <Callout kind="info">{t('reports.tryNotReport')}</Callout>}
        {r && r.isReportQuestion && r.matches.length === 0 && <Callout kind="warn">{t('reports.tryNone')}</Callout>}
        {r && r.matches.length > 0 && (
          <Stack gap={4}>
            <Text size="sm" fw={600}>{t('reports.tryFound', { n: r.matches.length })}</Text>
            {r.matches.map((m) => <Text key={m.id} size="sm">{m.name}{m.description ? ' — ' + m.description : ''}</Text>)}
          </Stack>
        )}
      </Stack>
    </SectionCard>
  )
}

/** Renders the add or edit form for a report catalog entry. */
function EntryForm({ existing, crmReports, onClose, onSaved }: { existing: Entry | null; crmReports: CrmReport[]; onClose: () => void; onSaved: () => void }) {
  const t = useT()
  const reason = useReason()
  const [name, setName] = useState(existing?.name ?? '')
  const [description, setDescription] = useState(existing?.description ?? '')
  const [keywords, setKeywords] = useState(existing ? existing.keywords.join('\n') : '')
  const [kind, setKind] = useState<string>(existing?.url ? 'external' : 'crm')
  const [crmId, setCrmId] = useState<string | null>(existing?.crmReportId ?? null)
  const [url, setUrl] = useState(existing?.url ?? '')
  const [recordEntity, setRecordEntity] = useState(existing?.recordEntity ?? '')
  const [recordParam, setRecordParam] = useState(existing?.recordParam ?? '')
  const [error, setError] = useState<string | null>(null)

  const body = () => ({
    name,
    description,
    keywords: keywords.split(/\r?\n|,/).map((x) => x.trim()).filter(Boolean),
    crmReportId: kind === 'crm' ? crmId : null,
    url: kind === 'external' ? url : null,
    recordEntity,
    recordParam: kind === 'external' ? recordParam : null,
    enabled: existing ? existing.enabled : true,
  })

  const save = useMutation({
    mutationFn: () => (existing ? put(`/api/admin/reports/${encodeURIComponent(existing.id)}`, body()) : post('/api/admin/reports', body())),
    onSuccess: onSaved,
    onError: (e) => setError(reason(e)),
  })

  return (
    <Stack gap="md">
      <TextInput label={t('reports.fName')} description={t('reports.fNameHelp')} value={name} maxLength={120} onChange={(e) => setName(e.currentTarget.value)} />
      <Textarea label={t('reports.fDescription')} autosize minRows={2} maxLength={300} value={description} onChange={(e) => setDescription(e.currentTarget.value)} />
      <Textarea label={t('reports.fKeywords')} description={t('reports.fKeywordsHelp')} autosize minRows={3} value={keywords} onChange={(e) => setKeywords(e.currentTarget.value)} />
      <Select label={t('reports.fKind')} allowDeselect={false} value={kind} onChange={(v) => setKind(v ?? 'crm')}
        data={[{ value: 'crm', label: t('reports.targetCrm') }, { value: 'external', label: t('reports.targetExternal') }]} />
      {kind === 'crm' ? (
        <Select label={t('reports.fCrmReport')} placeholder={t('reports.fChooseReport')} searchable value={crmId} onChange={setCrmId}
          data={crmReports.map((r) => ({ value: r.id, label: r.name }))} />
      ) : (
        <TextInput label={t('reports.fUrl')} description={t('reports.fUrlHelp')} placeholder="https://" value={url} maxLength={1000} onChange={(e) => setUrl(e.currentTarget.value)} />
      )}
      <TextInput label={t('reports.fRecordEntity')} description={t('reports.fRecordEntityHelp')} placeholder="account" value={recordEntity} maxLength={64} onChange={(e) => setRecordEntity(e.currentTarget.value)} />
      {kind === 'external' && recordEntity.trim() && (
        <TextInput label={t('reports.fRecordParam')} description={t('reports.fRecordParamHelp')} placeholder="accountId" value={recordParam} maxLength={60} onChange={(e) => setRecordParam(e.currentTarget.value)} />
      )}
      {error && <Callout kind="bad">{error}</Callout>}
      <Group justify="flex-end" gap="xs">
        <Button variant="default" onClick={onClose}>{t('ui.cancel')}</Button>
        <Button loading={save.isPending} onClick={() => { setError(null); save.mutate() }}>{t('ui.save')}</Button>
      </Group>
    </Stack>
  )
}
