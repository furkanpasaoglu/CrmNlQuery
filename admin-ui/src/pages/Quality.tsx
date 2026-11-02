import { useEffect, useMemo, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Checkbox, Group, Progress, Select, SimpleGrid, Stack, Switch, Table, Tabs, Text } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { IconCheck, IconDownload, IconPlayerPlay, IconSparkles } from '@tabler/icons-react'
import { ApiError, downloadFile, get, post, put } from '../api/client'
import { useI18n, useT, useTOr } from '../i18n'
import { PageHeader } from '../components/PageHeader'
import { DataGrid, type GridColumn } from '../components/DataGrid'
import { Callout } from '../components/Callout'
import { KpiCard } from '../components/KpiCard'
import { SectionCard } from '../components/SectionCard'
import { StatusPill } from '../components/StatusPill'
import { confirmAction } from '../components/confirm'

type QCase = {
  id: string; question: string; entity: string; source: string; status: string; verified: boolean
  note: string | null; createdAt: string; referenceFetch: string | null
}
type CasesData = { persistent: boolean; maxCases: number; showFetchXml: boolean; cases: QCase[] }
type Line = { caseId: string; question: string; entity: string; verdict: string; detail: string; passes: number; runs: number; durationMs: number }
type Report = {
  total: number; verified: number; unverified: number; passed: number; failed: number; flaky: number; errors: number; skipped: number; unreadable: number
  accuracyPercent: number | null; dataChanged: number; hasPrevious: boolean
  regressions: Line[]; fixed: Line[]; problems: Line[]; latency: { averageMs: number; p95Ms: number; maxMs: number }
}
type Run = { id: string; startedAt: string; status: string; repeat: number; total: number; done: number; error: string | null; summary: { AccuracyPercent?: number | null; accuracyPercent?: number | null } | null }
type RunDetail = { run: Run; report: Report }
type ScheduleLast = { id: string; status: string; startedAt: string; finishedAt: string | null; total: number; accuracyPercent: number | null; regressions: number; errors: number; error: string | null }
type Schedule = { enabled: boolean; hour: number; repeat: number; runAs: string; windowHours: number; serverTime: string; nextRun: string | null; lastRun: ScheduleLast | null }
type Candidate = { entity: string; label: string }

/** Tells whether a quality case or run status counts as active. */
const isActive = (r: Run) => r.status === 'queued' || r.status === 'running'

/** Extracts a readable error message from a failed API call. */
function errText(e: unknown) { return e instanceof ApiError || e instanceof Error ? e.message : String(e) }

/** Renders the Quality check admin page with its tabs. */
export default function Quality() {
  const t = useT()
  const [tab, setTab] = useState<string | null>('cases')
  const casesQ = useQuery({ queryKey: ['quality-cases-probe'], queryFn: () => get<CasesData>('/api/admin/quality/cases?status=active'), retry: false })
  const unavailable = casesQ.error instanceof ApiError && casesQ.error.status === 404

  return (
    <>
      <PageHeader title={t('quality.title')} description={t('quality.intro')} />
      {unavailable ? (
        <Callout kind="bad">{t('quality.notAvailable')}</Callout>
      ) : (
        <Stack gap="md">
          {casesQ.data && !casesQ.data.persistent && <Callout kind="warn">{t('quality.notPersistent')}</Callout>}
          <Tabs value={tab} onChange={setTab} keepMounted={false}>
            <Tabs.List data-tour="quality-tabs">
              <Tabs.Tab value="cases">{t('quality.tabCases')}</Tabs.Tab>
              <Tabs.Tab value="generate">{t('quality.tabGenerate')}</Tabs.Tab>
              <Tabs.Tab value="run">{t('quality.tabRun')}</Tabs.Tab>
            </Tabs.List>
            <Tabs.Panel value="cases" pt="md"><CasesTab onGenerate={() => setTab('generate')} /></Tabs.Panel>
            <Tabs.Panel value="generate" pt="md"><GenerateTab onDone={() => setTab('cases')} /></Tabs.Panel>
            <Tabs.Panel value="run" pt="md"><RunTab /></Tabs.Panel>
          </Tabs>
        </Stack>
      )}
    </>
  )
}

/** Renders the Cases tab for listing, filtering and managing acceptance cases. */
function CasesTab({ onGenerate }: { onGenerate: () => void }) {
  const t = useT()
  const { locale } = useI18n()
  const qc = useQueryClient()
  const [status, setStatus] = useState<string>('active')
  const [verified, setVerified] = useState<string>('')
  const [selected, setSelected] = useState<Set<string>>(new Set())

  const qs = [status && `status=${encodeURIComponent(status)}`, verified && `verified=${verified}`].filter(Boolean).join('&')
  const q = useQuery({ queryKey: ['quality-cases', status, verified], queryFn: () => get<CasesData>('/api/admin/quality/cases' + (qs ? '?' + qs : '')) })
  const cases = q.data?.cases ?? []
  const pendingQ = useQuery({ queryKey: ['quality-cases', 'pending-count'], queryFn: () => get<CasesData>('/api/admin/quality/cases?status=pending') })
  const pendingN = pendingQ.data?.cases.length ?? 0

  const act = useMutation({
    mutationFn: (path: string) => post<{ changed: number }>('/api/admin/quality/' + path, { ids: Array.from(selected) }),
    onSuccess: (r) => {
      notifications.show({ color: 'teal', message: t('quality.changed', { n: r.changed }) })
      setSelected(new Set())
      void qc.invalidateQueries({ queryKey: ['quality-cases'] })
    },
    onError: (e) => notifications.show({ color: 'red', message: errText(e) }),
  })

  const verifiedN = cases.filter((c) => c.verified && c.status !== 'retired').length
  const retiredN = cases.filter((c) => c.status === 'retired').length
  const stateOf = (c: QCase) => (c.status === 'retired' ? t('quality.stateRetired') : c.status === 'pending' ? t('quality.statePending') : c.verified ? t('quality.stateVerified') : t('quality.stateUnverified'))
  const toggle = (id: string, on: boolean) => setSelected((s) => { const n = new Set(s); if (on) n.add(id); else n.delete(id); return n })
  const allOn = cases.length > 0 && cases.every((c) => selected.has(c.id))
  const none = selected.size === 0

  const columns: GridColumn<QCase>[] = [
    {
      id: 'pick', title: t('quality.pick'), value: (r) => (selected.has(r.id) ? '1' : ''), width: 70,
      render: (r) => <Checkbox aria-label={t('quality.pick')} checked={selected.has(r.id)} onChange={(e) => toggle(r.id, e.currentTarget.checked)} />,
    },
    {
      id: 'question', title: t('quality.colQuestion'), value: (r) => r.question, sortable: true, mobile: 'title',
      render: (r) => (
        <Stack gap={2}>
          <Text size="sm">{r.question}</Text>
          {r.note && <Text size="xs" c="dimmed">{r.note}</Text>}
          {r.referenceFetch && (
            <details>
              <summary style={{ cursor: 'pointer', fontSize: 'var(--mantine-font-size-xs)' }}>{t('quality.reference')}</summary>
              <pre style={{ whiteSpace: 'pre-wrap', fontSize: 'var(--mantine-font-size-xs)', margin: '4px 0 0' }}>{r.referenceFetch}</pre>
            </details>
          )}
        </Stack>
      ),
    },
    { id: 'entity', title: t('quality.colTable'), value: (r) => r.entity, sortable: true, filter: 'multi', width: 150 },
    { id: 'source', title: t('quality.colSource'), value: (r) => (r.source === 'marked' ? t('quality.sourceMarked') : r.source === 'suggested' ? t('quality.sourceSuggested') : t('quality.sourceGenerated')), sortable: true, filter: 'multi', width: 170 },
    {
      id: 'state', title: t('quality.colState'), value: (r) => stateOf(r), sortable: true, filter: 'multi', width: 140,
      render: (r) => <StatusPill tone={r.status === 'retired' ? 'neutral' : r.status === 'pending' ? 'warn' : r.verified ? 'good' : 'warn'}>{stateOf(r)}</StatusPill>,
    },
    { id: 'created', title: t('quality.colCreated'), value: (r) => r.createdAt, sortable: true, hidden: true, render: (r) => new Date(r.createdAt).toLocaleDateString(locale) },
  ]

  return (
    <Stack gap="sm">
      {q.isError && <Callout kind="bad" action={<Button mt="xs" size="xs" variant="default" onClick={() => void q.refetch()}>{t('ui.retry')}</Button>}>{errText(q.error)}</Callout>}
      {pendingN > 0 && status !== 'pending' && (
        <Callout kind="info" action={<Button mt="xs" size="xs" variant="default" onClick={() => { setStatus('pending'); setSelected(new Set()) }}>{t('quality.pendingShow')}</Button>}>{t('quality.pendingNotice', { n: pendingN })}</Callout>
      )}
      <Group gap="xs" wrap="wrap" align="flex-end">
        <Select label={t('quality.statusFilter')} w={150} allowDeselect={false} value={status} onChange={(v) => { setStatus(v ?? ''); setSelected(new Set()) }}
          data={[{ value: '', label: t('quality.optAll') }, { value: 'active', label: t('quality.optActive') }, { value: 'pending', label: t('quality.optPending') }, { value: 'retired', label: t('quality.optRetired') }]} />
        <Select label={t('quality.verifiedFilter')} w={170} allowDeselect={false} value={verified} onChange={(v) => { setVerified(v ?? ''); setSelected(new Set()) }}
          data={[{ value: '', label: t('quality.optAll') }, { value: 'true', label: t('quality.optVerified') }, { value: 'false', label: t('quality.optUnverified') }]} />
        {q.data && <Text size="sm" c="dimmed" pb={6}>{t('quality.summary', { total: cases.length, verified: verifiedN, unverified: cases.length - verifiedN - retiredN, retired: retiredN })} {t('quality.limit', { max: q.data.maxCases })}</Text>}
      </Group>

      <Group gap="xs" wrap="wrap">
        <Checkbox label={t('quality.pickAll')} checked={allOn} indeterminate={!allOn && !none} onChange={(e) => setSelected(e.currentTarget.checked ? new Set(cases.map((c) => c.id)) : new Set())} />
        <Text size="sm" c="dimmed">{t('quality.selectedN', { n: selected.size })}</Text>
        <Button size="xs" leftSection={<IconCheck size={14} />} disabled={none} loading={act.isPending && act.variables === 'approve'} onClick={() => act.mutate('approve')}>{t('quality.approve')}</Button>
        <Button size="xs" variant="default" disabled={none} onClick={() => act.mutate('retire')}>{t('quality.retire')}</Button>
        <Button size="xs" variant="default" disabled={none} onClick={() => act.mutate('restore')}>{t('quality.restore')}</Button>
        <Button size="xs" variant="default" color="red" disabled={none}
          onClick={() => confirmAction({ title: t('quality.delete'), message: t('quality.deleteConfirm'), confirmLabel: t('quality.delete'), cancelLabel: t('ui.cancel'), danger: true, onConfirm: () => act.mutate('delete') })}>
          {t('quality.delete')}
        </Button>
        <Button size="xs" variant="default" leftSection={<IconDownload size={14} />} title={t('quality.exportHelp')} onClick={() => void downloadFile('/api/admin/quality/export', 'acceptance-set.json').catch((e) => notifications.show({ color: 'red', message: e instanceof Error ? e.message : String(e) }))}>{t('quality.export')}</Button>
      </Group>

      <DataGrid<QCase>
        storageKey="quality-cases"
        exportName="acceptance-set"
        records={cases}
        loading={q.isLoading}
        getId={(r) => r.id}
        columns={columns}
        emptyTitle={t('quality.none')}
        emptyAction={<Button size="xs" leftSection={<IconSparkles size={14} />} onClick={onGenerate}>{t('quality.generateButton')}</Button>}
      />
    </Stack>
  )
}

/** Renders the tab that generates acceptance cases from the schema. */
function GenerateTab({ onDone }: { onDone: () => void }) {
  const t = useT()
  const tOr = useTOr()
  const { lang } = useI18n()
  const qc = useQueryClient()
  const q = useQuery({ queryKey: ['quality-entities'], queryFn: () => get<Candidate[]>('/api/admin/quality/entities') })
  const list = q.data ?? []
  const [picked, setPicked] = useState<string[] | null>(null)
  const chosen = picked ?? list.map((c) => c.entity)
  const [step, setStep] = useState<{ label: string; n: number; total: number } | null>(null)
  const [result, setResult] = useState<{ text: string; bad: boolean } | null>(null)

  const run = useMutation({
    mutationFn: async () => {
      let added = 0, dup = 0
      const notes: string[] = []
      const chosenList = list.filter((c) => chosen.includes(c.entity))
      for (let i = 0; i < chosenList.length; i++) {
        const c = chosenList[i]
        setStep({ label: c.label, n: i + 1, total: chosenList.length })
        const r = await post<{ added: number; duplicates: number; reason: string | null }>('/api/admin/quality/generate', { entity: c.entity, language: lang })
        added += r.added; dup += r.duplicates
        if (r.reason) notes.push(t('quality.generateNone', { entity: c.label, reason: tOr('quality.reason_' + r.reason, r.reason) }))
      }
      return [t('quality.generateDone', { added, duplicates: dup }), ...notes].join(' ')
    },
    onMutate: () => setResult(null),
    onSuccess: (text) => { setResult({ text, bad: false }); void qc.invalidateQueries({ queryKey: ['quality-cases'] }) },
    onError: (e) => { setResult({ text: errText(e), bad: true }); void qc.invalidateQueries({ queryKey: ['quality-cases'] }) },
    onSettled: () => setStep(null),
  })

  return (
    <SectionCard title={t('quality.generateTitle')} description={t('quality.generateHelp')}>
      <Stack gap="md">
        {q.isError && <Callout kind="bad">{errText(q.error)}</Callout>}
        {q.isSuccess && list.length === 0 && <Text c="dimmed" size="sm">{t('quality.noCandidates')}</Text>}
        {list.length > 0 && (
          <Checkbox.Group value={chosen} onChange={setPicked} label={t('quality.pickTables')}>
            <SimpleGrid cols={{ base: 1, xs: 2, md: 3 }} mt="xs" spacing="xs">
              {list.map((c) => <Checkbox key={c.entity} value={c.entity} label={c.label} />)}
            </SimpleGrid>
          </Checkbox.Group>
        )}
        {step && (
          <Stack gap={4} role="status">
            <Text size="sm">{t('quality.generating', { entity: step.label, n: step.n, total: step.total })}</Text>
            <Progress value={(100 * (step.n - 1)) / step.total} animated />
          </Stack>
        )}
        {result && <Callout kind={result.bad ? 'bad' : 'info'} action={!result.bad ? <Button mt="xs" size="xs" variant="default" onClick={onDone}>{t('quality.reviewCases')}</Button> : undefined}>{result.text}</Callout>}
        <Group>
          <Button leftSection={<IconSparkles size={16} />} disabled={chosen.length === 0} loading={run.isPending} onClick={() => run.mutate()}>{t('quality.generateButton')}</Button>
        </Group>
      </Stack>
    </SectionCard>
  )
}

/** Renders the tab that starts, watches and cancels quality runs. */
function RunTab() {
  const t = useT()
  const tOr = useTOr()
  const { locale } = useI18n()
  const qc = useQueryClient()
  const [repeat, setRepeat] = useState<string>('1')
  const [viewId, setViewId] = useState<string | null>(null)
  const prevActive = useRef<string | null>(null)

  const runsQ = useQuery({
    queryKey: ['quality-runs'],
    queryFn: () => get<Run[]>('/api/admin/quality/runs'),
    refetchInterval: (query) => ((query.state.data ?? []).some(isActive) ? 3000 : false),
  })
  const runs = runsQ.data ?? []
  const active = runs.find(isActive) ?? null

  useEffect(() => {
    if (!runsQ.data) return
    if (active) { prevActive.current = active.id; return }
    if (prevActive.current) {
      const fin = runs.find((r) => r.id === prevActive.current)
      prevActive.current = null
      if (fin) { notifications.show({ color: fin.status === 'done' ? 'teal' : 'red', message: t('quality.finished_' + fin.status) }); setViewId(fin.id) }
    } else if (!viewId && runs.length) setViewId(runs[0].id)
  }, [runsQ.data]) // eslint-disable-line react-hooks/exhaustive-deps

  const detail = useQuery({ queryKey: ['quality-run', viewId], queryFn: () => get<RunDetail>(`/api/admin/quality/runs/${encodeURIComponent(viewId ?? '')}`), enabled: Boolean(viewId) })

  const start = useMutation({
    mutationFn: () => post('/api/admin/quality/runs', { repeat: Number(repeat) }),
    onSuccess: () => { setViewId(null); void qc.invalidateQueries({ queryKey: ['quality-runs'] }) },
    onError: (e) => notifications.show({ color: 'red', message: errText(e) }),
  })
  const cancel = useMutation({
    mutationFn: (id: string) => post(`/api/admin/quality/runs/${encodeURIComponent(id)}/cancel`),
    onSuccess: () => { notifications.show({ color: 'yellow', message: t('quality.cancelling') }); void qc.invalidateQueries({ queryKey: ['quality-runs'] }) },
    onError: (e) => notifications.show({ color: 'red', message: errText(e) }),
  })

  const when = (v: string) => new Date(v).toLocaleString(locale)
  const pct = (v: number | null | undefined) => (v === null || v === undefined ? '–' : v.toLocaleString(locale, { maximumFractionDigits: 1 }) + '%')
  const accOf = (r: Run) => r.summary?.AccuracyPercent ?? r.summary?.accuracyPercent

  return (
    <Stack gap="md">
      <ScheduleCard />
      <SectionCard title={t('quality.runTitle')} description={t('quality.runHelp')}>
        <Stack gap="sm">
          <Group align="flex-end" gap="sm" wrap="wrap">
            <Select label={t('quality.repeat')} description={t('quality.repeatHelp')} w={170} allowDeselect={false} data={['1', '2', '3']} value={repeat} onChange={(v) => setRepeat(v ?? '1')} disabled={Boolean(active)} />
            {active
              ? <Button variant="default" color="red" loading={cancel.isPending} onClick={() => cancel.mutate(active.id)}>{t('quality.runCancel')}</Button>
              : <Button leftSection={<IconPlayerPlay size={16} />} loading={start.isPending} onClick={() => start.mutate()}>{t('quality.runStart')}</Button>}
          </Group>
          {active && (
            <Stack gap={4} role="status">
              <Text size="sm">{t('quality.running', { done: active.done, total: active.total, repeat: active.repeat })}</Text>
              <Progress value={active.total ? (100 * active.done) / active.total : 0} animated />
            </Stack>
          )}
        </Stack>
      </SectionCard>

      {detail.isError && <Callout kind="bad">{errText(detail.error)}</Callout>}
      {detail.data && <ReportView data={detail.data} when={when} pct={pct} />}
      {!viewId && !active && runs.length === 0 && runsQ.isSuccess && <Text c="dimmed" size="sm">{t('quality.runNone')}</Text>}

      {runs.length > 0 && (
        <SectionCard title={t('quality.runHistory')} flush>
          <Table.ScrollContainer minWidth={420}>
            <Table verticalSpacing="xs" highlightOnHover>
              <Table.Tbody>
                {runs.map((r) => (
                  <Table.Tr key={r.id}>
                    <Table.Td><Text size="sm">{when(r.startedAt)}</Text></Table.Td>
                    <Table.Td><StatusPill tone={r.status === 'done' ? 'good' : r.status === 'failed' ? 'bad' : isActive(r) ? 'warn' : 'neutral'}>{tOr('quality.run_' + r.status, r.status)}</StatusPill></Table.Td>
                    <Table.Td><Text size="sm" c="dimmed">{r.done}/{r.total} ×{r.repeat}</Text></Table.Td>
                    <Table.Td><Text size="sm">{pct(accOf(r))}</Text></Table.Td>
                    <Table.Td ta="right"><Button size="compact-sm" variant={r.id === viewId ? 'light' : 'default'} onClick={() => setViewId(r.id)}>{t('quality.runView')}</Button></Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
        </SectionCard>
      )}
    </Stack>
  )
}

/** Renders a quality run report with accuracy, regressions and problem cases. */
function ReportView({ data, when, pct }: { data: RunDetail; when: (v: string) => string; pct: (v: number | null | undefined) => string }) {
  const t = useT()
  const { locale } = useI18n()
  const { run, report: r } = data
  const sec = (ms: number) => (ms / 1000).toLocaleString(locale, { maximumFractionDigits: 1, minimumFractionDigits: 1 })
  const counted = r.passed + r.failed + r.flaky + r.errors

  const slowest = useMemo(() => {
    const byId = new Map<string, Line>()
    for (const l of [...r.regressions, ...r.fixed, ...r.problems]) byId.set(l.caseId, l)
    return Array.from(byId.values()).sort((a, b) => b.durationMs - a.durationMs).slice(0, 5)
  }, [r])

  return (
    <Stack gap="md">
      <Text fw={600}>{t('quality.reportTitle', { when: when(run.startedAt) })}</Text>
      {run.error && <Callout kind="bad">{run.error}</Callout>}
      <SimpleGrid cols={{ base: 1, xs: 2, md: 4 }}>
        <KpiCard label={t('quality.accuracyLabel')} value={pct(r.accuracyPercent)} tone={r.accuracyPercent === null ? undefined : r.accuracyPercent >= 90 ? 'good' : r.accuracyPercent >= 70 ? 'warn' : 'bad'}
          hint={r.accuracyPercent === null ? t('quality.noAccuracy') : t('quality.accuracy', { percent: pct(r.accuracyPercent), passed: r.passed, counted })} />
        <KpiCard label={t('quality.verdict_pass')} value={r.passed} tone="good" />
        <KpiCard label={t('quality.verdict_fail')} value={r.failed + r.flaky + r.errors} tone={r.failed + r.flaky + r.errors > 0 ? 'bad' : undefined}
          hint={t('quality.failBreakdown', { failed: r.failed, flaky: r.flaky, errors: r.errors })} />
        <KpiCard label={t('quality.latencyLabel')} value={`${sec(r.latency.averageMs)} s`} hint={t('quality.latency', { avg: sec(r.latency.averageMs), p95: sec(r.latency.p95Ms), max: sec(r.latency.maxMs) })} />
      </SimpleGrid>
      <Text size="sm" c="dimmed">{t('quality.counts', { passed: r.passed, failed: r.failed, flaky: r.flaky, errors: r.errors, skipped: r.skipped, unreadable: r.unreadable, unverified: r.unverified })}</Text>
      <Stack gap="xs">
        {r.unverified > 0 && <Callout kind="info">{t('quality.unverifiedNote')}</Callout>}
        {r.skipped > 0 && <Callout kind="info">{t('quality.skippedNote')}</Callout>}
        {r.unreadable > 0 && <Callout kind="info">{t('quality.unreadableNote')}</Callout>}
        {r.dataChanged > 0 && <Callout kind="info">{t('quality.dataChangedNote', { n: r.dataChanged })}</Callout>}
        {!r.hasPrevious && <Callout kind="info">{t('quality.firstRunNote')}</Callout>}
        {r.hasPrevious && r.regressions.length === 0 && <Callout kind="info" title={t('quality.regressions')}>{t('quality.noRegressions')}</Callout>}
      </Stack>
      {r.regressions.length > 0 && <Lines title={t('quality.regressions')} list={r.regressions} danger />}
      {r.fixed.length > 0 && <Lines title={t('quality.fixed')} list={r.fixed} />}
      {r.problems.length > 0 && <Lines title={t('quality.problems')} list={r.problems} />}
      {slowest.length > 0 && <Lines title={t('quality.slowest')} list={slowest} showTime />}
    </Stack>
  )
}

/** Renders a list of lines as simple text blocks. */
function Lines({ title, list, danger, showTime }: { title: string; list: Line[]; danger?: boolean; showTime?: boolean }) {
  const t = useT()
  const tOr = useTOr()
  const { locale } = useI18n()
  return (
    <SectionCard title={`${title} (${list.length})`} flush>
      <Table.ScrollContainer minWidth={520}>
        <Table verticalSpacing="xs">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>{t('quality.colQuestion')}</Table.Th>
              <Table.Th>{t('quality.colTable')}</Table.Th>
              <Table.Th>{t('quality.colVerdict')}</Table.Th>
              <Table.Th>{showTime ? t('quality.colTime') : t('quality.colDetail')}</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {list.map((l) => (
              <Table.Tr key={l.caseId}>
                <Table.Td>{l.question}</Table.Td>
                <Table.Td>{l.entity}</Table.Td>
                <Table.Td>
                  <Badge variant="light" color={danger || l.verdict === 'fail' || l.verdict === 'error' ? 'red' : l.verdict === 'pass' ? 'teal' : 'gray'} tt="none">
                    {tOr('quality.verdict_' + l.verdict, l.verdict)}{l.runs > 1 ? ` (${l.passes}/${l.runs})` : ''}
                  </Badge>
                </Table.Td>
                <Table.Td>{showTime ? `${(l.durationMs / 1000).toLocaleString(locale, { maximumFractionDigits: 1 })} s` : l.detail}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
    </SectionCard>
  )
}

/** Renders the card for the daily scheduled quality check settings and last run. */
function ScheduleCard() {
  const t = useT()
  const tOr = useTOr()
  const { locale } = useI18n()
  const qc = useQueryClient()
  const q = useQuery({ queryKey: ['quality-schedule'], queryFn: () => get<Schedule>('/api/admin/quality/schedule') })
  const [enabled, setEnabled] = useState(false)
  const [hour, setHour] = useState('3')
  const [repeat, setRepeat] = useState('1')
  const loaded = useRef(false)

  useEffect(() => {
    if (!q.data || loaded.current) return
    loaded.current = true
    setEnabled(q.data.enabled)
    setHour(String(q.data.hour))
    setRepeat(String(q.data.repeat))
  }, [q.data])

  const save = useMutation({
    mutationFn: () => put<Schedule>('/api/admin/quality/schedule', { enabled, hour: Number(hour), repeat: Number(repeat) }),
    onSuccess: (data) => { qc.setQueryData(['quality-schedule'], data); notifications.show({ color: 'teal', message: t('quality.schedSaved') }) },
    onError: (e) => notifications.show({ color: 'red', message: errText(e) }),
  })

  const data = q.data
  const last = data?.lastRun ?? null
  const problem = last !== null && (last.status !== 'done' || last.regressions > 0 || last.errors > 0)
  const pct = (v: number | null | undefined) => (v === null || v === undefined ? '–' : v.toLocaleString(locale, { maximumFractionDigits: 1 }) + '%')

  return (
    <SectionCard title={t('quality.schedTitle')} description={t('quality.schedHelp')}>
      {q.isError ? <Callout kind="bad">{errText(q.error)}</Callout> : (
        <Stack gap="sm">
          <Group align="flex-end" gap="md" wrap="wrap">
            <Switch label={t('quality.schedEnabled')} checked={enabled} onChange={(e) => setEnabled(e.currentTarget.checked)} disabled={!data} />
            <Select label={t('quality.schedHour')} w={150} allowDeselect={false} disabled={!data || !enabled}
              data={Array.from({ length: 24 }, (_, h) => ({ value: String(h), label: t('quality.runAllHours', { hour: String(h).padStart(2, '0') }) }))} value={hour} onChange={(v) => setHour(v ?? '3')} />
            <Select label={t('quality.repeat')} w={110} allowDeselect={false} disabled={!data || !enabled} data={['1', '2', '3']} value={repeat} onChange={(v) => setRepeat(v ?? '1')} />
            <Button loading={save.isPending} disabled={!data} onClick={() => save.mutate()}>{t('quality.schedSave')}</Button>
          </Group>
          {data && (
            <Stack gap={2}>
              <Text size="sm" c="dimmed">{t('quality.schedHourHelp', { n: data.windowHours })}</Text>
              <Text size="sm" c="dimmed">{data.enabled && data.runAs ? t('quality.schedRunAs', { account: data.runAs }) : t('quality.schedRunAsNone')}</Text>
              <Text size="sm" c="dimmed">{data.enabled && data.nextRun ? t('quality.schedNext', { when: data.nextRun }) : t('quality.schedOff')}</Text>
              {last === null
                ? <Text size="sm" c="dimmed">{t('quality.schedNever')}</Text>
                : (
                  <Group gap="xs">
                    <Text size="sm">{t('quality.schedLast', { when: new Date(last.startedAt).toLocaleString(locale) })}</Text>
                    <StatusPill tone={last.status === 'done' && !problem ? 'good' : problem ? 'bad' : 'neutral'}>{tOr('quality.run_' + last.status, last.status)}</StatusPill>
                    {last.status === 'done' && <Text size="sm" c="dimmed">{t('quality.schedLastResult', { accuracy: pct(last.accuracyPercent), regressions: last.regressions, errors: last.errors })}</Text>}
                  </Group>
                )}
              {problem && <Callout kind="warn">{t('quality.schedLastProblem')}</Callout>}
            </Stack>
          )}
        </Stack>
      )}
    </SectionCard>
  )
}
