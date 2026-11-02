import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ActionIcon, Badge, Button, Drawer, Group, Menu, Modal, Select, Stack, Text, TextInput } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { IconDots, IconPlus, IconTrash, IconUserCog } from '@tabler/icons-react'
import { ApiError, del, get, put } from '../api/client'
import { useMe } from '../auth/me'
import { useT } from '../i18n'
import { PageHeader } from '../components/PageHeader'
import { DataGrid, type GridColumn } from '../components/DataGrid'
import { Callout } from '../components/Callout'
import { confirmAction } from '../components/confirm'
import { AccessRoleChecks, errorText, type Role } from '../components/AccessRoleChecks'

type Group = { group: string; roleIds: string[]; roles: string[] }
type Identity = { groups?: string[] }

/** Renders the AD groups table of the admin page. */
export default function Groups() {
  const t = useT()
  const qc = useQueryClient()
  const { can, refresh } = useMe()
  const [mapping, setMapping] = useState(false)
  const [editing, setEditing] = useState<Group | null>(null)

  const groups = useQuery({ queryKey: ['groups'], queryFn: () => get<Group[]>('/api/admin/groups') })
  const roles = useQuery({ queryKey: ['roles'], queryFn: () => get<Role[]>('/api/admin/roles') })

  const changed = (message: string) => {
    notifications.show({ color: 'teal', message })
    void qc.invalidateQueries({ queryKey: ['groups'] })
    refresh()
  }

  const remove = useMutation({
    mutationFn: (g: Group) => del(`/api/admin/groups?group=${encodeURIComponent(g.group)}`),
    onSuccess: () => changed(t('groups.removed')),
    onError: (e) => notifications.show({ color: 'red', message: errorText(e) }),
  })
  const askRemove = (g: Group) => confirmAction({
    title: t('groups.removeTitle', { group: g.group }),
    message: t('groups.removeMessage'),
    confirmLabel: t('groups.remove'),
    cancelLabel: t('groups.cancel'),
    danger: true,
    onConfirm: () => remove.mutate(g),
  })

  const loadError = groups.error ?? roles.error
  const columns: GridColumn<Group>[] = [
    { id: 'group', title: t('groups.colGroup'), value: (g) => g.group, sortable: true, filter: 'text', mobile: 'title' },
    {
      id: 'roles', title: t('groups.colRoles'), value: (g) => g.roles.join(', '), filter: 'text',
      render: (g) => g.roles.length === 0
        ? <Text size="sm" c="dimmed">{t('groups.none')}</Text>
        : <Group gap={4}>{g.roles.map((r) => <Badge key={r} variant="light" tt="none" fw={500}>{r}</Badge>)}</Group>,
    },
    {
      id: 'actions', title: '', value: () => '', width: 64, align: 'right',
      render: (g) => (
        <Menu position="bottom-end" withinPortal>
          <Menu.Target>
            <ActionIcon variant="subtle" color="gray" size="lg" aria-label={t('groups.actionsAria', { group: g.group })}><IconDots size={18} /></ActionIcon>
          </Menu.Target>
          <Menu.Dropdown>
            <Menu.Item leftSection={<IconUserCog size={16} />} onClick={() => setEditing(g)}>{t('groups.editRoles')}</Menu.Item>
            <Menu.Divider />
            <Menu.Item color="red" leftSection={<IconTrash size={16} />} onClick={() => askRemove(g)}>{t('groups.remove')}</Menu.Item>
          </Menu.Dropdown>
        </Menu>
      ),
    },
  ]

  const mapButton = <Button leftSection={<IconPlus size={16} />} onClick={() => setMapping(true)}>{t('groups.mapGroup')}</Button>

  return (
    <>
      <PageHeader title={t('groups.title')} description={t('groups.intro')} actions={can('Users.Manage') ? mapButton : undefined} />
      {loadError ? (
        <Callout kind="bad" action={<Button mt="xs" size="xs" variant="default" onClick={() => { void groups.refetch(); void roles.refetch() }}>{t('ui.retry')}</Button>}>
          {loadError instanceof ApiError && loadError.status === 404 ? t('groups.notAvailable') : errorText(loadError)}
        </Callout>
      ) : (
        <DataGrid<Group>
          storageKey="groups"
          exportName="ad-groups"
          records={groups.data ?? []}
          loading={groups.isLoading}
          getId={(g) => g.group}
          columns={columns}
          emptyTitle={t('groups.noneMapped')}
          emptyAction={can('Users.Manage') ? mapButton : undefined}
        />
      )}

      <Drawer opened={mapping} onClose={() => setMapping(false)} position="right" size="md" title={t('groups.mapTitle')}>
        <GroupForm roles={roles.data ?? []} onDone={() => { setMapping(false); changed(t('groups.mapped')) }} onCancel={() => setMapping(false)} />
      </Drawer>
      <Drawer opened={editing !== null} onClose={() => setEditing(null)} position="right" size="md" title={editing ? t('groups.rolesOf', { group: editing.group }) : ''}>
        {editing && <GroupForm existing={editing} roles={roles.data ?? []} onDone={() => { setEditing(null); changed(t('groups.rolesSaved')) }} onCancel={() => setEditing(null)} />}
      </Drawer>
    </>
  )
}

/** Renders the form to add a group mapping or change its roles, showing a warning on risky mappings that need confirmation. */
function GroupForm({ existing, roles, onDone, onCancel }: { existing?: Group; roles: Role[]; onDone: () => void; onCancel: () => void }) {
  const t = useT()
  const [name, setName] = useState(existing?.group ?? '')
  const [roleIds, setRoleIds] = useState<string[]>(existing?.roleIds ?? [])
  const [risk, setRisk] = useState<string | null>(null)

  const identity = useQuery({ queryKey: ['identity'], queryFn: () => get<Identity>('/api/admin/identity'), enabled: !existing })
  const mine = (identity.data?.groups ?? []).filter((g) => !g.startsWith('S-1-'))

  const m = useMutation({
    mutationFn: (confirm: boolean) =>
      put(`/api/admin/groups/roles?group=${encodeURIComponent(name.trim())}${confirm ? '&confirm=true' : ''}`, { roleIds }),
    onSuccess: onDone,
    onError: (e) => {
      if (e instanceof ApiError && e.status === 409 && e.code === 'confirm_required') setRisk(e.message)
      else setRisk(null)
    },
  })

  const needsConfirm = m.error instanceof ApiError && m.error.status === 409 && m.error.code === 'confirm_required'

  return (
    <form onSubmit={(e) => { e.preventDefault(); m.mutate(false) }}>
      <Stack gap="md">
        {m.isError && !needsConfirm && <Callout kind="bad">{errorText(m.error)}</Callout>}
        <TextInput
          label={t('groups.adGroup')}
          placeholder={t('groups.namePlaceholder')}
          value={name}
          onChange={(e) => setName(e.currentTarget.value)}
          maxLength={256}
          disabled={Boolean(existing)}
          required
          data-autofocus
        />
        {!existing && (
          <Select
            label={t('groups.pickLabel')}
            placeholder={mine.length === 0 && !identity.isLoading ? t('groups.noGroupsFound') : t('groups.pickPlaceholder')}
            data={mine}
            value={null}
            onChange={(v) => { if (v) setName(v) }}
            disabled={mine.length === 0}
            searchable
            clearable={false}
            nothingFoundMessage={t('groups.noGroupsFound')}
          />
        )}
        <Stack gap={6}>
          <Text size="sm" fw={500}>{t('groups.roles')}</Text>
          <AccessRoleChecks roles={roles} value={roleIds} onChange={setRoleIds} lockedLabel={t('groups.roleLocked')} />
        </Stack>
        <Group justify="flex-end" wrap="wrap">
          <Button variant="default" onClick={onCancel}>{t('groups.cancel')}</Button>
          <Button type="submit" loading={m.isPending && !risk} disabled={name.trim() === ''}>{existing ? t('groups.saveRoles') : t('groups.mapGroup')}</Button>
        </Group>
      </Stack>

      <Modal
        opened={risk !== null}
        onClose={() => { setRisk(null); m.reset(); notifications.show({ color: 'gray', message: t('groups.nothingChanged') }) }}
        title={t('groups.confirmTitle')}
        zIndex={400}
        centered
      >
        <Stack gap="md">
          <Callout kind="warn" title={t('groups.needsConfirmation')}>{risk}</Callout>
          <Group justify="flex-end" wrap="wrap">
            <Button variant="default" onClick={() => { setRisk(null); m.reset(); notifications.show({ color: 'gray', message: t('groups.nothingChanged') }) }}>{t('groups.cancel')}</Button>
            <Button color="red" loading={m.isPending} onClick={() => m.mutate(true)}>{t('groups.confirmYes')}</Button>
          </Group>
        </Stack>
      </Modal>
    </form>
  )
}
