import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Card, Checkbox, Grid, Group, NavLink, Select, Stack, Text, TextInput, Tooltip } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import { IconPlus, IconTrash } from '@tabler/icons-react'
import { ApiError, del, get, post, put } from '../api/client'
import { useMe } from '../auth/me'
import { useT, useTOr } from '../i18n'
import { PageHeader } from '../components/PageHeader'
import { Callout } from '../components/Callout'
import { confirmAction } from '../components/confirm'
import { errorText, type Role } from '../components/AccessRoleChecks'

type Permission = { key: string; group: string; description: string }

/** Splits a description so the first sentence becomes the label and the rest goes on a second line. */
function splitDescription(text: string): [string, string] {
  const dot = text.search(/\.\s/)
  let head = dot >= 0 ? text.slice(0, dot) : text.replace(/\.$/, '')
  let rest = dot >= 0 ? text.slice(dot + 2).trim() : ''
  if (head.length > 90) {
    const cut = head.search(/[:;]\s/)
    if (cut > 0) {
      const tail = head.slice(cut + 2).trim()
      head = head.slice(0, cut)
      rest = rest ? `${tail}. ${rest}` : tail
    }
  }
  return [head, rest ? rest.charAt(0).toUpperCase() + rest.slice(1) : '']
}

/** Renders the Roles admin page for managing roles and permissions. */
export default function Roles() {
  const t = useT()
  const mobile = useMediaQuery('(max-width: 48em)')
  const [selectedId, setSelectedId] = useState('')

  const roles = useQuery({ queryKey: ['roles'], queryFn: () => get<Role[]>('/api/admin/roles') })
  const permissions = useQuery({ queryKey: ['permissions'], queryFn: () => get<Permission[]>('/api/admin/permissions') })

  const list = roles.data ?? []
  const activeId = selectedId === 'new' ? 'new' : (list.find((r) => r.id === selectedId)?.id ?? list[0]?.id ?? 'new')
  const role = list.find((r) => r.id === activeId) ?? null
  const loadError = roles.error ?? permissions.error

  return (
    <>
      <PageHeader
        title={t('roles.title')}
        description={t('roles.intro')}
        actions={<Button leftSection={<IconPlus size={16} />} onClick={() => setSelectedId('new')}>{t('roles.addRole')}</Button>}
      />
      {loadError ? (
        <Callout kind="bad" action={<Button mt="xs" size="xs" variant="default" onClick={() => { void roles.refetch(); void permissions.refetch() }}>{t('ui.retry')}</Button>}>
          {loadError instanceof ApiError && loadError.status === 404 ? t('roles.notAvailable') : errorText(loadError)}
        </Callout>
      ) : roles.isLoading || permissions.isLoading ? (
        <Text c="dimmed">{t('roles.loading')}</Text>
      ) : (
        <Grid gap="md">
          <Grid.Col span={{ base: 12, md: 4, lg: 3 }}>
            {mobile ? (
              <Select
                aria-label={t('roles.title')}
                data={[...list.map((r) => ({ value: r.id, label: r.name })), { value: 'new', label: t('roles.newRole') }]}
                value={activeId}
                onChange={(v) => { if (v) setSelectedId(v) }}
                allowDeselect={false}
              />
            ) : (
              <Card padding="xs" className="grid-card" data-tour="roles-list">
                <Stack gap={2}>
                  {list.map((r) => (
                    <NavLink
                      key={r.id}
                      label={r.name}
                      active={r.id === activeId}
                      rightSection={r.builtin ? <Badge variant="light" color="gray" tt="none" fw={500}>{t('roles.builtInBadge')}</Badge> : null}
                      onClick={() => setSelectedId(r.id)}
                    />
                  ))}
                  <NavLink label={t('roles.newRole')} active={activeId === 'new'} onClick={() => setSelectedId('new')} />
                </Stack>
              </Card>
            )}
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 8, lg: 9 }}>
            <RoleDetail
              key={`${activeId}:${role ? role.name + '|' + role.permissions.join(',') : ''}`}
              role={role}
              permissions={permissions.data ?? []}
              onSelect={setSelectedId}
            />
          </Grid.Col>
        </Grid>
      )}
    </>
  )
}

/** Renders the detail editor for one role. */
function RoleDetail({ role, permissions, onSelect }: { role: Role | null; permissions: Permission[]; onSelect: (id: string) => void }) {
  const t = useT()
  const tOr = useTOr()
  const qc = useQueryClient()
  const { can, refresh } = useMe()
  const isNew = role === null
  const locked = !isNew && role.builtin

  const [name, setName] = useState(role?.name ?? '')
  const [picked, setPicked] = useState<string[]>(role?.permissions ?? [])
  const original = role?.permissions ?? []

  const disabled = (key: string) => locked || (!can(key) && !original.includes(key))

  const groups: Record<string, Permission[]> = {}
  for (const p of permissions) (groups[p.group] ??= []).push(p)

  const toggleGroup = (items: Permission[]) => {
    const free = items.filter((p) => !disabled(p.key)).map((p) => p.key)
    const allOn = free.every((k) => picked.includes(k))
    setPicked((cur) => (allOn ? cur.filter((k) => !free.includes(k)) : Array.from(new Set([...cur, ...free]))))
  }

  const done = (message: string) => {
    notifications.show({ color: 'teal', message })
    void qc.invalidateQueries({ queryKey: ['roles'] })
    refresh()
  }

  const save = useMutation({
    mutationFn: () => {
      const body = { name: name.trim(), permissions: permissions.map((p) => p.key).filter((k) => picked.includes(k)) }
      return isNew ? post<{ id?: string }>('/api/admin/roles', body) : put(`/api/admin/roles/${encodeURIComponent(role.id)}`, body)
    },
    onSuccess: (data) => {
      done(isNew ? t('roles.roleCreated') : t('roles.changesSaved'))
      if (isNew && data && (data as { id?: string }).id) onSelect((data as { id: string }).id)
    },
  })
  const remove = useMutation({
    mutationFn: () => del(`/api/admin/roles/${encodeURIComponent(role!.id)}`),
    onSuccess: () => { onSelect(''); done(t('roles.roleDeleted')) },
    onError: (e) => notifications.show({ color: 'red', message: errorText(e) }),
  })

  const dirty = isNew ? name.trim() !== '' : name.trim() !== role.name || picked.length !== original.length || picked.some((k) => !original.includes(k))

  return (
    <Card padding="md" className="grid-card" data-tour="roles-form">
      <Stack gap="lg">
        {locked && <Callout kind="info">{t('roles.builtInNote')}</Callout>}
        {save.isError && <Callout kind="bad">{errorText(save.error)}</Callout>}
        <TextInput label={t('roles.roleName')} value={name} onChange={(e) => setName(e.currentTarget.value)} maxLength={64} disabled={locked} maw={420} />

        {Object.entries(groups).map(([group, items]) => {
          const free = items.filter((p) => !disabled(p.key))
          const allOn = free.length > 0 && free.every((p) => picked.includes(p.key))
          return (
            <Stack key={group} gap="xs">
              <Group justify="space-between" wrap="nowrap">
                <Text fw={600}>{tOr('shell.group' + group, group)}</Text>
                {free.length > 0 && (
                  <Button variant="subtle" size="compact-sm" onClick={() => toggleGroup(items)}>{allOn ? t('roles.clearGroup') : t('roles.selectGroup')}</Button>
                )}
              </Group>
              <Stack gap="sm">
                {items.map((p) => {
                  const [label, rest] = splitDescription(tOr('perm.' + p.key, p.description))
                  const off = disabled(p.key)
                  return (
                    <Tooltip key={p.key} label={t('roles.cannotGive')} disabled={!off || locked} multiline w={260} withArrow>
                      <div>
                        <Checkbox
                          label={label}
                          description={
                            <>
                              {rest && <Text size="xs" c="dimmed">{rest}</Text>}
                              <Text size="xs" c="dimmed" ff="monospace">{p.key}</Text>
                            </>
                          }
                          checked={picked.includes(p.key)}
                          disabled={off}
                          onChange={(e) => {
                            const on = e.currentTarget.checked
                            setPicked((cur) => (on ? [...cur, p.key] : cur.filter((k) => k !== p.key)))
                          }}
                        />
                      </div>
                    </Tooltip>
                  )
                })}
              </Stack>
            </Stack>
          )
        })}

        {!locked && (
          <Group justify="space-between" wrap="wrap">
            {!isNew ? (
              <Button
                variant="light"
                color="red"
                leftSection={<IconTrash size={16} />}
                loading={remove.isPending}
                onClick={() => confirmAction({
                  title: t('roles.deleteTitle', { name: role.name }),
                  message: t('roles.deleteMessage'),
                  confirmLabel: t('roles.deleteRole'),
                  cancelLabel: t('ui.cancel'),
                  danger: true,
                  onConfirm: () => remove.mutate(),
                })}
              >
                {t('roles.deleteRole')}
              </Button>
            ) : <span />}
            <Button loading={save.isPending} disabled={!dirty || name.trim() === ''} onClick={() => save.mutate()}>
              {isNew ? t('roles.createRole') : t('roles.saveChanges')}
            </Button>
          </Group>
        )}
      </Stack>
    </Card>
  )
}
