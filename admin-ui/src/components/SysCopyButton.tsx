import { useState } from 'react'
import { Button, CopyButton, ActionIcon, Tooltip } from '@mantine/core'
import { IconCheck, IconCopy } from '@tabler/icons-react'
import { useT } from '../i18n'

/** Renders a copy-to-clipboard button, with text or icon only, doing nothing quietly when the clipboard is unavailable. */
export function SysCopyButton({ value, label, icon }: { value: string; label?: string; icon?: boolean }) {
  const t = useT()
  const [, setTick] = useState(0)
  const text = label ?? t('ui.copy')
  return (
    <CopyButton value={value} timeout={1800}>
      {({ copied, copy }) => icon ? (
        <Tooltip label={copied ? t('ui.copied') : text}>
          <ActionIcon variant="default" size="lg" aria-label={text} onClick={() => { copy(); setTick((n) => n + 1) }}>
            {copied ? <IconCheck size={16} /> : <IconCopy size={16} />}
          </ActionIcon>
        </Tooltip>
      ) : (
        <Button variant="default" leftSection={copied ? <IconCheck size={16} /> : <IconCopy size={16} />} onClick={copy}>
          {copied ? t('ui.copied') : text}
        </Button>
      )}
    </CopyButton>
  )
}
