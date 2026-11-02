import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Accordion, Box, Button, Card, Collapse, CopyButton, Group, ScrollArea, Select, SimpleGrid, Stack, Table, Text, TextInput, ThemeIcon, UnstyledButton, Code } from '@mantine/core'
import { IconCheck, IconChevronDown, IconCopy, IconDownload } from '@tabler/icons-react'
import { ApiError, downloadFile, get } from '../api/client'
import { useMe } from '../auth/me'
import { useI18n, useTOr } from '../i18n'
import { PageHeader } from '../components/PageHeader'
import { SectionCard } from '../components/SectionCard'
import { Callout } from '../components/Callout'

type Column = { name: string; description: string; type: string }
type Dataset = { dataset: string; permission: string; columns: Column[] }
type Check = { fileName: string }

const SOURCE_CODES = ['', 'ask', 'result', 'lookup', 'record_context', 'summary', 'draft', 'count', 'other']

/** Returns the day-of-month value for a date in the export page. */
const dayValue = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`

/** Converts a date string to that day's local midnight in ISO UTC, using the next day's midnight for the inclusive end. */
function localMidnightIso(value: string, plusDays: number): string | null {
  const p = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  return p ? new Date(+p[1], +p[2] - 1, +p[3] + plusDays).toISOString() : null
}

/** Renders the title of a step in the export page. */
function StepTitle({ n, children }: { n: number; children: string }) {
  return (
    <Group gap="xs" wrap="nowrap">
      <ThemeIcon radius="xl" size={26} variant="light" color="cobalt"><Text size="sm" fw={600}>{n}</Text></ThemeIcon>
      <Text fw={600}>{children}</Text>
    </Group>
  )
}

/** Renders the admin Export page for downloading datasets. */
export default function ExportPage() {
  const { t } = useI18n()
  const tOr = useTOr()
  const { can } = useMe()

  const schema = useQuery({ queryKey: ['export-schema'], queryFn: () => get<Dataset[]>('/api/admin/export/schema'), retry: false })
  const allowed = useMemo(() => (schema.data ?? []).filter((s) => can(s.permission)), [schema.data, can])

  const today = new Date()
  const [dataset, setDataset] = useState<string | null>(null)
  const [format, setFormat] = useState('csv')
  const [from, setFrom] = useState(dayValue(new Date(today.getFullYear(), today.getMonth(), today.getDate() - 30)))
  const [to, setTo] = useState(dayValue(today))
  const [account, setAccount] = useState('')
  const [kind, setKind] = useState('')
  const [userId, setUserId] = useState('')
  const [table, setTable] = useState('')
  const [source, setSource] = useState('')
  const [advanced, setAdvanced] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [info, setInfo] = useState('')

  const ds = dataset ?? allowed[0]?.dataset ?? ''
  const title = (d: string) => tOr('export.ds_' + d, d)

  let zone = ''
  try { zone = Intl.DateTimeFormat().resolvedOptions().timeZone || '' } catch {  }

  /** Builds the export query parameters from the form. */
  function query(): string {
    const q: string[] = []
    const f = localMidnightIso(from, 0)
    const tt = localMidnightIso(to, 1)
    if (f) q.push('from=' + encodeURIComponent(f))
    if (tt) q.push('to=' + encodeURIComponent(tt))
    q.push('format=' + encodeURIComponent(format))
    if (zone) q.push('tz=' + encodeURIComponent(zone))
    if (ds === 'audit') {
      if (account.trim()) q.push('account=' + encodeURIComponent(account.trim()))
      if (kind.trim()) q.push('kind=' + encodeURIComponent(kind.trim().toLowerCase()))
    } else if (ds === 'accesslog') {
      if (userId.trim()) q.push('user=' + encodeURIComponent(userId.trim()))
      if (table.trim()) q.push('table=' + encodeURIComponent(table.trim().toLowerCase()))
      if (source) q.push('kind=' + encodeURIComponent(source))
    } else if (ds === 'questions') {
      if (userId.trim()) q.push('user=' + encodeURIComponent(userId.trim()))
    }
    return q.join('&')
  }

  /** Validates the export on the server first (permission, range, filter, concurrency), shows errors, then starts the browser download. */
  async function download() {
    setBusy(true); setError(''); setInfo(t('export.checking'))
    const base = '/api/admin/export/' + encodeURIComponent(ds)
    const q = query()
    try {
      const r = await get<Check>(base + '?' + q + '&check=true')
      await downloadFile(base + '?' + q, r.fileName)
      setInfo(t('export.started', { file: r.fileName }))
    } catch (e) {
      setInfo('')
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const hasAdvanced = ds === 'audit' || ds === 'accesslog' || ds === 'questions'
  const current = allowed.find((s) => s.dataset === ds)

  if (schema.isError) {
    const e = schema.error
    return (
      <>
        <PageHeader title={t('export.title')} description={t('export.intro')} />
        <Callout kind="bad">{e instanceof ApiError && e.status === 404 ? t('export.notAvailable') : (e as Error).message}</Callout>
      </>
    )
  }

  return (
    <>
      <PageHeader title={t('export.title')} description={t('export.intro')} />
      {schema.isSuccess && allowed.length === 0 ? (
        <Callout kind="warn">{t('export.noViewPerm')}</Callout>
      ) : (
        <Stack gap="md">
          <Card padding="md" className="grid-card" data-tour="export-form">
            <Stack gap="lg">
              <Stack gap="xs">
                <StepTitle n={1}>{t('export.step1')}</StepTitle>
                <SimpleGrid cols={{ base: 1, sm: 2 }}>
                  <Select
                    label={t('export.fldDataset')}
                    data={allowed.map((s) => ({ value: s.dataset, label: title(s.dataset) }))}
                    value={ds || null}
                    onChange={(v) => setDataset(v)}
                    allowDeselect={false}
                    disabled={schema.isLoading}
                  />
                  <Select
                    label={t('export.fldFormat')}
                    data={[{ value: 'csv', label: t('export.formatCsv') }, { value: 'jsonl', label: t('export.formatJsonl') }]}
                    value={format}
                    onChange={(v) => setFormat(v ?? 'csv')}
                    allowDeselect={false}
                  />
                </SimpleGrid>
                {ds && <Text size="sm" c="dimmed" className="readable">{tOr('export.about_' + ds, '')}</Text>}
              </Stack>

              <Stack gap="xs">
                <StepTitle n={2}>{t('export.step2')}</StepTitle>
                <SimpleGrid cols={{ base: 1, sm: 2 }}>
                  <TextInput type="date" label={t('export.fldFrom')} value={from} onChange={(e) => setFrom(e.currentTarget.value)} />
                  <TextInput type="date" label={t('export.fldTo')} value={to} onChange={(e) => setTo(e.currentTarget.value)} />
                </SimpleGrid>
                {hasAdvanced && (
                  <>
                    <UnstyledButton onClick={() => setAdvanced((o) => !o)} aria-expanded={advanced} w="fit-content">
                      <Group gap={4}>
                        <IconChevronDown size={16} style={{ transform: advanced ? 'rotate(180deg)' : undefined, transition: 'transform 150ms' }} />
                        <Text size="sm" c="cobalt" fw={500}>{t('export.advanced')}</Text>
                      </Group>
                    </UnstyledButton>
                    <Collapse expanded={advanced}>
                      <SimpleGrid cols={{ base: 1, sm: 2 }}>
                        {ds === 'audit' && (
                          <>
                            <TextInput label={t('export.fldAccount')} placeholder={t('export.phAccount')} maxLength={200} value={account} onChange={(e) => setAccount(e.currentTarget.value)} />
                            <TextInput label={t('export.fldAction')} placeholder={t('export.phKind')} maxLength={40} value={kind} onChange={(e) => setKind(e.currentTarget.value)} />
                          </>
                        )}
                        {ds === 'accesslog' && (
                          <>
                            <TextInput label={t('export.fldUserId')} placeholder={t('export.phUserId')} maxLength={36} value={userId} onChange={(e) => setUserId(e.currentTarget.value)} />
                            <TextInput label={t('export.fldTable')} placeholder={t('export.phTable')} maxLength={64} value={table} onChange={(e) => setTable(e.currentTarget.value)} />
                            <Select
                              label={t('export.fldSource')}
                              data={SOURCE_CODES.map((c) => ({ value: c, label: c === '' ? t('export.srcAny') : tOr('export.src_' + c, c) }))}
                              value={source}
                              onChange={(v) => setSource(v ?? '')}
                              allowDeselect={false}
                            />
                          </>
                        )}
                        {ds === 'questions' && (
                          <TextInput label={t('export.fldUserId')} placeholder={t('export.phUserId')} maxLength={36} value={userId} onChange={(e) => setUserId(e.currentTarget.value)} />
                        )}
                      </SimpleGrid>
                    </Collapse>
                  </>
                )}
              </Stack>

              <Stack gap="xs">
                <StepTitle n={3}>{t('export.step3')}</StepTitle>
                <Group>
                  <Button data-tour="export-go" leftSection={<IconDownload size={16} />} loading={busy} disabled={!ds} onClick={() => void download()}>{t('export.download')}</Button>
                </Group>
                {error && <Callout kind="bad">{error}</Callout>}
                {info && !error && <Text size="sm" c="dimmed" role="status" style={{ whiteSpace: 'pre-wrap' }}>{info}</Text>}
              </Stack>
            </Stack>
          </Card>

          <SectionCard title={t('export.verifyTitle')}>
            <Stack gap="xs">
              <Text size="sm" c="dimmed" className="readable">{t('export.verifyP1')}</Text>
              <Group gap="xs" wrap="nowrap" align="flex-start">
                <Box style={{ flex: 1, minWidth: 0 }}>
                  <ScrollArea type="auto"><Code block>{'Get-FileHash -Algorithm SHA256 .\\nlquery-audit-20260701-20261001.csv'}</Code></ScrollArea>
                </Box>
                <CopyButton value={'Get-FileHash -Algorithm SHA256 .\\nlquery-audit-20260701-20261001.csv'}>
                  {({ copied, copy }) => (
                    <Button variant="default" size="xs" leftSection={copied ? <IconCheck size={14} /> : <IconCopy size={14} />} onClick={copy}>
                      {copied ? t('ui.copied') : t('ui.copy')}
                    </Button>
                  )}
                </CopyButton>
              </Group>
              <Text size="sm" c="dimmed" className="readable">{t('export.verifyP2')}</Text>
            </Stack>
          </SectionCard>

          {current || allowed.length > 0 ? (
            <SectionCard title={t('export.columns')}>
              <Accordion variant="separated" defaultValue={ds}>
                {allowed.map((s) => (
                  <Accordion.Item key={s.dataset} value={s.dataset}>
                    <Accordion.Control>{title(s.dataset)}</Accordion.Control>
                    <Accordion.Panel>
                      <Table.ScrollContainer minWidth={420}>
                        <Table verticalSpacing={4} fz="sm">
                          <Table.Thead>
                            <Table.Tr><Table.Th>{t('export.colColumn')}</Table.Th><Table.Th>{t('export.colType')}</Table.Th><Table.Th>{t('export.colMeaning')}</Table.Th></Table.Tr>
                          </Table.Thead>
                          <Table.Tbody>
                            {s.columns.map((c) => (
                              <Table.Tr key={c.name}>
                                <Table.Td><Text size="sm" ff="monospace">{c.name}</Text></Table.Td>
                                <Table.Td>{c.type}</Table.Td>
                                <Table.Td>{tOr('exportcol.' + s.dataset + '.' + c.name, tOr('exportcol.common.' + c.name, c.description))}</Table.Td>
                              </Table.Tr>
                            ))}
                          </Table.Tbody>
                        </Table>
                      </Table.ScrollContainer>
                    </Accordion.Panel>
                  </Accordion.Item>
                ))}
              </Accordion>
            </SectionCard>
          ) : null}
        </Stack>
      )}
    </>
  )
}
