import { ActionIcon, Indicator, Menu } from '@mantine/core'
import { IconHandClick, IconHelp, IconRoute } from '@tabler/icons-react'
import { useLocation } from 'react-router-dom'
import { useI18n } from '../i18n'
import { useTour } from './TourProvider'

/** Renders the help menu button next to the profile with the page tour and welcome tour, showing a dot for unseen tours. */
export function TourHelpMenu() {
  const { t } = useI18n()
  const { start, has, seen } = useTour()
  const pageId = useLocation().pathname.split('/').filter(Boolean)[0] ?? ''
  const hasPage = has(pageId)
  const fresh = hasPage && !seen(pageId)
  return (
    <Menu position="bottom-end" width={260} withinPortal>
      <Menu.Target>
        <Indicator disabled={!fresh} size={9} offset={4} color="cobalt" processing>
          <ActionIcon variant="default" size="lg" aria-label={t('tour.ui.help')} data-tour="help"><IconHelp size={18} /></ActionIcon>
        </Indicator>
      </Menu.Target>
      <Menu.Dropdown>
        <Menu.Label>{t('tour.ui.help')}</Menu.Label>
        <Menu.Item leftSection={<IconHandClick size={16} />} disabled={!hasPage} onClick={() => start(pageId)}>
          {hasPage ? t('tour.ui.pageTour') : t('tour.ui.noPageTour')}
        </Menu.Item>
        <Menu.Item leftSection={<IconRoute size={16} />} onClick={() => start('welcome')}>{t('tour.ui.welcomeTour')}</Menu.Item>
      </Menu.Dropdown>
    </Menu>
  )
}
