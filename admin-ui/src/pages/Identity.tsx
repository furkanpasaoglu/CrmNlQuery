import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Badge, Button, Card, Collapse, Group, Skeleton, Stack, Text } from '@mantine/core'
import { IconChevronDown } from '@tabler/icons-react'
import { get } from '../api/client'
import { useI18n } from '../i18n'
import { PageHeader } from '../components/PageHeader'
import { SectionCard } from '../components/SectionCard'
import { Callout } from '../components/Callout'
import { StatusPill } from '../components/StatusPill'
import { SysCopyButton } from '../components/SysCopyButton'

type Identity = {
  account: string
  identityMode: string | null
  groups: string[]
  memberOfMappedGroups: string[]
  roles: string[]
  permissions: string[]
  isAdministrator: boolean
  appUseMode: string
  hasAppUse: boolean
}

/** Admin panel page showing the identity of the current user. */
export default function IdentityPage() {
  const { t } = useI18n()
  const [showGroups, setShowGroups] = useState(false)
  const q = useQuery({ queryKey: ['identity'], queryFn: () => get<Identity>('/api/admin/identity') })

  if (q.isError) {
    return (
      <>
        <PageHeader title={t('identity.title')} />
        <Callout kind="bad" action={<Button mt="xs" size="xs" variant="default" onClick={() => void q.refetch()}>{t('ui.retry')}</Button>}>{(q.error as Error).message}</Callout>
      </>
    )
  }
  if (q.isLoading || !q.data) {
    return (
      <>
        <PageHeader title={t('identity.title')} description={t('identity.intro')} />
        <Stack><Skeleton h={120} radius="md" /><Skeleton h={160} radius="md" /></Stack>
      </>
    )
  }

  const d = q.data
  const modeKey = { off: 'identity.modeOff', report: 'identity.modeReport', on: 'identity.modeOn' }[d.appUseMode]
  const modeText = modeKey ? t(modeKey) : d.appUseMode
  const none = t('identity.none')

  const diagnostic = [
    `${t('identity.account')}: ${d.account}`,
    `${t('identity.identityMode')}: ${d.identityMode || t('identity.notSet')}`,
    `${t('identity.roles')}: ${d.roles.join(', ') || none}`,
    `${t('identity.permissions')}: ${d.permissions.join(', ') || none}`,
    `${t('identity.mayUse')}: ${d.hasAppUse ? t('identity.yes') : t('identity.no')}`,
    `${t('identity.appUseRequired')}: ${modeText}`,
    `${t('identity.mappedGroups')}: ${d.memberOfMappedGroups.join(', ') || t('identity.noMappedGroup')}`,
    `${t('identity.groupsCarried', { n: d.groups.length })}:`,
    ...(d.groups.length ? d.groups.map((g) => `  ${g}`) : [`  ${t('identity.noGroups')}`]),
  ].join('\n')

  return (
    <>
      <PageHeader
        title={t('identity.title')}
        description={t('identity.intro')}
        actions={<SysCopyButton value={diagnostic} label={t('identity.copyDiag')} />}
      />
      <Stack gap="md" data-tour="id-cards">
        <SectionCard title={t('identity.cardWho')}>
          <Stack gap="sm">
            <Field label={t('identity.account')}><Text ff="monospace" size="sm" style={{ overflowWrap: 'anywhere' }}>{d.account}</Text></Field>
            <Field label={t('identity.identityMode')}><Text size="sm">{d.identityMode || t('identity.notSet')}</Text></Field>
            <Field label={t('identity.roles')}><Badges values={d.roles} empty={none} /></Field>
          </Stack>
        </SectionCard>

        <SectionCard title={t('identity.cardAccess')}>
          <Stack gap="sm">
            <Field label={t('identity.mayUse')}><StatusPill tone={d.hasAppUse ? 'good' : 'bad'}>{d.hasAppUse ? t('identity.yes') : t('identity.no')}</StatusPill></Field>
            <Field label={t('identity.appUseRequired')}><Text size="sm">{modeText}</Text></Field>
            <Field label={t('identity.permissions')}><Badges values={d.permissions} empty={none} /></Field>
          </Stack>
        </SectionCard>

        <SectionCard title={t('identity.cardGroups')}>
          <Stack gap="sm">
            <Field label={t('identity.mappedGroups')}><Badges values={d.memberOfMappedGroups} empty={t('identity.noMappedGroup')} /></Field>
            <div>
              <Group gap="xs" wrap="wrap">
                <Button
                  variant="subtle" size="compact-sm" aria-expanded={showGroups} disabled={d.groups.length === 0}
                  leftSection={<IconChevronDown size={16} style={{ transform: showGroups ? 'rotate(180deg)' : undefined, transition: 'transform 150ms' }} />}
                  onClick={() => setShowGroups((s) => !s)}
                >
                  {showGroups ? t('identity.hideGroups') : t('identity.showGroups', { n: d.groups.length })}
                </Button>
                {d.groups.length > 0 && <SysCopyButton value={d.groups.join('\n')} label={t('identity.copyGroups')} />}
              </Group>
              {d.groups.length === 0 && <Text size="sm" c="dimmed" mt={4}>{t('identity.noGroups')}</Text>}
              <Collapse expanded={showGroups}>
                <Card withBorder padding="sm" mt="xs" radius="md">
                  <Stack gap={2}>
                    {d.groups.map((g) => <Text key={g} size="sm" ff="monospace" style={{ overflowWrap: 'anywhere' }}>{g}</Text>)}
                  </Stack>
                </Card>
              </Collapse>
            </div>
          </Stack>
        </SectionCard>
      </Stack>
    </>
  )
}

/** Renders a small labelled row with a dimmed caption above the given content, used in detail drawers. */
function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <Text size="sm" c="dimmed" mb={2}>{label}</Text>
      {children}
    </div>
  )
}

/** Renders a list of badges or an empty text. */
function Badges({ values, empty }: { values: string[]; empty: string }) {
  if (values.length === 0) return <Text size="sm" c="dimmed">{empty}</Text>
  return <Group gap={6}>{values.map((v) => <Badge key={v} variant="light" tt="none" fw={500}>{v}</Badge>)}</Group>
}
