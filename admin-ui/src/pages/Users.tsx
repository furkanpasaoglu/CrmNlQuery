import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ActionIcon, Badge, Button, Drawer, Group, Menu, Stack, Text, TextInput } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { IconDots, IconPlus, IconTrash, IconUserCheck, IconUserCog, IconUserOff } from '@tabler/icons-react'
import { ApiError, del, get, post, put } from '../api/client'
import { useMe } from '../auth/me'
import { useT } from '../i18n'
import { PageHeader } from '../components/PageHeader'
import { DataGrid, type GridColumn } from '../components/DataGrid'
import { Callout } from '../components/Callout'
import { StatusPill } from '../components/StatusPill'
import { confirmAction } from '../components/confirm'
import { AccessRoleChecks, errorText, type Role } from '../components/AccessRoleChecks'

type User = { account: string; displayName: string; active: boolean; createdAt: string; roleIds: string[]; roles: string[] }

/** Renders the Users admin page for listing, adding, editing and deactivating users. */
export default function Users() {
  const t = useT()
  const qc = useQueryClient()
  const { can, refresh } = useMe()
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<User | null>(null)

  const users = useQuery({ queryKey: ['users'], queryFn: () => get<User[]>('/api/admin/users') })
  const roles = useQuery({ queryKey: ['roles'], queryFn: () => get<Role[]>('/api/admin/roles') })

  const changed = (message: string) => {
    notifications.show({ color: 'teal', message })
    void qc.invalidateQueries({ queryKey: ['users'] })
    refresh()
  }
  const failed = (e: unknown) => notifications.show({ color: 'red', message: errorText(e) })

  const toggle = useMutation({
    mutationFn: (u: User) => put(`/api/admin/users/active?account=${encodeURIComponent(u.account)}`, { active: !u.active }),
    onSuccess: (_d, u) => changed(u.active ? t('users.deactivated') : t('users.activated')),
    onError: failed,
  })
  const remove = useMutation({
    mutationFn: (u: User) => del(`/api/admin/users?account=${encodeURIComponent(u.account)}`),
    onSuccess: () => changed(t('users.deleted')),
    onError: failed,
  })

  const askDelete = (u: User) => confirmAction({
    title: t('users.deleteTitle', { name: u.displayName || u.account }),
    message: t('users.deleteMessage'),
    confirmLabel: t('users.delete'),
    cancelLabel: t('ui.cancel'),
    danger: true,
    onConfirm: () => remove.mutate(u),
  })

  const loadError = users.error ?? roles.error
  const columns: GridColumn<User>[] = [
    { id: 'name', title: t('users.colName'), value: (u) => u.displayName || u.account, sortable: true, filter: 'text', mobile: 'title' },
    { id: 'account', title: t('users.colAccount'), value: (u) => u.account, sortable: true, filter: 'text' },
    {
      id: 'roles', title: t('users.colRoles'), value: (u) => u.roles.join(', '), filter: 'text',
      render: (u) => u.roles.length === 0
        ? <Text size="sm" c="dimmed">{t('users.none')}</Text>
        : <Group gap={4}>{u.roles.map((r) => <Badge key={r} variant="light" tt="none" fw={500}>{r}</Badge>)}</Group>,
    },
    {
      id: 'status', title: t('users.colStatus'), value: (u) => (u.active ? t('users.active') : t('users.inactive')), sortable: true, filter: 'multi', width: 130,
      render: (u) => <StatusPill tone={u.active ? 'good' : 'neutral'}>{u.active ? t('users.active') : t('users.inactive')}</StatusPill>,
    },
    {
      id: 'actions', title: '', value: () => '', width: 64, align: 'right',
      render: (u) => (
        <Menu position="bottom-end" withinPortal>
          <Menu.Target>
            <ActionIcon variant="subtle" color="gray" size="lg" aria-label={t('users.actionsAria', { name: u.displayName || u.account })}><IconDots size={18} /></ActionIcon>
          </Menu.Target>
          <Menu.Dropdown>
            <Menu.Item leftSection={<IconUserCog size={16} />} onClick={() => setEditing(u)}>{t('users.editRoles')}</Menu.Item>
            <Menu.Item leftSection={u.active ? <IconUserOff size={16} /> : <IconUserCheck size={16} />} onClick={() => toggle.mutate(u)}>
              {u.active ? t('users.deactivate') : t('users.activate')}
            </Menu.Item>
            <Menu.Divider />
            <Menu.Item color="red" leftSection={<IconTrash size={16} />} onClick={() => askDelete(u)}>{t('users.delete')}</Menu.Item>
          </Menu.Dropdown>
        </Menu>
      ),
    },
  ]

  const addButton = <Button data-tour="user-add" leftSection={<IconPlus size={16} />} onClick={() => setAdding(true)}>{t('users.addUser')}</Button>

  return (
    <>
      <PageHeader title={t('users.title')} description={t('users.sub')} actions={can('Users.Manage') ? addButton : undefined} />
      {loadError ? (
        <Callout kind="bad" action={<Button mt="xs" size="xs" variant="default" onClick={() => { void users.refetch(); void roles.refetch() }}>{t('ui.retry')}</Button>}>
          {loadError instanceof ApiError && loadError.status === 404 ? t('users.notAvailable') : errorText(loadError)}
        </Callout>
      ) : (
        <DataGrid<User>
          storageKey="users"
          exportName="users"
          records={users.data ?? []}
          loading={users.isLoading}
          getId={(u) => u.account}
          columns={columns}
          emptyTitle={t('users.noUsers')}
          emptyAction={can('Users.Manage') ? addButton : undefined}
        />
      )}

      <Drawer opened={adding} onClose={() => setAdding(false)} position="right" size="md" title={t('users.addUser')}>
        <AddUserForm roles={roles.data ?? []} onDone={() => { setAdding(false); changed(t('users.added')) }} onCancel={() => setAdding(false)} />
      </Drawer>
      <Drawer opened={editing !== null} onClose={() => setEditing(null)} position="right" size="md" title={editing ? t('users.rolesOf', { name: editing.displayName || editing.account }) : ''}>
        {editing && <EditRolesForm user={editing} roles={roles.data ?? []} onDone={() => { setEditing(null); changed(t('users.rolesSaved')) }} onCancel={() => setEditing(null)} />}
      </Drawer>
    </>
  )
}

/** Renders the form for adding a user with initial roles. */
function AddUserForm({ roles, onDone, onCancel }: { roles: Role[]; onDone: () => void; onCancel: () => void }) {
  const t = useT()
  const [account, setAccount] = useState('')
  const [name, setName] = useState('')
  const [roleIds, setRoleIds] = useState<string[]>([])
  const m = useMutation({
    mutationFn: () => post('/api/admin/users', { account: account.trim(), displayName: name.trim(), roleIds }),
    onSuccess: onDone,
  })
  return (
    <form onSubmit={(e) => { e.preventDefault(); m.mutate() }}>
      <Stack gap="md">
        {m.isError && <Callout kind="bad">{errorText(m.error)}</Callout>}
        <TextInput wrapperProps={{ 'data-tour': 'user-form-account' }} label={t('users.accountAria')} placeholder={t('users.accountPlaceholder')} value={account} onChange={(e) => setAccount(e.currentTarget.value)} maxLength={256} required data-autofocus />
        <TextInput label={t('users.nameAria')} placeholder={t('users.namePlaceholder')} value={name} onChange={(e) => setName(e.currentTarget.value)} maxLength={128} />
        <Stack gap={6} data-tour="user-form-roles">
          <Text size="sm" fw={500}>{t('users.roles')}</Text>
          <AccessRoleChecks roles={roles} value={roleIds} onChange={setRoleIds} lockedLabel={t('users.roleLocked')} />
        </Stack>
        <Group justify="flex-end" wrap="wrap">
          <Button variant="default" onClick={onCancel}>{t('ui.cancel')}</Button>
          <Button data-tour="user-form-save" type="submit" loading={m.isPending} disabled={account.trim() === ''}>{t('users.addUser')}</Button>
        </Group>
      </Stack>
    </form>
  )
}

/** Renders the form for editing a user's roles. */
function EditRolesForm({ user, roles, onDone, onCancel }: { user: User; roles: Role[]; onDone: () => void; onCancel: () => void }) {
  const t = useT()
  const [roleIds, setRoleIds] = useState<string[]>(user.roleIds)
  const m = useMutation({
    mutationFn: () => put(`/api/admin/users/roles?account=${encodeURIComponent(user.account)}`, { roleIds }),
    onSuccess: onDone,
  })
  return (
    <Stack gap="md">
      {m.isError && <Callout kind="bad">{errorText(m.error)}</Callout>}
      <Text size="sm" c="dimmed">{user.account}</Text>
      <AccessRoleChecks roles={roles} value={roleIds} onChange={setRoleIds} lockedLabel={t('users.roleLocked')} />
      <Group justify="flex-end" wrap="wrap">
        <Button variant="default" onClick={onCancel}>{t('ui.cancel')}</Button>
        <Button loading={m.isPending} onClick={() => m.mutate()}>{t('users.saveRoles')}</Button>
      </Group>
    </Stack>
  )
}
