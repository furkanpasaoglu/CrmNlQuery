import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { AreaChart, BarChart } from '@mantine/charts'
import { Accordion, Button, Center, Group, Loader, Progress, SegmentedControl, SimpleGrid, Skeleton, Stack, Table, Text } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { IconActivity, IconAlertTriangle, IconBook2, IconClockHour4, IconCoin, IconMessages, IconRefresh, IconThumbUp } from '@tabler/icons-react'
import { ApiError, get, post } from '../api/client'
import { useMe } from '../auth/me'
import { useI18n, useTOr } from '../i18n'
import { PageHeader } from '../components/PageHeader'
import { KpiCard } from '../components/KpiCard'
import { SectionCard } from '../components/SectionCard'
import { HealthPipeline, type HealthItem } from '../components/HealthPipeline'
import { Callout } from '../components/Callout'

type Report = {
  requests: number; answers: number; questions: number; failures: number; cancelled: number; failureRate: number
  requestP95Ms: number; requestP50Ms: number; totalCostUsd: number | null; costIsPartial: boolean
  aiCallsPerRequest: number; aiCalls: number; aiErrorRate: number; inputTokens: number; outputTokens: number
  blocked: number; blockedByType: { key: string; count: number }[]
  knowledgeSearched: number; knowledgeUsed: number; knowledgeUsedRate: number; knowledgeAveragePieces: number
  followUps: number; followUpRate: number
  days: { day: string; requests: number; failures: number; inputTokens: number; outputTokens: number }[]
  latency: { purpose: string; calls: number; errors: number; p50Ms: number; p95Ms: number; avgEstTokens: number | null }[]
  failuresByType: { key: string; count: number }[]
  models: { provider: string; model: string; calls: number; inputTokens: number; outputTokens: number; estimatedCostUsd: number | null }[]
  users: { user: string; name: string | null; requests: number }[]
}
type Feedback = { enabled: boolean; up: number; down: number; reasons: { reason: string; count: number }[] }
type Suggestions = { kinds: { kind: string; shown: number; clicked: number }[] }
type ReportUse = { enabled: boolean; shown: number; opened: number; top: { name: string; shown: number; opened: number }[] }
type Glossary = { enabled: boolean; entries: number; broken: number; notApplied: number }

/** Awaits a promise and returns its value, or null if it fails. */
const settle = async <T,>(p: Promise<T>): Promise<T | null> => { try { return await p } catch { return null } }

/** Renders the admin dashboard page with usage, health and report boxes. */
export default function Dashboard() {
  const { t, locale } = useI18n()
  const tOr = useTOr()
  const { can } = useMe()
  const [days, setDays] = useState('7')

  const report = useQuery({ queryKey: ['report', days], queryFn: () => get<Report>(`/api/admin/report?days=${days}`) })
  const health = useQuery({ queryKey: ['health'], queryFn: () => get<HealthItem[]>('/api/admin/health'), refetchInterval: 30_000 })
  const extras = useQuery({
    queryKey: ['dashboard-extras', days],
    queryFn: async () => {
      const [fb, sg, gl, rp] = await Promise.all([
        settle(get<Feedback>(`/api/admin/feedback/summary?days=${days}`)),
        settle(get<Suggestions>(`/api/admin/suggestions/summary?days=${days}`)),
        settle(get<Glossary>('/api/admin/glossary/summary')),
        settle(get<ReportUse>(`/api/admin/reports/summary?days=${days}`)),
      ])
      return { fb, sg, gl, rp }
    },
  })
  const refreshMeta = useMutation({
    mutationFn: () => post('/api/admin/metadata/refresh'),
    onSuccess: () => notifications.show({ color: 'teal', message: t('dashboard.metaCleared') }),
    onError: (e) => notifications.show({ color: 'red', message: e instanceof ApiError ? e.message : String(e) }),
  })

  const nf = new Intl.NumberFormat(locale)
  const dec = (n: number, d = 1) => n.toLocaleString(locale, { minimumFractionDigits: d, maximumFractionDigits: d })
  const pct = (r: number) => `${dec(r * 100, r > 0 && r < 0.1 ? 1 : 0)}%`
  const dur = (ms: number) => (ms < 1000 ? `${Math.round(ms)} ms` : `${dec(ms / 1000)} s`)
  const compact = (n: number) => new Intl.NumberFormat(locale, { notation: 'compact', maximumFractionDigits: 1 }).format(n)
  const money = (v: number | null) => (v == null ? t('dashboard.na') : `$${v < 1 ? dec(v, 3) : dec(v, 2)}`)

  const r = report.data
  const items = health.data ?? []
  const allOk = items.length > 0 && items.every((h) => h.ok)
  const { fb, sg, gl, rp } = extras.data ?? { fb: null, sg: null, gl: null, rp: null }

  const failedLabel = (key: string) => tOr('dashboard.failure.' + key, key)
  const purpose = (key: string) => tOr('dashboard.purpose.' + key, key)
  const maxFail = Math.max(1, ...(r?.failuresByType.map((f) => f.count) ?? [1]))

  return (
    <>
      <PageHeader
        title={t('dashboard.title')}
        description={t('dashboard.intro')}
        actions={
          <>
            <SegmentedControl
              data-tour="dash-range"
              value={days}
              onChange={setDays}
              data={['7', '30', '90'].map((d) => ({ value: d, label: t('dashboard.nDays', { n: d }) }))}
              aria-label={t('dashboard.period')}
            />
            <Button variant="default" leftSection={<IconRefresh size={16} />} loading={report.isFetching} onClick={() => { void report.refetch(); void health.refetch(); void extras.refetch() }}>
              {t('dashboard.refresh')}
            </Button>
            {can('Metadata.Refresh') && (
              <Button variant="light" loading={refreshMeta.isPending} onClick={() => refreshMeta.mutate()}>{t('dashboard.refreshMeta')}</Button>
            )}
          </>
        }
      />

      <Stack gap="lg">
        <div data-tour="dash-health">{health.isLoading ? <Skeleton h={120} radius="md" /> : (
          <HealthPipeline items={items} allOk={allOk} title={t('dashboard.health')} okText={t('dashboard.statusAllOk')} failText={t('dashboard.statusProblem')} />
        )}</div>

        {report.isError && (
          <Callout kind="bad" title={t('dashboard.reportFailed', { status: report.error instanceof ApiError ? report.error.status : 0 })}>
            {report.error instanceof ApiError && report.error.status === 404 ? t('dashboard.reportUnavailable') : (report.error as Error).message}
          </Callout>
        )}

        {report.isLoading && <Center py={60}><Loader size="sm" /></Center>}

        {r && (
          <>
            <SimpleGrid cols={{ base: 1, xs: 2, lg: 4 }} data-tour="dash-kpis">
              <KpiCard
                icon={<IconMessages size={18} />}
                label={t('dashboard.questions')}
                value={nf.format(r.requests)}
                hint={<>{t('dashboard.answeredLine', { n: r.answers })}<br />{t('dashboard.failedLine', { n: r.failures })}{r.cancelled ? <><br />{t('dashboard.stopped', { n: r.cancelled })}</> : null}</>}
              />
              <KpiCard
                icon={<IconAlertTriangle size={18} />}
                label={t('dashboard.failureRate')}
                value={pct(r.failureRate)}
                tone={r.requests === 0 ? undefined : r.failureRate > 0.1 ? 'bad' : r.failureRate > 0.03 ? 'warn' : 'good'}
                hint={t('dashboard.failuresOf', { failed: r.failures, total: r.requests - (r.cancelled || 0) })}
              />
              <KpiCard
                icon={<IconClockHour4 size={18} />}
                label={t('dashboard.responseP95')}
                value={dur(r.requestP95Ms)}
                tone={r.requestP95Ms > 100_000 ? 'warn' : undefined}
                hint={t('dashboard.median', { v: dur(r.requestP50Ms) })}
              />
              <KpiCard
                icon={<IconCoin size={18} />}
                label={t('dashboard.estCost')}
                value={r.totalCostUsd == null ? t('dashboard.na') : `~${money(r.totalCostUsd)}`}
                hint={r.costIsPartial ? t('dashboard.costPartial') : t('dashboard.costEstimate')}
              />
            </SimpleGrid>

            <SimpleGrid cols={{ base: 1, lg: 2 }}>
              <SectionCard title={t('dashboard.perDayTitle')} description={t('dashboard.perDaySub')}>
                {r.requests === 0 ? <Text c="dimmed" size="sm" py="md">{t('dashboard.noData')}</Text> : (
                  <BarChart
                    h={240}
                    data={r.days.map((d) => ({ day: d.day.slice(5), [t('dashboard.succeeded')]: d.requests - d.failures, [t('dashboard.failed')]: d.failures }))}
                    dataKey="day"
                    type="stacked"
                    series={[{ name: t('dashboard.succeeded'), color: 'cobalt.6' }, { name: t('dashboard.failed'), color: 'orange.6' }]}
                    withLegend
                    legendProps={{ verticalAlign: 'bottom', height: 28 }}
                    tickLine="y"
                    gridAxis="y"
                  />
                )}
              </SectionCard>
              <SectionCard title={t('dashboard.whyFailedTitle')} description={t('dashboard.whyFailedSub')}>
                {r.failuresByType.length === 0 ? <Text c="dimmed" size="sm" py="md">{t('dashboard.noData')}</Text> : (
                  <Stack gap="sm">
                    {r.failuresByType.map((f) => (
                      <div key={f.key}>
                        <Group justify="space-between" mb={3} wrap="nowrap">
                          <Text size="sm">{failedLabel(f.key)}</Text>
                          <Text size="sm" fw={600}>{f.count}</Text>
                        </Group>
                        <Progress value={(f.count / maxFail) * 100} color="orange" size="sm" />
                      </div>
                    ))}
                  </Stack>
                )}
              </SectionCard>
            </SimpleGrid>

            <div>
              <Text fw={600} mb="xs">{t('dashboard.featuresTitle')}</Text>
              <SimpleGrid cols={{ base: 1, xs: 2, lg: 3 }}>
                <KpiCard
                  icon={<IconThumbUp size={18} />}
                  label={t('dashboard.feedback')}
                  value={!fb ? t('dashboard.na') : fb.up + fb.down === 0 ? t('dashboard.noneYet') : `👍 ${fb.up}   👎 ${fb.down}`}
                  hint={!fb ? t('dashboard.notAvailable') : !fb.enabled && fb.up + fb.down === 0 ? t('dashboard.feedbackOff') : fb.up + fb.down === 0 ? t('dashboard.feedbackNone') : fb.down ? t('dashboard.thumbsDown', { reasons: fb.reasons.slice(0, 2).map((x) => `${tOr('dashboard.reason.' + x.reason, x.reason)} ${x.count}`).join(', ') || t('dashboard.noReason') }) : t('dashboard.noThumbsDown')}
                />
                <KpiCard
                  label={t('dashboard.suggestions')}
                  value={(() => { const shown = sg?.kinds.reduce((a, k) => a + k.shown, 0) ?? 0; const clicked = sg?.kinds.reduce((a, k) => a + k.clicked, 0) ?? 0; return !sg ? t('dashboard.na') : shown === 0 ? t('dashboard.noneYet') : t('dashboard.clickedPct', { pct: pct(clicked / shown) }) })()}
                  hint={!sg ? t('dashboard.notAvailable') : sg.kinds.every((k) => k.shown === 0) ? t('dashboard.suggestionsNone') : sg.kinds.filter((k) => k.shown > 0).map((k) => `${tOr('dashboard.kind.' + k.kind, k.kind)} ${k.clicked}/${k.shown}`).join(', ')}
                />
                <KpiCard
                  label={t('dashboard.reports')}
                  value={!rp ? t('dashboard.na') : !rp.enabled && rp.shown === 0 ? t('dashboard.off') : rp.shown === 0 ? t('dashboard.noneYet') : t('dashboard.reportsOpened', { opened: rp.opened, shown: rp.shown })}
                  hint={!rp ? t('dashboard.notAvailable') : rp.top.length === 0 ? t('dashboard.reportsNone') : rp.top.slice(0, 3).map((x) => `${x.name} ${x.opened}/${x.shown}`).join(', ')}
                />
                <KpiCard
                  label={t('dashboard.followUps')}
                  value={r.answers === 0 ? t('dashboard.na') : pct(r.followUpRate)}
                  hint={r.answers === 0 ? t('dashboard.noAnswered') : t('dashboard.followUpsSub', { n: r.followUps, total: r.answers })}
                />
                <KpiCard
                  icon={<IconBook2 size={18} />}
                  label={t('dashboard.glossary')}
                  value={!gl ? t('dashboard.na') : !gl.enabled ? t('dashboard.off') : gl.entries === 0 ? t('dashboard.noneYet') : gl.broken ? t('dashboard.glossaryBroken', { n: gl.broken }) : t('dashboard.glossaryOk')}
                  tone={gl && gl.enabled && gl.entries > 0 ? (gl.broken ? 'bad' : 'good') : undefined}
                  hint={!gl ? t('dashboard.notAvailable') : !gl.enabled ? t('dashboard.glossaryOff') : gl.entries === 0 ? t('dashboard.glossaryNone') : t('dashboard.glossarySub', { entries: gl.entries, failed: gl.notApplied })}
                />
                <KpiCard
                  icon={<IconActivity size={18} />}
                  label={t('dashboard.knowledge')}
                  value={r.knowledgeSearched === 0 ? t('dashboard.off') : pct(r.knowledgeUsedRate)}
                  hint={r.knowledgeSearched === 0 ? t('dashboard.knowledgeUnused') : t('dashboard.knowledgeFound', { used: r.knowledgeUsed, searched: r.knowledgeSearched, avg: dec(r.knowledgeAveragePieces) })}
                />
                <KpiCard
                  label={t('dashboard.blocked')}
                  value={nf.format(r.blocked)}
                  hint={r.blocked === 0 ? t('dashboard.blockedNone') : r.blockedByType.slice(0, 2).map((b) => `${failedLabel(b.key)} ${b.count}`).join(', ')}
                />
              </SimpleGrid>
            </div>

            <Accordion variant="contained" radius="md" data-tour="dash-tech">
              <Accordion.Item value="tech">
                <Accordion.Control>{t('dashboard.technicalTitle')}</Accordion.Control>
                <Accordion.Panel>
                  <Stack gap="lg">
                    <SimpleGrid cols={{ base: 1, sm: 2 }}>
                      <KpiCard label={t('dashboard.callsPerQuestion')} value={dec(r.aiCallsPerRequest)} hint={t('dashboard.callsSub', { calls: compact(r.aiCalls), pct: pct(r.aiErrorRate) })} />
                      <KpiCard label={t('dashboard.tokens')} value={compact(r.inputTokens + r.outputTokens)} hint={t('dashboard.tokensSub', { inp: compact(r.inputTokens), out: compact(r.outputTokens) })} />
                    </SimpleGrid>
                    <SectionCard title={t('dashboard.tokensPerDayTitle')} description={t('dashboard.tokensPerDaySub')}>
                      <AreaChart
                        h={200}
                        data={r.days.map((d) => ({ day: d.day.slice(5), [t('dashboard.input')]: d.inputTokens, [t('dashboard.output')]: d.outputTokens }))}
                        dataKey="day"
                        type="stacked"
                        series={[{ name: t('dashboard.input'), color: 'cobalt.6' }, { name: t('dashboard.output'), color: 'cobalt.3' }]}
                        withLegend
                        valueFormatter={(v) => compact(v)}
                      />
                    </SectionCard>
                    <SectionCard title={t('dashboard.latencyTitle')} description={t('dashboard.latencySub')} flush>
                      <Table.ScrollContainer minWidth={520}>
                        <Table verticalSpacing="xs" horizontalSpacing="md">
                          <Table.Thead><Table.Tr><Table.Th>{t('dashboard.colStep')}</Table.Th><Table.Th ta="right">{t('dashboard.colCalls')}</Table.Th><Table.Th ta="right">{t('dashboard.colErrors')}</Table.Th><Table.Th ta="right">{t('dashboard.medianLabel')}</Table.Th><Table.Th ta="right">{t('dashboard.slowest')}</Table.Th><Table.Th ta="right">{t('dashboard.colAvgPrompt')}</Table.Th></Table.Tr></Table.Thead>
                          <Table.Tbody>
                            {r.latency.map((l) => (
                              <Table.Tr key={l.purpose}><Table.Td>{purpose(l.purpose)}</Table.Td><Table.Td ta="right">{l.calls}</Table.Td><Table.Td ta="right">{l.errors}</Table.Td><Table.Td ta="right">{dur(l.p50Ms)}</Table.Td><Table.Td ta="right">{dur(l.p95Ms)}</Table.Td><Table.Td ta="right">{l.avgEstTokens == null ? '–' : compact(Math.round(l.avgEstTokens))}</Table.Td></Table.Tr>
                            ))}
                          </Table.Tbody>
                        </Table>
                      </Table.ScrollContainer>
                    </SectionCard>
                    <SimpleGrid cols={{ base: 1, lg: 2 }}>
                      <SectionCard title={t('dashboard.models')} description={t('dashboard.modelsSub')} flush>
                        <Table.ScrollContainer minWidth={420}>
                          <Table verticalSpacing="xs" horizontalSpacing="md">
                            <Table.Thead><Table.Tr><Table.Th>{t('dashboard.colModel')}</Table.Th><Table.Th ta="right">{t('dashboard.colCalls')}</Table.Th><Table.Th ta="right">{t('dashboard.colEstCost')}</Table.Th></Table.Tr></Table.Thead>
                            <Table.Tbody>{r.models.map((m) => <Table.Tr key={m.provider + m.model}><Table.Td>{m.provider} / {m.model}</Table.Td><Table.Td ta="right">{m.calls}</Table.Td><Table.Td ta="right">{money(m.estimatedCostUsd)}</Table.Td></Table.Tr>)}</Table.Tbody>
                          </Table>
                        </Table.ScrollContainer>
                      </SectionCard>
                      <SectionCard title={t('dashboard.activeUsers')} description={t('dashboard.activeUsersSub')} flush>
                        <Table verticalSpacing="xs" horizontalSpacing="md">
                          <Table.Thead><Table.Tr><Table.Th>{t('dashboard.colUser')}</Table.Th><Table.Th ta="right">{t('dashboard.colQuestions')}</Table.Th></Table.Tr></Table.Thead>
                          <Table.Tbody>{r.users.map((u) => <Table.Tr key={u.user}><Table.Td>{u.name || `…${u.user.replace(/-/g, '').slice(-6)}`}</Table.Td><Table.Td ta="right">{u.requests}</Table.Td></Table.Tr>)}</Table.Tbody>
                        </Table>
                      </SectionCard>
                    </SimpleGrid>
                  </Stack>
                </Accordion.Panel>
              </Accordion.Item>
            </Accordion>
          </>
        )}
      </Stack>
    </>
  )
}
