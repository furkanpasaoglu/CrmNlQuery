import type { ReactNode } from 'react'
import { Badge, Box } from '@mantine/core'

type Tone = 'good' | 'warn' | 'bad' | 'neutral'
const color = { good: 'teal', warn: 'yellow', bad: 'red', neutral: 'gray' } as const

/** Renders a status pill with a dot and always-visible text, so color alone carries no meaning. */
export function StatusPill({ tone = 'neutral', children }: { tone?: Tone; children: ReactNode }) {
  return (
    <Badge variant="light" color={color[tone]} tt="none" fw={500} leftSection={<Box w={7} h={7} style={{ borderRadius: '50%', background: 'currentColor' }} />}>
      {children}
    </Badge>
  )
}
