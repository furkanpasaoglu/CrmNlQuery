import type { ReactNode } from 'react'
import { Card, Group, Text } from '@mantine/core'

type Props = { title: string; description?: string; right?: ReactNode; children?: ReactNode; flush?: boolean }

/** Renders a titled section card, optionally with flush content. */
export function SectionCard({ title, description, right, children, flush }: Props) {
  return (
    <Card padding={0} className="grid-card">
      <Group justify="space-between" align="flex-start" wrap="wrap" gap="sm" p="md" pb={children ? 'sm' : 'md'}>
        <div style={{ flex: "1 1 240px", minWidth: 0 }}>
          <Text fw={600}>{title}</Text>
          {description && <Text size="sm" c="dimmed" mt={2}>{description}</Text>}
        </div>
        {right}
      </Group>
      {children && <div style={flush ? undefined : { padding: '0 var(--mantine-spacing-md) var(--mantine-spacing-md)' }}>{children}</div>}
    </Card>
  )
}
