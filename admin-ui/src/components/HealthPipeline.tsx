import { Fragment } from 'react'
import { Box, Card, Group, Stack, Text } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { IconCheck, IconX } from '@tabler/icons-react'
import { StatusPill } from './StatusPill'

export type HealthItem = { name: string; ok: boolean; ms: number; detail?: string }

/** Shows service health as a flow of connected CRM, database and model nodes so a break is visible at a glance. */
export function HealthPipeline({ items, okText, failText, allOk, title }: { items: HealthItem[]; okText: string; failText: string; allOk: boolean; title: string }) {
  const mobile = useMediaQuery('(max-width: 48em)')
  if (mobile) {
    return (
      <Card padding="md">
        <Group justify="space-between" mb="sm" wrap="wrap">
          <Text fw={600}>{title}</Text>
          <StatusPill tone={allOk ? 'good' : 'bad'}>{allOk ? okText : failText}</StatusPill>
        </Group>
        <Stack gap="sm">
          {items.map((h) => (
            <Group key={h.name} wrap="nowrap" align="flex-start" gap="sm">
              <Box w={28} h={28} style={{ flex: 'none', borderRadius: '50%', display: 'grid', placeItems: 'center', background: h.ok ? 'var(--mantine-color-teal-light)' : 'var(--mantine-color-red-light)', color: h.ok ? 'var(--mantine-color-teal-light-color)' : 'var(--mantine-color-red-light-color)' }}>
                {h.ok ? <IconCheck size={16} /> : <IconX size={16} />}
              </Box>
              <div style={{ minWidth: 0 }}>
                <Text size="sm" fw={500}>{h.name}</Text>
                <Text size="xs" c="dimmed">{h.ms} ms</Text>
                {h.detail && <Text size="xs" c="red" style={{ overflowWrap: 'anywhere' }}>{h.detail}</Text>}
              </div>
            </Group>
          ))}
        </Stack>
      </Card>
    )
  }
  return (
    <Card padding="md">
      <Group justify="space-between" mb="sm" wrap="wrap">
        <Text fw={600}>{title}</Text>
        <StatusPill tone={allOk ? 'good' : 'bad'}>{allOk ? okText : failText}</StatusPill>
      </Group>
      <Group gap={0} wrap="nowrap" align="stretch" style={{ overflowX: 'auto' }}>
        {items.map((h, i) => (
          <Fragment key={h.name}>
            <Stack gap={2} align="center" miw={150} style={{ flex: '1 1 0' }}>
              <Box
                w={34}
                h={34}
                style={{
                  borderRadius: '50%',
                  display: 'grid',
                  placeItems: 'center',
                  background: h.ok ? 'var(--mantine-color-teal-light)' : 'var(--mantine-color-red-light)',
                  color: h.ok ? 'var(--mantine-color-teal-light-color)' : 'var(--mantine-color-red-light-color)',
                  border: `1.5px solid ${h.ok ? 'var(--mantine-color-teal-4)' : 'var(--mantine-color-red-4)'}`,
                }}
              >
                {h.ok ? <IconCheck size={18} /> : <IconX size={18} />}
              </Box>
              <Text size="sm" fw={500} ta="center">{h.name}</Text>
              <Text size="xs" c="dimmed">{h.ms} ms</Text>
              {h.detail && <Text size="xs" c="red" ta="center">{h.detail}</Text>}
            </Stack>
            {i < items.length - 1 && (
              <Box style={{ flex: '0 0 48px', alignSelf: 'flex-start', marginTop: 17 }}>
                <Box h={2} style={{ background: 'var(--mantine-color-default-border)', borderRadius: 2 }} />
              </Box>
            )}
          </Fragment>
        ))}
      </Group>
    </Card>
  )
}
