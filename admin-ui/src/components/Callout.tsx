import type { ReactNode } from 'react'
import { Alert } from '@mantine/core'
import { IconAlertTriangle, IconCircleX, IconInfoCircle } from '@tabler/icons-react'

type Props = { kind: 'info' | 'warn' | 'bad'; title?: string; children: ReactNode; action?: ReactNode }

const look = {
  info: { color: 'cobalt', icon: <IconInfoCircle size={18} /> },
  warn: { color: 'yellow', icon: <IconAlertTriangle size={18} /> },
  bad: { color: 'red', icon: <IconCircleX size={18} /> },
} as const

/** Renders an info, warning or error callout box with an optional action. */
export function Callout({ kind, title, children, action }: Props) {
  const l = look[kind]
  return (
    <Alert color={l.color} icon={l.icon} title={title} variant="light" radius="md" role={kind === 'bad' ? 'alert' : 'note'}>
      {children}
      {action}
    </Alert>
  )
}
