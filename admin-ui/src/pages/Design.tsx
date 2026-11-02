import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button, Card, Drawer, Group, Loader, Progress, Skeleton, Stack, Text, TextInput, Textarea } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { IconFileTypeDocx, IconBraces, IconMarkdown, IconPlayerPlay } from '@tabler/icons-react'
import { ApiError, del, downloadFile, get, post } from '../api/client'
import { useI18n, useTOr } from '../i18n'
import { PageHeader } from '../components/PageHeader'
import { SectionCard } from '../components/SectionCard'
import { StatusPill } from '../components/StatusPill'
import { Callout } from '../components/Callout'
import { EmptyState } from '../components/EmptyState'
import { confirmAction } from '../components/confirm'
import { ToolMarkdown } from '../components/ToolMarkdown'

type Job = { id: string; title: string; status: string; step: string; percent: number; problems: number; createdAt: string; error?: string; markdown?: string }

const STATUS_KEYS: Record<string, string> = { queued: 'statusQueued', running: 'statusRunning', ready: 'statusReady', failed: 'statusFailed', cancelled: 'statusCancelled' }
const STEP_KEYS: Record<string, string> = {
  'Waiting to start': 'stepWaiting', 'Understanding the request': 'stepUnderstanding', 'Searching the CRM schema': 'stepSearching',
  'Deciding what to reuse': 'stepDeciding', 'Designing': 'stepDesigning', 'Fixing the design': 'stepFixing', 'Finishing': 'stepFinishing',
  'Ready': 'stepReady', 'Cancelled': 'stepCancelled', 'Failed': 'stepFailed',
}
/** Returns whether a design job is still active. */
const isActive = (j: Job) => j.status === 'queued' || j.status === 'running'
/** Returns a display tone for a job status. */
const tone = (s: string) => (s === 'ready' ? 'good' : s === 'failed' ? 'bad' : s === 'cancelled' ? 'neutral' : 'warn') as 'good' | 'bad' | 'neutral' | 'warn'

/** Renders the Design assistant admin page for starting and viewing design jobs. */
export default function DesignAssistant() {
  const { t, locale } = useI18n()
  const tOr = useTOr()
  const qc = useQueryClient()
  const [text, setText] = useState('')
  const [prefix, setPrefix] = useState('')
  const [openId, setOpenId] = useState<string | null>(null)
  const [error, setError] = useState('')
  const lastStatus = useRef<Record<string, string>>({})

  const statusText = (s: string) => (STATUS_KEYS[s] ? tOr('design.' + STATUS_KEYS[s], s) : s)
  const stepText = (s: string) => (STEP_KEYS[s] ? tOr('design.' + STEP_KEYS[s], s) : s)
  const progressText = (j: Job) => (j.percent ? t('design.progressPct', { state: statusText(j.status), step: stepText(j.step), percent: j.percent }) : t('design.progressNoPct', { state: statusText(j.status), step: stepText(j.step) }))
  const when = (v: string) => { const d = new Date(v); return isNaN(d.getTime()) ? '' : d.toLocaleString(locale) }

  const jobs = useQuery({
    queryKey: ['design-jobs'],
    queryFn: () => get<Job[]>('/api/admin/design'),
    retry: false,
    refetchInterval: (q) => ((q.state.data ?? []).some(isActive) ? 3000 : false),
  })
  const anyActive = (jobs.data ?? []).some(isActive)

  const detail = useQuery({
    queryKey: ['design-job', openId],
    queryFn: () => get<Job>('/api/admin/design/' + encodeURIComponent(openId ?? '')),
    enabled: openId !== null,
    retry: false,
    refetchInterval: (q) => (q.state.data && isActive(q.state.data) ? 3000 : false),
  })

  useEffect(() => {
    for (const j of jobs.data ?? []) {
      const prev = lastStatus.current[j.id]
      if ((prev === 'queued' || prev === 'running') && !isActive(j)) {
        const key = j.status === 'ready' ? 'design.doneReady' : j.status === 'failed' ? 'design.doneFailed' : 'design.doneCancelled'
        notifications.show({ color: j.status === 'ready' ? 'teal' : j.status === 'failed' ? 'red' : 'gray', message: t(key, { title: j.title }) })
        if (j.status === 'ready') setOpenId(j.id)
      }
      lastStatus.current[j.id] = j.status
    }
  }, [jobs.data, t])

  const errText = (e: unknown) => (e as Error).message

  const start = useMutation({
    mutationFn: () => post<{ id: string }>('/api/admin/design', prefix.trim() ? { text, prefix: prefix.trim() } : { text }),
    onSuccess: (r) => {
      setError(''); setText(''); setOpenId(null)
      lastStatus.current[r.id] = 'queued'
      notifications.show({ color: 'teal', message: t('design.started') })
      void qc.invalidateQueries({ queryKey: ['design-jobs'] })
    },
    onError: (e) => setError(errText(e)),
  })
  const cancel = useMutation({
    mutationFn: (id: string) => post('/api/admin/design/' + encodeURIComponent(id) + '/cancel'),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['design-jobs'] }),
    onError: (e) => notifications.show({ color: 'red', message: errText(e) }),
  })
  const remove = useMutation({
    mutationFn: (id: string) => del('/api/admin/design/' + encodeURIComponent(id)),
    onSuccess: (_r, id) => {
      notifications.show({ color: 'teal', message: t('design.deleted') })
      if (openId === id) setOpenId(null)
      void qc.invalidateQueries({ queryKey: ['design-jobs'] })
    },
    onError: (e) => notifications.show({ color: 'red', message: errText(e) }),
  })

  /** Asks for confirmation before deleting a design job. */
  function askDelete(j: Job) {
    confirmAction({ title: t('design.delete'), message: t('design.deleteMessage', { title: j.title }), confirmLabel: t('design.delete'), cancelLabel: t('ui.cancel'), danger: true, onConfirm: () => remove.mutate(j.id) })
  }

  if (jobs.isError) {
    const e = jobs.error
    return (
      <>
        <PageHeader title={t('design.title')} description={t('design.intro')} />
        <Callout kind="bad">{e instanceof ApiError && e.status === 404 ? t('design.notAvailableToYou') : (e as Error).message}</Callout>
      </>
    )
  }

  const list = jobs.data ?? []
  const open = detail.data

  return (
    <>
      <PageHeader title={t('design.title')} description={t('design.intro')} />
      <Stack gap="md">
        <SectionCard title={t('design.requirementLabel')}>
          <Stack gap="sm">
            <Textarea
              data-tour="design-request"
              aria-label={t('design.requirementLabel')}
              placeholder={t('design.requirementPlaceholder')}
              minRows={6}
              autosize
              maxLength={4000}
              value={text}
              onChange={(e) => setText(e.currentTarget.value)}
            />
            <Group align="flex-end" wrap="wrap">
              <TextInput label={t('design.prefixLabel')} placeholder={t('design.prefixPlaceholder')} maxLength={8} w={{ base: '100%', xs: 220 }} value={prefix} onChange={(e) => setPrefix(e.currentTarget.value)} />
              <Button data-tour="design-start" leftSection={<IconPlayerPlay size={16} />} loading={start.isPending} disabled={anyActive || !text.trim()} onClick={() => start.mutate()}>{t('design.startButton')}</Button>
            </Group>
            {error && <Callout kind="bad">{error}</Callout>}
            {anyActive && <Text size="sm" c="dimmed">{t('design.started')}</Text>}
          </Stack>
        </SectionCard>

        <Text fw={600} data-tour="design-jobs">{t('design.yourDesigns')}</Text>
        {jobs.isLoading ? (
          <Skeleton height={90} radius="md" />
        ) : list.length === 0 ? (
          <Card className="grid-card"><EmptyState title={t('design.noDesigns')} /></Card>
        ) : (
          <Stack gap="sm">
            {list.map((j) => (
              <Card key={j.id} padding="md" className="grid-card">
                <Stack gap="xs">
                  <Group justify="space-between" align="flex-start" wrap="wrap" gap="xs">
                    <Text fw={500} style={{ flex: 1, minWidth: 200, overflowWrap: 'anywhere' }}>{j.title}</Text>
                    <StatusPill tone={tone(j.status)}>{statusText(j.status)}</StatusPill>
                  </Group>
                  <Text size="sm" c="dimmed">{when(j.createdAt)}</Text>
                  {isActive(j) && (
                    <>
                      <Progress value={j.percent || 5} animated aria-label={progressText(j)} />
                      <Text size="sm" c="dimmed">{progressText(j)}</Text>
                    </>
                  )}
                  {j.status === 'ready' && j.problems > 0 && <Text size="sm" c="yellow.8">{t('design.readyWithProblems', { n: j.problems })}</Text>}
                  <Group gap="xs">
                    <Button size="xs" variant="default" onClick={() => setOpenId(j.id)}>{t('design.open')}</Button>
                    {isActive(j)
                      ? <Button size="xs" variant="default" loading={cancel.isPending && cancel.variables === j.id} onClick={() => cancel.mutate(j.id)}>{t('design.cancel')}</Button>
                      : <Button size="xs" variant="subtle" color="red" onClick={() => askDelete(j)}>{t('design.delete')}</Button>}
                  </Group>
                </Stack>
              </Card>
            ))}
          </Stack>
        )}
      </Stack>

      <Drawer opened={openId !== null} onClose={() => setOpenId(null)} position="right" size="xl" title={open?.title ?? t('design.title')}>
        {detail.isLoading ? (
          <Loader size="sm" />
        ) : detail.isError || !open ? (
          <Text c="dimmed">{t('design.unavailable')}</Text>
        ) : (
          <Stack gap="sm">
            <Group gap="xs">
              <StatusPill tone={tone(open.status)}>{statusText(open.status)}</StatusPill>
              {isActive(open) && <Text size="sm" c="dimmed">{progressText(open)}</Text>}
            </Group>
            {open.error && <Callout kind="bad">{open.error}</Callout>}
            {open.status === 'ready' && (
              <>
                {open.problems > 0 && <Callout kind="warn">{t('design.problemsNote', { n: open.problems })}</Callout>}
                <Group gap="xs" wrap="wrap">
                  {([['docx', t('design.downloadDocx'), <IconFileTypeDocx key="d" size={16} />], ['md', t('design.downloadMd'), <IconMarkdown key="m" size={16} />], ['json', t('design.downloadJson'), <IconBraces key="j" size={16} />]] as const).map(([f, label, icon]) => (
                    <Button key={f} variant="default" size="xs" leftSection={icon} onClick={() => void downloadFile('/api/admin/design/' + encodeURIComponent(open.id) + '/download?format=' + f, 'design.' + f).catch((e) => notifications.show({ color: 'red', message: e instanceof Error ? e.message : String(e) }))}>{label}</Button>
                  ))}
                </Group>
              </>
            )}
            {open.markdown && <ToolMarkdown source={open.markdown} />}
          </Stack>
        )}
      </Drawer>
    </>
  )
}
