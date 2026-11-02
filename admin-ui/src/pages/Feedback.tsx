import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ActionIcon, Badge, Button, Card, Group, SimpleGrid, Stack, Text, Tooltip } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { IconCheck, IconCopy, IconTrash } from '@tabler/icons-react'
import { ApiError, del, get } from '../api/client'
import { useI18n, useT, useTOr } from '../i18n'
import { PageHeader } from '../components/PageHeader'
import { Callout } from '../components/Callout'
import { EmptyState } from '../components/EmptyState'
import { confirmAction } from '../components/confirm'

type Candidate = { id: string; question: string; explanation: string; reason: string | null; createdAt: string }
type Data = { retentionDays: number; candidates: Candidate[] }

/** Renders the admin Feedback page listing shared feedback candidates with copy and delete actions. */
export default function Feedback() {
  const t = useT()
  const tOr = useTOr()
  const { locale } = useI18n()
  const qc = useQueryClient()
  const [copied, setCopied] = useState<string | null>(null)

  const q = useQuery({ queryKey: ['feedback-candidates'], queryFn: () => get<Data>('/api/admin/feedback/candidates?take=200'), retry: false })
  const remove = useMutation({
    mutationFn: (id: string) => del(`/api/admin/feedback/candidates/${encodeURIComponent(id)}`),
    onSuccess: () => { notifications.show({ color: 'teal', message: t('feedback.deleted') }); void qc.invalidateQueries({ queryKey: ['feedback-candidates'] }) },
    onError: (e) => {
      if (e instanceof ApiError && e.status === 404) void qc.invalidateQueries({ queryKey: ['feedback-candidates'] })
      else notifications.show({ color: 'red', message: e instanceof Error ? e.message : String(e) })
    },
  })

  /** Copies a feedback candidate's question and explanation to the clipboard and shows a confirmation or error. */
  async function copy(c: Candidate) {
    try {
      await navigator.clipboard.writeText(c.question + '\n' + c.explanation)
      setCopied(c.id)
      setTimeout(() => setCopied((x) => (x === c.id ? null : x)), 1500)
    } catch {
      notifications.show({ color: 'red', message: t('feedback.copyFailed') })
    }
  }

  const unavailable = q.error instanceof ApiError && q.error.status === 404
  const list = q.data?.candidates ?? []
  const days = q.data?.retentionDays ?? 0

  return (
    <>
      <PageHeader title={t('feedback.title')} description={t('feedback.intro')} />
      {unavailable ? (
        <Callout kind="bad">{t('feedback.notAvailable')}</Callout>
      ) : q.isError ? (
        <Callout kind="bad" action={<Button mt="xs" size="xs" variant="default" onClick={() => void q.refetch()}>{t('ui.retry')}</Button>}>{(q.error as Error).message}</Callout>
      ) : q.isLoading ? (
        <Text c="dimmed">{t('feedback.loading')}</Text>
      ) : (
        <Stack gap="md">
          <Callout kind="info">{days > 0 ? t('feedback.noteDays', { days }) : t('feedback.noteForever')}</Callout>
          {list.length === 0 ? (
            <Card><EmptyState title={t('feedback.none')} /></Card>
          ) : (
            <>
              <Text size="sm" c="dimmed">{list.length === 1 ? t('feedback.countOne') : t('feedback.countMany', { n: list.length })}</Text>
              <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md" data-tour="fb-list">
                {list.map((c) => (
                  <Card key={c.id}>
                    <Stack gap="xs" h="100%" justify="space-between">
                      <Stack gap={4}>
                        <Group gap="xs" wrap="wrap">
                          {c.reason && <Badge variant="light" color="gray" tt="none">{tOr('feedback.reason_' + c.reason, c.reason)}</Badge>}
                          <Text size="xs" c="dimmed">{new Date(c.createdAt).toLocaleString(locale)}</Text>
                        </Group>
                        <Text fw={600} style={{ overflowWrap: 'anywhere' }}>{c.question}</Text>
                        <Text size="sm" c="dimmed" style={{ overflowWrap: 'anywhere' }}>{c.explanation}</Text>
                      </Stack>
                      <Group gap="xs" justify="flex-end">
                        <Tooltip label={t('feedback.copyTitle')}>
                          <Button variant="default" size="xs" leftSection={copied === c.id ? <IconCheck size={14} /> : <IconCopy size={14} />} onClick={() => void copy(c)}>
                            {copied === c.id ? t('feedback.copied') : t('feedback.copy')}
                          </Button>
                        </Tooltip>
                        <Tooltip label={t('feedback.delete')}>
                          <ActionIcon variant="default" color="red" size="lg" aria-label={t('feedback.delete')}
                            onClick={() => confirmAction({ title: t('feedback.delete'), message: t('feedback.deleteMsg'), confirmLabel: t('feedback.delete'), cancelLabel: t('ui.cancel'), danger: true, onConfirm: () => remove.mutate(c.id) })}>
                            <IconTrash size={16} />
                          </ActionIcon>
                        </Tooltip>
                      </Group>
                    </Stack>
                  </Card>
                ))}
              </SimpleGrid>
            </>
          )}
        </Stack>
      )}
    </>
  )
}
