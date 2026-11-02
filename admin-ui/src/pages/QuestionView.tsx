import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button, Group, Stack, Text, TextInput } from '@mantine/core'
import { IconSearch } from '@tabler/icons-react'
import { ApiError, get } from '../api/client'
import { useI18n } from '../i18n'
import { PageHeader } from '../components/PageHeader'
import { DataGrid, type GridColumn } from '../components/DataGrid'
import { Callout } from '../components/Callout'

type Q = { at: string; entity: string; question: string }
type Result = { questions?: Q[] }

const GUID = /^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$/

/** Admin panel page for viewing question texts. */
export default function QuestionView() {
  const { t, locale } = useI18n()
  const [input, setInput] = useState('')
  const [who, setWho] = useState('')

  const q = useQuery({
    queryKey: ['question-view', who],
    enabled: who !== '',
    retry: false,
    gcTime: 0,
    staleTime: 0,
    queryFn: () => get<Result>('/api/admin/users/questions?' + (GUID.test(who) ? 'user=' : 'account=') + encodeURIComponent(who) + '&take=100'),
  })

  const submit = () => {
    const v = input.trim()
    if (!v) return
    if (v === who) void q.refetch(); else setWho(v)
  }

  const list = q.data?.questions ?? []
  const when = (iso: string) => { const d = new Date(iso); return isNaN(d.getTime()) ? '' : d.toLocaleString(locale) }
  const columns: GridColumn<Q>[] = [
    { id: 'at', title: t('question-view.colTime'), value: (r) => r.at, render: (r) => <Text size="sm" style={{ whiteSpace: 'nowrap' }}>{when(r.at)}</Text>, sortable: true, filter: 'date', width: 190 },
    { id: 'entity', title: t('question-view.colTable'), value: (r) => r.entity, sortable: true, filter: 'multi', width: 160 },
    { id: 'question', title: t('question-view.colQuestion'), value: (r) => r.question, filter: 'text', mobile: 'title', render: (r) => <Text size="sm" style={{ overflowWrap: 'anywhere' }}>{r.question}</Text> },
  ]

  let body = null
  if (q.isError) {
    const e = q.error
    body = <Callout kind="bad">{e instanceof ApiError && e.status === 404 && !e.hasMessage ? t('question-view.notAvailable') : (e instanceof ApiError && e.message) || t('question-view.loadFailed')}</Callout>
  } else if (q.isSuccess) {
    body = (
      <>
        <Callout kind="info">{t('question-view.countRecorded', { n: list.length })}</Callout>
        <DataGrid<Q>
          storageKey="question-view"
          exportName="questions"
          records={list}
          getId={(r) => r.at + r.entity + r.question}
          columns={columns}
          emptyTitle={t('question-view.nothing')}
        />
      </>
    )
  }

  return (
    <>
      <PageHeader title={t('question-view.title')} description={t('question-view.typeAccount')} />
      <Stack gap="md">
        <form data-tour="qv-form" onSubmit={(e) => { e.preventDefault(); submit() }}>
          <Group align="flex-end" wrap="wrap" gap="sm">
            <TextInput label={t('question-view.ariaUser')} placeholder={t('question-view.placeholder')} maxLength={256} value={input} onChange={(e) => setInput(e.currentTarget.value)} w={{ base: '100%', xs: 360 }} />
            <Button type="submit" leftSection={<IconSearch size={16} />} loading={q.isFetching} disabled={!input.trim()}>{t('question-view.show')}</Button>
          </Group>
        </form>
        {body}
      </Stack>
    </>
  )
}
