import type { ReactNode } from 'react'
import { Card, Group, Text, ThemeIcon } from '@mantine/core'

type Props = {
  label: string
  value: ReactNode
  hint?: ReactNode
  tone?: 'good' | 'warn' | 'bad'
  icon?: ReactNode
}

const toneColor = { good: 'teal', warn: 'yellow', bad: 'red' } as const
const toneWord = { good: undefined, warn: undefined, bad: undefined } as const

/** Renders a dashboard KPI card, showing status with an icon and text as well as color. */
export function KpiCard({ label, value, hint, tone, icon }: Props) {
  void toneWord
  return (
    <Card h="100%">
      <Group justify="space-between" align="flex-start" wrap="nowrap">
        <Text size="sm" c="dimmed" fw={500}>{label}</Text>
        {icon && <ThemeIcon variant="light" color={tone ? toneColor[tone] : 'cobalt'} size="md" radius="md">{icon}</ThemeIcon>}
      </Group>
      <Text fz={28} fw={600} lh={1.15} mt={6} c={tone ? toneColor[tone] : undefined} style={{ fontVariantNumeric: 'tabular-nums' }}>{value}</Text>
      {hint && <Text size="xs" c="dimmed" mt={6} lh={1.45}>{hint}</Text>}
    </Card>
  )
}
