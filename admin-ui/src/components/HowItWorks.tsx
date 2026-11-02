import type { ReactNode } from 'react'
import { Box, Group, List, Paper, Stack, Text } from '@mantine/core'
import { IconBulb } from '@tabler/icons-react'
import { useI18n } from '../i18n'

type Example = { title: string; rows: Array<[label: string, value: string]>; result?: string }
type Props = {
  what: string
  steps?: string[]
  example?: Example
  tip?: string
}

/** Renders the collapsible How it works card for an admin page. */
export function HowItWorks({ what, steps, example, tip }: Props) {
  const { t } = useI18n()
  return (
    <Paper withBorder radius="md" p="md" w="100%">
      <Stack gap="md">
        <div>
          <Text size="sm" fw={600} mb={2}>{t('shell.howWhat')}</Text>
          <Text size="sm">{what}</Text>
        </div>

        {steps && steps.length > 0 && (
          <div>
            <Text size="sm" fw={600} mb={4}>{t('shell.howSteps')}</Text>
            <List type="ordered" size="sm" spacing={4}>
              {steps.map((s) => <List.Item key={s}>{s}</List.Item>)}
            </List>
          </div>
        )}

        {example && (
          <div>
            <Text size="sm" fw={600} mb={4}>{t('shell.howExample')}: {example.title}</Text>
            <Box p="sm" style={{ borderRadius: 'var(--mantine-radius-md)', background: 'var(--mantine-color-default-hover)' }}>
              <Stack gap={4}>
                {example.rows.map(([label, value]) => (
                  <Group key={label} gap="xs" wrap="nowrap" align="flex-start">
                    <Text size="sm" c="dimmed" w={110} style={{ flex: 'none' }}>{label}</Text>
                    <Text size="sm" style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{value}</Text>
                  </Group>
                ))}
              </Stack>
            </Box>
            {example.result && <Text size="sm" mt={6}><strong>{t('shell.howResult')}:</strong> {example.result}</Text>}
          </div>
        )}

        {tip && (
          <Group gap="xs" wrap="nowrap" align="flex-start">
            <IconBulb size={18} style={{ flex: 'none', marginTop: 2 }} aria-hidden />
            <Text size="sm">{tip}</Text>
          </Group>
        )}
      </Stack>
    </Paper>
  )
}

/** Builds the How it works card from the page id's howto keys when the catalog has them. */
export function useHowItWorks(pageId: string): ReactNode | null {
  const { t } = useI18n()
  const has = (key: string) => t(key) !== key
  const k = (s: string) => `howto.${pageId}.${s}`
  if (!has(k('what'))) return null
  const steps: string[] = []
  for (let n = 1; has(k('s' + n)); n++) steps.push(t(k('s' + n)))
  const rows: Array<[string, string]> = []
  for (let n = 1; has(k(`r${n}l`)); n++) rows.push([t(k(`r${n}l`)), t(k(`r${n}v`))])
  return (
    <HowItWorks
      what={t(k('what'))}
      steps={steps}
      example={has(k('exTitle')) && rows.length > 0 ? { title: t(k('exTitle')), rows } : undefined}
      tip={has(k('tip')) ? t(k('tip')) : undefined}
    />
  )
}
