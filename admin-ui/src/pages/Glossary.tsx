import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ActionIcon, Button, Drawer, FileInput, Group, Modal, Select, Stack, Switch, Text, Textarea, Tooltip } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { IconDownload, IconPencil, IconPlus, IconTrash, IconUpload } from '@tabler/icons-react'
import { ApiError, downloadFile, get, post, put, del } from '../api/client'
import { useI18n, useT, useTOr } from '../i18n'
import { PageHeader } from '../components/PageHeader'
import { DataGrid, type GridColumn } from '../components/DataGrid'
import { Callout } from '../components/Callout'
import { StatusPill } from '../components/StatusPill'
import { SectionCard } from '../components/SectionCard'
import { confirmAction } from '../components/confirm'
import { KnowConditionRow, allowedOps, type ConditionDraft, type GlossaryAttr } from '../components/KnowConditionRow'

type Term = { text: string; language: string }
type Cond = { attribute: string; operator: string; values: string[] }
type Entry = {
  id: string; kind: 'alias' | 'filter'; terms: Term[]; entity: string; attribute: string | null; conditions: Cond[]
  description: string; enabled: boolean; status: 'ok' | 'broken' | 'disabled'; reason: string | null
  applied: number; notApplied: number; lastNotAppliedAt: string | null
}
type GlossaryData = { enabled: boolean; persistent: boolean; maxEntries: number; broken: number; entries: Entry[] }
type Table = { entity: string; label: string }
type TryResult = { outcome: string; rows: number; capped: boolean; reason: string | null; text: string | null }
type ImportReport = { total: number; accepted: number; applied: number; rejected: { index: number; term: string; reason: string }[] }

let seq = 0
/** Generates a new unique key for a list item. */
const newKey = () => 'c' + ++seq

/** Returns the reason text for a server error, translated when it has a code and otherwise the server message. */
function useReason() {
  const tOr = useTOr()
  return (e: unknown) => (e instanceof ApiError ? (e.code ? tOr('glossary.reason_' + e.code, e.code) : e.message) : String(e))
}

/** Renders the Business glossary admin page with its list, status, and add, import and export actions. */
export default function Glossary() {
  const t = useT()
  const tOr = useTOr()
  const { locale } = useI18n()
  const reason = useReason()
  const qc = useQueryClient()
  const [kindF, setKindF] = useState<string | null>(null)
  const [statusF, setStatusF] = useState<string | null>(null)
  const [editing, setEditing] = useState<Entry | 'new' | null>(null)
  const [importOpen, setImportOpen] = useState(false)

  const q = useQuery({ queryKey: ['glossary'], queryFn: () => get<GlossaryData>('/api/admin/glossary') })
  const tables = useQuery({ queryKey: ['glossary-tables'], queryFn: () => get<Table[]>('/api/admin/glossary/schema'), staleTime: 300_000 })
  const labels = useMemo(() => Object.fromEntries((tables.data ?? []).map((x) => [x.entity, x.label])), [tables.data])
  const refresh = () => void qc.invalidateQueries({ queryKey: ['glossary'] })

  const toggle = useMutation({
    mutationFn: (v: { id: string; enabled: boolean }) => put(`/api/admin/glossary/${encodeURIComponent(v.id)}/enabled`, { enabled: v.enabled }),
    onSuccess: (_d, v) => { notifications.show({ color: 'teal', message: t(v.enabled ? 'glossary.nowEnabled' : 'glossary.nowDisabled') }); refresh() },
    onError: (e) => notifications.show({ color: 'red', message: reason(e) }),
  })
  const remove = useMutation({
    mutationFn: (id: string) => del(`/api/admin/glossary/${encodeURIComponent(id)}`),
    onSuccess: () => { notifications.show({ color: 'teal', message: t('glossary.deleted') }); refresh() },
    onError: (e) => notifications.show({ color: 'red', message: reason(e) }),
  })

  const data = q.data
  const entries = useMemo(() => (data?.entries ?? []).filter((e) => (!kindF || e.kind === kindF) && (!statusF || e.status === statusF)), [data, kindF, statusF])

  const technical = (e: Entry) => e.kind === 'alias'
    ? `${e.entity}${e.attribute ? ' · ' + e.attribute : ''}`
    : `${e.entity}: ` + e.conditions.map((c) => `${c.attribute} ${tOr('glossary.op_' + c.operator.replace(/-/g, '_'), c.operator)}${c.values.length ? ' ' + c.values.join(', ') : ''}`).join('; ')
  const readable = (e: Entry) => e.description || technical(e)

  const columns: GridColumn<Entry>[] = [
    { id: 'terms', title: t('glossary.colTerms'), value: (r) => r.terms.map((x) => x.text).join(', '), sortable: true, mobile: 'title', render: (r) => <Text size="sm" fw={600}>{r.terms.map((x) => x.text).join(', ')}</Text> },
    { id: 'kind', title: t('glossary.colKind'), value: (r) => tOr('glossary.kind_' + r.kind, r.kind), sortable: true, width: 170 },
    {
      id: 'target', title: t('glossary.colTarget'), value: (r) => readable(r) + ' ' + (labels[r.entity] ?? r.entity),
      render: (r) => (
        <Tooltip label={technical(r)} multiline maw={360} withinPortal>
          <Stack gap={0}>
            <Text size="sm">{readable(r)}</Text>
            <Text size="xs" c="dimmed">{labels[r.entity] ?? r.entity}</Text>
          </Stack>
        </Tooltip>
      ),
    },
    {
      id: 'status', title: t('glossary.colStatus'), value: (r) => tOr('glossary.status_' + r.status, r.status), sortable: true, width: 150,
      render: (r) => (
        <Stack gap={2} align="flex-start">
          <StatusPill tone={r.status === 'ok' ? 'good' : r.status === 'broken' ? 'bad' : 'neutral'}>{tOr('glossary.status_' + r.status, r.status)}</StatusPill>
          {r.status === 'broken' && r.reason && <Text size="xs" c="red">{tOr('glossary.reason_' + r.reason, r.reason)}</Text>}
        </Stack>
      ),
    },
    {
      id: 'notApplied', title: t('glossary.colNotApplied'), value: (r) => r.notApplied, sortable: true, width: 170,
      render: (r) => r.notApplied > 0
        ? <Text size="sm">{t('glossary.notAppliedTimes', { n: r.notApplied })}{r.lastNotAppliedAt ? ` (${t('glossary.notAppliedSince', { when: new Date(r.lastNotAppliedAt).toLocaleDateString(locale) })})` : ''}</Text>
        : <Text size="sm" c="dimmed">{t('glossary.never')}</Text>,
    },
    {
      id: 'actions', title: t('glossary.colActions'), value: () => '', width: 170,
      render: (r) => (
        <Group gap={6} wrap="nowrap">
          <Switch aria-label={r.enabled ? t('glossary.disable') : t('glossary.enable')} checked={r.enabled} disabled={toggle.isPending} onChange={(e) => toggle.mutate({ id: r.id, enabled: e.currentTarget.checked })} />
          <Tooltip label={t('glossary.edit')}><ActionIcon variant="default" size="lg" aria-label={t('glossary.edit')} onClick={() => setEditing(r)}><IconPencil size={16} /></ActionIcon></Tooltip>
          <Tooltip label={t('glossary.delete')}>
            <ActionIcon variant="default" color="red" size="lg" aria-label={t('glossary.delete')}
              onClick={() => confirmAction({ title: t('glossary.delete'), message: t('glossary.deleteMsg'), confirmLabel: t('glossary.delete'), cancelLabel: t('glossary.cancel'), danger: true, onConfirm: () => remove.mutate(r.id) })}>
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
        title={t('glossary.title')}
        description={t('glossary.sub')}
        actions={
          <>
            <Button variant="default" leftSection={<IconUpload size={16} />} onClick={() => setImportOpen(true)}>{t('glossary.import')}</Button>
            <Button variant="default" leftSection={<IconDownload size={16} />} onClick={() => void downloadFile('/api/admin/glossary/export', 'glossary.json').catch((e) => notifications.show({ color: 'red', message: e instanceof Error ? e.message : String(e) }))}>{t('glossary.export')}</Button>
            <Button leftSection={<IconPlus size={16} />} onClick={() => setEditing('new')}>{t('glossary.add')}</Button>
          </>
        }
      />

      {q.isError ? (
        <Callout kind="bad" action={<Button mt="xs" size="xs" variant="default" onClick={() => void q.refetch()}>{t('ui.retry')}</Button>}>{t('glossary.loadError')}</Callout>
      ) : (
        <Stack gap="sm">
          {data && !data.persistent && <Callout kind="warn">{t('glossary.notPersistent')}</Callout>}
          {data && data.broken > 0 && <Callout kind="warn">{t('glossary.brokenWarn', { n: data.broken })}</Callout>}
          {data && !data.enabled && <Callout kind="info">{t('glossary.switchedOff')}</Callout>}
          <DataGrid<Entry>
            storageKey="glossary"
            exportName="glossary"
            records={entries}
            loading={q.isLoading}
            getId={(r) => r.id}
            columns={columns}
            emptyTitle={t('glossary.empty')}
            emptyAction={<Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => setEditing('new')}>{t('glossary.add')}</Button>}
            toolbarExtra={
              <>
                <Select aria-label={t('glossary.colKind')} w={170} data={[{ value: 'alias', label: t('glossary.kind_alias') }, { value: 'filter', label: t('glossary.kind_filter') }]} placeholder={t('glossary.allKinds')} value={kindF} onChange={setKindF} clearable />
                <Select aria-label={t('glossary.colStatus')} w={150} data={['ok', 'broken', 'disabled'].map((s) => ({ value: s, label: t('glossary.status_' + s) }))} placeholder={t('glossary.allStatus')} value={statusF} onChange={setStatusF} clearable />
                {data && <Text size="sm" c="dimmed">{t('glossary.count', { count: data.entries.length, max: data.maxEntries })}</Text>}
              </>
            }
          />
        </Stack>
      )}

      <Drawer opened={editing !== null} onClose={() => setEditing(null)} position="right" size="lg" title={<Text fw={600}>{editing === 'new' ? t('glossary.formNew') : t('glossary.formEdit')}</Text>}>
        {editing !== null && (
          <EntryForm
            key={editing === 'new' ? 'new' : editing.id}
            existing={editing === 'new' ? null : editing}
            tables={tables.data ?? []}
            onClose={() => setEditing(null)}
            onSaved={(docs) => {
              setEditing(null)
              refresh()
              notifications.show({ color: 'teal', autoClose: docs.length ? 10000 : 4000, message: t('glossary.saved') + (docs.length ? ' ' + t('glossary.docConflict', { docs: docs.join(', ') }) : '') })
            }}
          />
        )}
      </Drawer>

      <Modal opened={importOpen} onClose={() => setImportOpen(false)} title={t('glossary.importTitle')} size="lg">
        {importOpen && <ImportFlow onDone={() => { refresh() }} onClose={() => setImportOpen(false)} />}
      </Modal>
    </>
  )
}

/** Renders the form to add or edit a glossary entry. */
function EntryForm({ existing, tables, onClose, onSaved }: { existing: Entry | null; tables: Table[]; onClose: () => void; onSaved: (docs: string[]) => void }) {
  const t = useT()
  const reason = useReason()
  const [kind, setKind] = useState<string>(existing?.kind ?? 'filter')
  const [terms, setTerms] = useState(existing ? existing.terms.map((x) => x.text).join('\n') : '')
  const [lang, setLang] = useState<string>(existing?.terms[0]?.language ?? 'any')
  const [entity, setEntity] = useState<string | null>(existing?.entity ?? null)
  const [aliasAttr, setAliasAttr] = useState<string | null>(existing?.attribute ?? null)
  const [conds, setConds] = useState<ConditionDraft[]>(existing ? existing.conditions.map((c) => ({ key: newKey(), attribute: c.attribute, operator: c.operator, values: c.values })) : [])
  const [description, setDescription] = useState(existing?.description ?? '')
  const [error, setError] = useState<string | null>(null)
  const [tried, setTried] = useState<TryResult | null>(null)

  const attrsQ = useQuery({
    queryKey: ['glossary-attrs', entity],
    queryFn: () => get<GlossaryAttr[]>(`/api/admin/glossary/schema/${encodeURIComponent(entity ?? '')}`),
    enabled: Boolean(entity),
    staleTime: 300_000,
  })
  const attrs = attrsQ.data ?? []

  const draft = () => ({
    kind,
    terms: terms.split(/\r?\n/).map((x) => x.trim()).filter(Boolean).map((x) => ({ text: x, language: lang })),
    entity: entity ?? '',
    attribute: kind === 'alias' ? aliasAttr : null,
    conditions: kind === 'filter' ? conds.map((c) => ({ attribute: c.attribute, operator: c.operator, values: c.values })) : [],
    description,
    enabled: existing ? existing.enabled : true,
  })

  const save = useMutation({
    mutationFn: () => (existing ? put<{ documents?: string[] }>(`/api/admin/glossary/${encodeURIComponent(existing.id)}`, draft()) : post<{ documents?: string[] }>('/api/admin/glossary', draft())),
    onSuccess: (r) => onSaved(r?.documents ?? []),
    onError: (e) => setError(reason(e)),
  })
  const tryIt = useMutation({
    mutationFn: () => post<TryResult>('/api/admin/glossary/try', draft()),
    onMutate: () => { setTried(null); setError(null) },
    onSuccess: setTried,
    onError: (e) => setError(e instanceof ApiError ? e.message : String(e)),
  })

  /** Handles a change of the entry's table selection in the form. */
  function changeEntity(v: string | null) {
    setEntity(v)
    setAliasAttr(null)
    setTried(null)
    setConds(kind === 'filter' && v ? [{ key: newKey(), attribute: '', operator: '', values: [] }] : [])
  }
  /** Handles a change of the entry's kind in the form. */
  function changeKind(v: string | null) {
    const k = v ?? 'filter'
    setKind(k)
    setTried(null)
    if (k === 'filter' && entity && conds.length === 0) setConds([{ key: newKey(), attribute: '', operator: '', values: [] }])
  }

  const tryLine = (r: TryResult): { kind: 'info' | 'bad'; text: string } => {
    if (r.outcome === 'rows') return { kind: 'info', text: r.capped ? t('glossary.tryCapped', { n: r.rows }) : t('glossary.tryRows', { n: r.rows }) }
    if (r.outcome === 'alias') return { kind: 'info', text: t('glossary.tryAlias', { text: r.text ?? '' }) }
    if (r.outcome === 'invalid') return { kind: 'bad', text: t('glossary.tryInvalid', { reason: r.reason ? t('glossary.reason_' + r.reason) : '' }) }
    if (r.outcome === 'not_readable') return { kind: 'bad', text: t('glossary.tryNotReadable') }
    return { kind: 'bad', text: t('glossary.tryError') }
  }

  return (
    <Stack gap="md">
      <Select label={t('glossary.fKind')} description={kind === 'filter' ? t('glossary.fKindFilterHelp') : t('glossary.fKindAliasHelp')} allowDeselect={false}
        data={[{ value: 'filter', label: t('glossary.kind_filter') }, { value: 'alias', label: t('glossary.kind_alias') }]} value={kind} onChange={changeKind} />
      <Textarea label={t('glossary.fTerms')} description={t('glossary.fTermsHelp')} autosize minRows={3} value={terms} onChange={(e) => setTerms(e.currentTarget.value)} />
      <Select label={t('glossary.fLanguage')} allowDeselect={false} value={lang} onChange={(v) => setLang(v ?? 'any')}
        data={[{ value: 'any', label: t('glossary.lang_any') }, { value: 'tr', label: t('glossary.lang_tr') }, { value: 'en', label: t('glossary.lang_en') }]} />
      <Select label={t('glossary.fTable')} placeholder={t('glossary.fChooseTable')} searchable value={entity} onChange={changeEntity} allowDeselect={false}
        data={tables.map((x) => ({ value: x.entity, label: `${x.label} (${x.entity})` }))} />

      {kind === 'alias' && (
        <Select label={t('glossary.fAttribute')} placeholder={t('glossary.fNoAttribute')} searchable clearable disabled={!entity} value={aliasAttr} onChange={setAliasAttr}
          data={attrs.map((a) => ({ value: a.attribute, label: `${a.label} (${a.attribute})` }))} />
      )}

      {kind === 'filter' && (
        <Stack gap="xs">
          <Text size="sm" fw={500}>{t('glossary.fConditions')}</Text>
          {conds.map((c) => (
            <KnowConditionRow key={c.key} attrs={attrs} row={c}
              onChange={(next) => { setTried(null); setConds((l) => l.map((x) => (x.key === c.key ? next : x))) }}
              onRemove={() => setConds((l) => l.filter((x) => x.key !== c.key))} />
          ))}
          <Button variant="default" size="xs" w="fit-content" leftSection={<IconPlus size={14} />} disabled={!entity || attrsQ.isLoading || conds.length >= 6}
            onClick={() => setConds((l) => [...l, { key: newKey(), attribute: '', operator: allowedOps(undefined)[0] ?? '', values: [] }])}>
            {t('glossary.addCondition')}
          </Button>
        </Stack>
      )}

      <Textarea label={t('glossary.fDescription')} description={t('glossary.descHelp')} autosize minRows={2} value={description} onChange={(e) => setDescription(e.currentTarget.value)} />

      {tried && <Callout kind={tryLine(tried).kind}>{tryLine(tried).text}{tried.outcome === 'rows' && <Text size="xs" mt={4}>{t('glossary.tryNote')}</Text>}</Callout>}
      {error && <Callout kind="bad">{error}</Callout>}

      <Group justify="space-between" wrap="wrap">
        <Button variant="default" loading={tryIt.isPending} onClick={() => tryIt.mutate()}>{tryIt.isPending ? t('glossary.trying') : t('glossary.try')}</Button>
        <Group gap="xs">
          <Button variant="default" onClick={onClose}>{t('ui.cancel')}</Button>
          <Button loading={save.isPending} onClick={() => { setError(null); save.mutate() }}>{t('ui.save')}</Button>
        </Group>
      </Group>
    </Stack>
  )
}

/** Renders the JSON import flow, showing a report first and importing after confirmation. */
function ImportFlow({ onDone, onClose }: { onDone: () => void; onClose: () => void }) {
  const t = useT()
  const reason = useReason()
  const [file, setFile] = useState<File | null>(null)
  const [report, setReport] = useState<ImportReport | null>(null)
  const [applied, setApplied] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const run = useMutation({
    mutationFn: async (apply: boolean) => {
      if (!file) throw new Error(t('glossary.importNoFile'))
      return post<ImportReport>('/api/admin/glossary/import', { json: await file.text(), apply })
    },
    onMutate: () => setError(null),
    onSuccess: (r, apply) => {
      setReport(r)
      if (apply) { setApplied(true); onDone(); notifications.show({ color: 'teal', message: t('glossary.importDone', { n: r.accepted }) }) }
    },
    onError: (e) => { setReport(null); setError(e instanceof Error && !(e instanceof ApiError) ? e.message : reason(e)) },
  })

  return (
    <Stack gap="md">
      <Text size="sm" c="dimmed">{t('glossary.importHelp')}</Text>
      <FileInput label={t('glossary.importChoose')} accept=".json,application/json" value={file} onChange={(f) => { setFile(f); setReport(null); setApplied(false); setError(null) }} clearable />
      {error && <Callout kind="bad">{error}</Callout>}
      {report && (
        <SectionCard title={applied ? t('glossary.importDone', { n: report.accepted }) : t('glossary.importReport', { accepted: report.accepted, total: report.total })}>
          {report.rejected.length > 0 && (
            <Stack gap={4}>
              {report.rejected.map((x) => <Text key={x.index} size="sm" c="red">{t('glossary.importRejected', { n: x.index + 1, term: x.term, reason: t('glossary.reason_' + x.reason) })}</Text>)}
            </Stack>
          )}
        </SectionCard>
      )}
      <Group justify="flex-end" wrap="wrap">
        <Button variant="default" onClick={onClose}>{t('glossary.importClose')}</Button>
        <Button variant="default" disabled={!file || applied} loading={run.isPending && !run.variables} onClick={() => run.mutate(false)}>{t('glossary.importCheck')}</Button>
        {report && !applied && report.accepted > 0 && <Button loading={run.isPending && run.variables === true} onClick={() => run.mutate(true)}>{t('glossary.importApply')}</Button>}
      </Group>
    </Stack>
  )
}
