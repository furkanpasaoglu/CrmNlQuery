import { Checkbox, Stack, Tooltip } from '@mantine/core'
import { ApiError } from '../api/client'
import { useMe } from '../auth/me'

export type Role = { id: string; name: string; builtin: boolean; permissions: string[] }

/** React hook mirroring the server's no-escalation rule so buttons are disabled for roles the user cannot grant; the real rule is on the server. */
export function useCanGive() {
  const { me, can } = useMe()
  return (role: Role) => (role.builtin ? me.isAdministrator : role.permissions.every((p) => can(p)))
}

/** Returns the server's own error message if there is one, otherwise the raw message. */
export function errorText(e: unknown): string {
  if (e instanceof ApiError) return e.message
  return e instanceof Error ? e.message : String(e)
}

type Props = {
  roles: Role[]
  value: string[]
  onChange: (ids: string[]) => void
  lockedLabel: string
}

/** Renders role checkboxes, with roles the user cannot grant disabled and already assigned roles kept checked. */
export function AccessRoleChecks({ roles, value, onChange, lockedLabel }: Props) {
  const canGive = useCanGive()
  return (
    <Stack gap="xs">
      {roles.map((r) => {
        const allowed = canGive(r)
        return (
          <Tooltip key={r.id} label={lockedLabel} disabled={allowed} multiline w={260} withArrow>
            <div>
              <Checkbox
                label={r.name}
                checked={value.includes(r.id)}
                disabled={!allowed}
                onChange={(e) => onChange(e.currentTarget.checked ? [...value, r.id] : value.filter((x) => x !== r.id))}
              />
            </div>
          </Tooltip>
        )
      })}
    </Stack>
  )
}
