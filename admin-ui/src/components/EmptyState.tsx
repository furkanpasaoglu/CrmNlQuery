import type { ReactNode } from 'react'
import { Center, Stack, Text, ThemeIcon } from '@mantine/core'
import { IconInbox } from '@tabler/icons-react'

type Props = { title: string; text?: string; action?: ReactNode; icon?: ReactNode }

/** Shows an empty state that explains what is missing and offers one action. */
export function EmptyState({ title, text, action, icon }: Props) {
  return (
    <Center py={48} px="md">
      <Stack align="center" gap={8} maw={440}>
        <ThemeIcon variant="light" color="gray" size={44} radius="xl">{icon ?? <IconInbox size={22} />}</ThemeIcon>
        <Text fw={600} ta="center">{title}</Text>
        {text && <Text c="dimmed" ta="center" size="sm">{text}</Text>}
        {action}
      </Stack>
    </Center>
  )
}
