import { useState, type ReactNode } from 'react'
import { Box, Collapse, Group, Stack, Text, Title, UnstyledButton } from '@mantine/core'
import { IconChevronDown } from '@tabler/icons-react'
import { useLocation } from 'react-router-dom'
import { useI18n } from '../i18n'
import { useHowItWorks } from './HowItWorks'

type Props = {
  title: string
  description?: string
  howTitle?: string
  how?: ReactNode
  actions?: ReactNode
}

/** Renders the standard header of an admin page, including the how-it-works card. */
export function PageHeader({ title, description, howTitle, how: howProp, actions }: Props) {
  const [open, setOpen] = useState(false)
  const { t } = useI18n()
  const pageId = useLocation().pathname.split('/').filter(Boolean)[0] ?? ''
  const auto = useHowItWorks(pageId)
  const how = howProp ?? auto
  howTitle = howTitle ?? t('shell.howItWorks')
  return (
    <Stack gap={6} mb="lg">
      <Group justify="space-between" align="flex-start" wrap="wrap" gap="sm">
        <Box maw={720} data-tour="page-title">
          <Title order={1}>{title}</Title>
          {description && <Text c="dimmed" mt={4} className="readable">{description}</Text>}
        </Box>
        {actions && <Group gap="xs" data-tour="page-actions">{actions}</Group>}
      </Group>
      {how && (
        <>
          <UnstyledButton onClick={() => setOpen((o) => !o)} aria-expanded={open} w="fit-content" data-tour="how">
            <Group gap={4}>
              <IconChevronDown size={16} style={{ transform: open ? 'rotate(180deg)' : undefined, transition: 'transform 150ms' }} />
              <Text size="sm" c="cobalt" fw={500}>{howTitle}</Text>
            </Group>
          </UnstyledButton>
          <Collapse expanded={open}>
            {typeof how === 'string' ? <Text size="sm" c="dimmed" className="readable">{how}</Text> : <Box mt={4} w="100%">{how}</Box>}
          </Collapse>
        </>
      )}
    </Stack>
  )
}
