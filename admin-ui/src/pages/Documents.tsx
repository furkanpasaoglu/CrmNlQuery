import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ActionIcon, Anchor, Badge, Button, Drawer, FileButton, Group, Loader, Menu, Skeleton, Stack, Switch, Text, TextInput, Textarea, Tooltip } from '@mantine/core'
import { Link } from 'react-router-dom'
import { useMediaQuery } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import { IconChevronDown, IconFileUpload, IconPencil, IconPlus, IconSearch, IconTemplate, IconTrash } from '@tabler/icons-react'
import { ApiError, del, get, post, put } from '../api/client'
import { useI18n } from '../i18n'
import { useMe } from '../auth/me'
import { checkDocument, checkFile, cleanText, titleFromFileName, type DocWarning } from '../lib/documentChecks'
import { PageHeader } from '../components/PageHeader'
import { SectionCard } from '../components/SectionCard'
import { DataGrid, type GridColumn } from '../components/DataGrid'
import { Callout } from '../components/Callout'
import { StatusPill } from '../components/StatusPill'
import { EmptyState } from '../components/EmptyState'
import { confirmAction } from '../components/confirm'

type Doc = { id: string; title: string; enabled: boolean; characters: number; updatedBy: string | null; updatedAt: string }
type DocsResponse = { searchEnabled: boolean; available: boolean; unavailableReason: string | null; documents: Doc[] }
type FullDoc = { id: string; title: string; body: string }
type Hit = { documentId: string; title: string; text: string; score: number; source: string }

/** Reads an error object into a displayable message string. */
const errText = (e: unknown) => (e instanceof Error ? e.message : String(e))
const MAX_TITLE = 200
const MAX_BODY = 20000
const TEMPLATES = ['term', 'field', 'avoid', 'rule', 'policy'] as const
const TEMPLATE_LABELS: Record<(typeof TEMPLATES)[number], string> = { term: 'documents.tplTerm', field: 'documents.tplField', avoid: 'documents.tplAvoid', rule: 'documents.tplRule', policy: 'documents.tplPolicy' }
const WARNING_KEYS: Record<Exclude<DocWarning, 'long'>, string> = { nationalId: 'documents.warnNationalId', iban: 'documents.warnIban', phone: 'documents.warnPhone', email: 'documents.warnEmail' }

/** Renders the Documents admin page for managing knowledge-base documents. */
export default function Documents() {
  const { t, locale } = useI18n()
  const { can } = useMe()
  const qc = useQueryClient()
  const mobile = useMediaQuery('(max-width: 48em)')
  const [editing, setEditing] = useState<string | null>(null)
  const [pending, setPending] = useState<Record<string, boolean>>({})

  const q = useQuery({ queryKey: ['documents'], queryFn: () => get<DocsResponse>('/api/admin/documents') })
  const reload = () => qc.invalidateQueries({ queryKey: ['documents'] })
  const fail = (e: unknown) => notifications.show({ color: 'red', message: errText(e), autoClose: 9000 })

  const enabledMut = useMutation({
    mutationFn: (v: { doc: Doc; enabled: boolean }) => put(`/api/admin/documents/${encodeURIComponent(v.doc.id)}/enabled`, { enabled: v.enabled }),
    onMutate: (v) => setPending((p) => ({ ...p, [v.doc.id]: v.enabled })),
    onSuccess: async (_d, v) => {
      notifications.show({ color: 'teal', message: v.enabled ? t('documents.turnedOn') : t('documents.turnedOff') })
      await qc.refetchQueries({ queryKey: ['documents'] })
    },
    onError: fail,
    onSettled: (_d, _e, v) => setPending((p) => { const n = { ...p }; delete n[v.doc.id]; return n }),
  })

  const deleteMut = useMutation({
    mutationFn: (doc: Doc) => del(`/api/admin/documents/${encodeURIComponent(doc.id)}`),
    onSuccess: () => { notifications.show({ color: 'teal', message: t('documents.deleted') }); void reload() },
    onError: fail,
  })

  /** Asks for confirmation before deleting a document. */
  function askDelete(doc: Doc) {
    confirmAction({
      title: t('documents.deleteTitle'),
      message: t('documents.deleteQuestion', { title: doc.title }),
      confirmLabel: t('documents.delete'),
      cancelLabel: t('ui.cancel'),
      danger: true,
      onConfirm: () => deleteMut.mutateAsync(doc).then(() => undefined, () => undefined),
    })
  }

  if (q.isError) {
    const e = q.error
    return (
      <>
        <PageHeader title={t('documents.title')} description={t('documents.intro')} />
        <Callout kind="bad" action={<Button mt="sm" size="xs" variant="default" onClick={() => void q.refetch()}>{t('ui.retry')}</Button>}>
          {e instanceof ApiError && e.status === 404 ? t('documents.notAvailable') : e instanceof ApiError && e.hasMessage ? e.message : t('documents.loadFailed')}
        </Callout>
      </>
    )
  }

  const data = q.data
  const available = data?.available !== false
  const docs = (data?.documents ?? []).map((d) => (d.id in pending ? { ...d, enabled: pending[d.id] } : d))
  const when = (iso: string) => { const d = new Date(iso); return Number.isNaN(d.getTime()) ? '' : d.toLocaleString(locale) }

  const columns: GridColumn<Doc>[] = [
    { id: 'title', title: t('documents.colTitle'), value: (r) => r.title, sortable: true, mobile: 'title', render: (r) => <Text size="sm" fw={500}>{r.title}</Text> },
    {
      id: 'status', title: t('documents.colStatus'), value: (r) => (r.enabled ? t('documents.active') : t('documents.off')), sortable: true, filter: 'multi', width: 160,
      render: (r) => (
        <Group gap="xs" wrap="nowrap" onClick={(e) => e.stopPropagation()}>
          <Switch checked={r.enabled} disabled={r.id in pending} aria-label={t('documents.switchLabel', { title: r.title })} onChange={(e) => enabledMut.mutate({ doc: r, enabled: e.currentTarget.checked })} />
          <StatusPill tone={r.enabled ? 'good' : 'neutral'}>{r.enabled ? t('documents.active') : t('documents.off')}</StatusPill>
        </Group>
      ),
    },
    { id: 'size', title: t('documents.colSize'), value: (r) => r.characters, render: (r) => <Text size="sm">{t('documents.chars', { count: r.characters.toLocaleString(locale) })}</Text>, sortable: true, align: 'right', width: 120 },
    { id: 'changed', title: t('documents.colChanged'), value: (r) => r.updatedAt, sortable: true, filter: 'date', width: 190, render: (r) => <Text size="sm" style={{ whiteSpace: 'nowrap' }}>{when(r.updatedAt)}</Text> },
    { id: 'by', title: t('documents.colUpdatedBy'), value: (r) => r.updatedBy ?? '', sortable: true, filter: 'multi', width: 180 },
    {
      id: 'actions', title: '', value: () => '', width: 96, mobile: 'hide',
      render: (r) => (
        <Group gap={2} wrap="nowrap" justify="flex-end" onClick={(e) => e.stopPropagation()}>
          <Tooltip label={t('documents.edit')}><ActionIcon variant="subtle" size="lg" aria-label={t('documents.edit')} onClick={() => setEditing(r.id)}><IconPencil size={18} /></ActionIcon></Tooltip>
          <Tooltip label={t('documents.delete')}><ActionIcon variant="subtle" color="red" size="lg" aria-label={t('documents.delete')} onClick={() => askDelete(r)}><IconTrash size={18} /></ActionIcon></Tooltip>
        </Group>
      ),
    },
  ]

  const addButton = <Button data-tour="doc-add" leftSection={<IconPlus size={16} />} disabled={!available} onClick={() => setEditing('new')}>{t('documents.addDocument')}</Button>

  return (
    <>
      <PageHeader
        title={t('documents.title')}
        description={t('documents.intro')}
        actions={docs.length > 0 ? addButton : undefined}
      />

      <Stack gap="md">
        <div data-tour="doc-hint"><Callout kind="info" action={can('Glossary.Manage') ? <Anchor component={Link} to="/glossary" size="sm" display="block" mt={4}>{t('documents.glossaryLink')}</Anchor> : undefined}>{t('documents.glossaryHint')}</Callout></div>
        {data && (!available
          ? <Callout kind="warn">{t('documents.switchedOff', { reason: data.unavailableReason || t('documents.storageUnavailable') })}</Callout>
          : <Callout kind="info">{data.searchEnabled ? t('documents.modelReads') : t('documents.modelDoesNotRead')}</Callout>)}

        <div data-tour="doc-list">
        {q.isLoading ? (
          <Skeleton height={260} radius="md" />
        ) : (
          <DataGrid<Doc>
            storageKey="documents"
            exportName="documents"
            records={docs}
            columns={columns}
            getId={(r) => r.id}
            onRowClick={(r) => setEditing(r.id)}
            emptyTitle={t('documents.emptyTitle')}
            emptyText={t('documents.emptyText')}
            emptyAction={addButton}
          />
        )}
        </div>

        {available && docs.length > 0 && <div data-tour="doc-try"><TrySearch /></div>}
      </Stack>

      <Drawer
        opened={editing !== null}
        onClose={() => setEditing(null)}
        position="right"
        size={mobile ? '100%' : 640}
        title={<Text fw={600}>{editing === 'new' ? t('documents.addTitle') : t('documents.editTitle')}</Text>}
      >
        {editing !== null && (
          <Editor
            key={editing}
            id={editing === 'new' ? null : editing}
            onDone={(message) => { setEditing(null); notifications.show({ color: 'teal', message }); void reload() }}
            onCancel={() => setEditing(null)}
            onDelete={editing === 'new' ? undefined : () => { const d = docs.find((x) => x.id === editing); if (d) { setEditing(null); askDelete(d) } }}
          />
        )}
      </Drawer>
    </>
  )
}

/** Renders the document editor form with save, cancel and delete actions. */
function Editor({ id, onDone, onCancel, onDelete }: { id: string | null; onDone: (message: string) => void; onCancel: () => void; onDelete?: () => void }) {
  const { t } = useI18n()
  const [title, setTitle] = useState('')
  const [body, setBody] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [dragging, setDragging] = useState(false)
  const warnings = useMemo(() => checkDocument(body), [body])

  const docQ = useQuery({ queryKey: ['document', id], queryFn: () => get<FullDoc>(`/api/admin/documents/${encodeURIComponent(id as string)}`), enabled: id !== null, gcTime: 0 })
  useEffect(() => { if (docQ.data) { setTitle(docQ.data.title); setBody(docQ.data.body) } }, [docQ.data])

  const save = useMutation({
    mutationFn: () => (id ? put(`/api/admin/documents/${encodeURIComponent(id)}`, { title, body }) : post('/api/admin/documents', { title, body })),
    onSuccess: () => onDone(id ? t('documents.saved') : t('documents.added')),
    onError: (e) => setError(errText(e)),
  })

  /** Submits the editor form to save the document. */
  function submit() {
    if (!title.trim()) { setError(t('documents.titleRequired')); return }
    if (!body.trim()) { setError(t('documents.bodyRequired')); return }
    setError(null)
    save.mutate()
  }

  /** Asks for confirmation before replacing existing form content with a template or file. */
  function replaceThen(apply: () => void) {
    if (!title.trim() && !body.trim()) { apply(); return }
    confirmAction({ title: t('documents.replaceTitle'), message: t('documents.replaceMessage'), confirmLabel: t('documents.replace'), cancelLabel: t('documents.cancel'), onConfirm: apply })
  }

  /** Applies an example template to the document form. */
  function applyTemplate(key: (typeof TEMPLATES)[number]) {
    replaceThen(() => { setTitle(t(`documents.tpl.${key}.title`)); setBody(t(`documents.tpl.${key}.body`)); setError(null) })
  }

  /** Reads a chosen text file into the document form. */
  async function loadFile(file: File) {
    const check = checkFile(file.name, file.size)
    if (!check.ok) { setError(t(check.reason === 'type' ? 'documents.fileType' : check.reason === 'empty' ? 'documents.fileEmpty' : 'documents.fileTooBig')); return }
    let text: string
    try { text = cleanText(await file.text()) } catch { setError(t('documents.fileReadFailed')); return }
    if (!text) { setError(t('documents.fileEmpty')); return }
    if (text.length > MAX_BODY) { setError(t('documents.fileTooLong', { count: text.length.toLocaleString(), max: MAX_BODY.toLocaleString() })); return }
    replaceThen(() => {
      setBody(text)
      if (!title.trim()) setTitle(titleFromFileName(file.name).slice(0, MAX_TITLE))
      setError(null)
      notifications.show({ color: 'teal', message: t('documents.fileLoaded') })
    })
  }

  if (id !== null && docQ.isLoading) return <Skeleton height={320} radius="md" />
  if (id !== null && docQ.isError) return <Callout kind="bad">{errText(docQ.error)}</Callout>

  return (
    <Stack gap="md">
      <Group gap="xs" wrap="wrap">
        <Menu position="bottom-start" withinPortal>
          <Menu.Target><Button data-tour="doc-form-template" variant="default" leftSection={<IconTemplate size={16} />} rightSection={<IconChevronDown size={14} />}>{t('documents.tplMenu')}</Button></Menu.Target>
          <Menu.Dropdown>
            <Menu.Label>{t('documents.tplHelp')}</Menu.Label>
            {TEMPLATES.map((k) => <Menu.Item key={k} onClick={() => applyTemplate(k)}>{t(TEMPLATE_LABELS[k])}</Menu.Item>)}
          </Menu.Dropdown>
        </Menu>
        <FileButton accept=".txt,.md,.markdown,text/plain,text/markdown" onChange={(f) => { if (f) void loadFile(f) }}>
          {(props) => <Button variant="default" leftSection={<IconFileUpload size={16} />} {...props}>{t('documents.upload')}</Button>}
        </FileButton>
      </Group>
      <TextInput wrapperProps={{ 'data-tour': 'doc-form-title' }} label={t('documents.titleLabel')} placeholder={t('documents.titlePlaceholder')} maxLength={MAX_TITLE} value={title} onChange={(e) => setTitle(e.currentTarget.value)} data-autofocus />
      <div
        data-tour="doc-form-text"
        onDragOver={(e) => { e.preventDefault(); setDragging(true) }}
        onDragLeave={() => setDragging(false)}
        onDrop={(e) => { e.preventDefault(); setDragging(false); const f = e.dataTransfer.files[0]; if (f) void loadFile(f) }}
        style={dragging ? { outline: '2px dashed var(--mantine-color-cobalt-5)', outlineOffset: 4, borderRadius: 'var(--mantine-radius-md)' } : undefined}
      >
      <Textarea
        label={t('documents.textLabel')}
        placeholder={t('documents.templateHint')}
        autosize
        minRows={12}
        maxRows={24}
        maxLength={MAX_BODY}
        value={body}
        onChange={(e) => setBody(e.currentTarget.value)}
        description={`${body.length.toLocaleString()} / ${MAX_BODY.toLocaleString()} · ${t('documents.uploadHint')}`}
      />
      </div>
      <Callout kind="info">{t('documents.noPersonalData')}</Callout>
      {warnings.length > 0 && (
        <Callout kind="warn" title={t('documents.warnTitle')}>
          <Stack gap={2}>
            {warnings.map((w) => <Text key={w} size="sm">{w === 'long' ? t('documents.warnLong', { count: body.length.toLocaleString() }) : t(WARNING_KEYS[w])}</Text>)}
          </Stack>
        </Callout>
      )}
      {error && <Callout kind="bad">{error}</Callout>}
      {save.isPending && <Text size="sm" c="dimmed">{t('documents.saving')}</Text>}
      <Group justify="space-between" wrap="wrap">
        {onDelete ? <Button variant="subtle" color="red" leftSection={<IconTrash size={16} />} onClick={onDelete}>{t('documents.delete')}</Button> : <span />}
        <Group gap="xs">
          <Button variant="default" onClick={onCancel}>{t('documents.cancel')}</Button>
          <Button data-tour="doc-form-save" loading={save.isPending} onClick={submit}>{id ? t('documents.saveChanges') : t('documents.addDocument')}</Button>
        </Group>
      </Group>
    </Stack>
  )
}

/** Renders the try-search panel that tests knowledge-base search with a question. */
function TrySearch() {
  const { t } = useI18n()
  const [question, setQuestion] = useState('')
  const search = useMutation({ mutationFn: (text: string) => post<Hit[]>('/api/admin/documents/search', { question: text }) })

  const sourceLabel = (s: string) => (s === 'both' ? t('documents.srcBoth') : s === 'vector' ? t('documents.srcVector') : t('documents.srcWords'))
  /** Runs the try-search with the typed question if it is not blank. */
  function run() { if (question.trim()) search.mutate(question) }

  return (
    <SectionCard title={t('documents.tryTitle')} description={t('documents.tryIntro')}>
      <Stack gap="sm">
        <Group gap="xs" align="flex-end" wrap="wrap">
          <TextInput
            style={{ flex: '1 1 260px' }}
            label={t('documents.questionLabel')}
            placeholder={t('documents.questionPlaceholder')}
            value={question}
            onChange={(e) => setQuestion(e.currentTarget.value)}
            onKeyDown={(e) => { if (e.key === 'Enter') run() }}
          />
          <Button leftSection={search.isPending ? <Loader size={14} color="white" /> : <IconSearch size={16} />} disabled={!question.trim() || search.isPending} onClick={run}>{t('documents.search')}</Button>
        </Group>
        {search.isError && <Callout kind="bad">{errText(search.error)}</Callout>}
        {search.data && search.data.length === 0 && <EmptyState title={t('documents.nothingFound')} />}
        {search.data?.map((hit, i) => (
          <div key={hit.documentId + i} style={{ border: '1px solid var(--mantine-color-default-border)', borderRadius: 'var(--mantine-radius-md)', padding: 'var(--mantine-spacing-sm)' }}>
            <Group gap="xs" wrap="wrap" mb={4}>
              <Text size="sm" fw={600}>{i + 1}. {hit.title}</Text>
              <Badge variant="light" tt="none" fw={500}>{sourceLabel(hit.source)}</Badge>
              <Text size="xs" c="dimmed">{t('documents.score', { score: hit.score })}</Text>
            </Group>
            <Text size="sm" style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{hit.text}</Text>
          </div>
        ))}
      </Stack>
    </SectionCard>
  )
}
