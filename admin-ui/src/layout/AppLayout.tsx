import { Suspense, useEffect, useMemo } from 'react'
import { Link, Navigate, Route, Routes, useLocation } from 'react-router-dom'
import {
  ActionIcon, AppShell, Avatar, Badge, Box, Burger, Center, Group, Loader, Menu, ScrollArea, SegmentedControl, Stack, Text, Tooltip, UnstyledButton,
  useMantineColorScheme,
} from '@mantine/core'
import { useDisclosure, useLocalStorage } from '@mantine/hooks'
import { TourProvider } from '../tour/TourProvider'
import { TourHelpMenu } from '../tour/TourHelpMenu'
import { Spotlight, spotlight, type SpotlightActionData } from '@mantine/spotlight'
import { IconChevronsLeft, IconChevronsRight, IconCircleHalf2, IconLogout2, IconMoon, IconSearch, IconSun } from '@tabler/icons-react'
import { put } from '../api/client'
import { useMe } from '../auth/me'
import { useI18n } from '../i18n'
import { allPages, groups } from '../nav'
import { EmptyState } from '../components/EmptyState'

const RAIL = 72
const WIDE = 268

/** Returns the initials of a person's name for an avatar. */
function initials(name: string) {
  const parts = name.split(/[\s\\]+/).filter(Boolean)
  return ((parts[0]?.[0] ?? '') + (parts.length > 1 ? parts[parts.length - 1][0] : '')).toUpperCase()
}

/** Renders the admin app shell layout with navigation, header and content area. */
export function AppLayout() {
  const { t, lang, setLang } = useI18n()
  const { me, can } = useMe()
  const location = useLocation()
  const [mobileOpen, { toggle: toggleMobile, close: closeMobile, open: openMobile }] = useDisclosure()
  const [rail, setRail] = useLocalStorage({ key: 'admin-rail', defaultValue: false })

  const visibleGroups = useMemo(
    () => groups.map((g) => ({ ...g, pages: g.pages.filter((p) => can(p.permission)) })).filter((g) => g.pages.length > 0),
    [can],
  )
  const pages = useMemo(() => visibleGroups.flatMap((g) => g.pages.map((p) => ({ ...p, group: g }))), [visibleGroups])

  useEffect(() => {
    let chosen: string | null = null
    try { chosen = localStorage.getItem('admin-lang') } catch {  }
    if (!chosen && (me.language === 'en' || me.language === 'tr')) setLang(me.language)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const currentId = location.pathname.replace(/^\//, '').split('/')[0]
  const current = pages.find((p) => p.id === currentId)
  useEffect(() => { closeMobile() }, [currentId, closeMobile])
  useEffect(() => { document.title = `${current ? t(current.titleKey) : t('shell.brandSub')} · NL Query` }, [current, t, lang])

  useEffect(() => {
    const onNav = (e: Event) => { if ((e as CustomEvent<{ open: boolean }>).detail?.open) openMobile(); else closeMobile() }
    window.addEventListener('tour:nav', onNav)
    return () => window.removeEventListener('tour:nav', onNav)
  }, [openMobile, closeMobile])

  const actions: SpotlightActionData[] = pages.map((p) => ({
    id: p.id,
    label: t(p.titleKey),
    description: t(p.group.titleKey),
    leftSection: p.icon,
    onClick: () => { window.location.hash = '/' + p.id },
  }))

  const navWidth = rail ? RAIL : WIDE

  return (
    <TourProvider autoStart="welcome">
    <AppShell
      header={{ height: 60 }}
      navbar={{ width: navWidth, breakpoint: 'sm', collapsed: { mobile: !mobileOpen } }}
      padding={{ base: 'sm', sm: 'lg' }}
      transitionDuration={150}
      style={{ "--nav-w": navWidth + "px" } as React.CSSProperties}
    >
      <AppShell.Header className="app-header" style={{ background: 'var(--mantine-color-body)' }}>
        <Group h="100%" px={{ base: 'sm', sm: 'lg' }} justify="space-between" wrap="nowrap">
          <Group gap="sm" wrap="nowrap" style={{ minWidth: 0 }}>
            <Burger opened={mobileOpen} onClick={toggleMobile} hiddenFrom="sm" size="sm" aria-label={t('shell.openMenuAria')} />
            {current && (
              <Box style={{ minWidth: 0 }}>
                <Text size="xs" c="dimmed" truncate>{t(current.group.titleKey)}</Text>
                <Text fw={600} lh={1.2} truncate>{t(current.titleKey)}</Text>
              </Box>
            )}
          </Group>

          <Group gap="xs" wrap="nowrap">
            <UnstyledButton
              onClick={() => spotlight.open()}
              visibleFrom="md"
              data-tour="search"
              aria-label={t('shell.searchHint')}
              style={{ border: '1px solid var(--mantine-color-default-border)', borderRadius: 8, padding: '6px 10px', minWidth: 260, background: 'var(--app-canvas)' }}
            >
              <Group gap={8} justify="space-between" wrap="nowrap">
                <Group gap={8} wrap="nowrap"><IconSearch size={16} /><Text size="sm" c="dimmed">{t('shell.searchHint')}</Text></Group>
                <Text size="xs" c="dimmed" className="mono">Ctrl K</Text>
              </Group>
            </UnstyledButton>
            <ActionIcon variant="default" size="lg" hiddenFrom="md" data-tour="search" onClick={() => spotlight.open()} aria-label={t('shell.searchHint')}><IconSearch size={18} /></ActionIcon>

            <SegmentedControl
              size="xs"
              visibleFrom="xs"
              data-tour="lang"
              value={lang}
              onChange={(v) => { const l = v as 'en' | 'tr'; setLang(l); void put('/api/admin/me/language', { language: l }).catch(() => undefined) }}
              data={[{ value: 'tr', label: 'TR' }, { value: 'en', label: 'EN' }]}
              aria-label={t('shell.languageLabel')}
            />
            <ThemeMenu />
            <TourHelpMenu />
            <UserMenu />
          </Group>
        </Group>
      </AppShell.Header>

      <AppShell.Navbar className="app-navbar" style={{ background: 'var(--app-sidebar)', borderRight: 0 }}>
        <Group h={60} px="md" gap="sm" wrap="nowrap" justify={rail ? 'center' : 'flex-start'} style={{ borderBottom: '1px solid rgba(255,255,255,0.08)' }}>
          <Center w={34} h={34} style={{ borderRadius: 9, background: 'var(--mantine-color-cobalt-6)', color: '#fff', fontWeight: 700, flex: 'none' }}>NQ</Center>
          {!rail && (
            <Box style={{ minWidth: 0 }}>
              <Text c="#fff" fw={600} lh={1.15}>NL Query</Text>
              <Text size="xs" c="ink.3" lh={1.15} truncate>{t('shell.brandSub')}</Text>
            </Box>
          )}
        </Group>

        <AppShell.Section grow component={ScrollArea} px="xs" pb="sm">
          <Stack gap={2} component="nav" aria-label={t('shell.navLabel')} data-tour="nav">
            {visibleGroups.map((g) => (
              <Box key={g.id}>
                {!rail && <div className="nav-group-title">{t(g.titleKey)}</div>}
                {rail && <div style={{ height: 10 }} />}
                {g.pages.map((p) => {
                  const active = currentId === p.id
                  const link = (
                    <UnstyledButton
                      component={Link}
                      to={'/' + p.id}
                      className="nav-link"
                      data-active={active || undefined}
                      aria-current={active ? 'page' : undefined}
                      px="sm"
                      py={8}
                      mb={2}
                      display="block"
                      w="100%"
                    >
                      <Group gap="sm" wrap="nowrap" justify={rail ? 'center' : 'flex-start'}>
                        {p.icon}
                        {!rail && <Text size="sm" fw={active ? 600 : 400} c="inherit" truncate>{t(p.titleKey)}</Text>}
                      </Group>
                    </UnstyledButton>
                  )
                  return rail ? <Tooltip key={p.id} label={t(p.titleKey)} position="right">{link}</Tooltip> : <Box key={p.id}>{link}</Box>
                })}
              </Box>
            ))}
          </Stack>
        </AppShell.Section>

        <AppShell.Section visibleFrom="sm" p="xs" style={{ borderTop: '1px solid rgba(255,255,255,0.08)' }}>
          <UnstyledButton className="nav-link" w="100%" px="sm" py={8} onClick={() => setRail((r) => !r)} aria-label={rail ? t('shell.expand') : t('shell.collapse')}>
            <Group gap="sm" justify={rail ? 'center' : 'flex-start'}>
              {rail ? <IconChevronsRight size={18} /> : <IconChevronsLeft size={18} />}
              {!rail && <Text size="sm" c="inherit">{t('shell.collapse')}</Text>}
            </Group>
          </UnstyledButton>
        </AppShell.Section>
      </AppShell.Navbar>

      <AppShell.Main>
        <div className="content">
          <Suspense fallback={<Center py={80}><Loader size="sm" /></Center>}>
            <Routes>
              <Route path="/" element={<Navigate to={'/' + (pages[0]?.id ?? 'dashboard')} replace />} />
              {pages.map((p) => <Route key={p.id} path={'/' + p.id} element={<p.component />} />)}
              <Route path="*" element={<EmptyState title={t('shell.pageNotFound')} text={t('shell.pageNotFoundText')} />} />
            </Routes>
          </Suspense>
        </div>
      </AppShell.Main>

      <Spotlight
        actions={actions}
        shortcut={['mod + K', '/']}
        nothingFound={t('shell.searchNone')}
        searchProps={{ placeholder: t('shell.searchHint') }}
        highlightQuery
      />
    </AppShell>
    </TourProvider>
  )
}

/** Renders the theme menu for choosing light, dark or system theme. */
function ThemeMenu() {
  const { t } = useI18n()
  const { colorScheme, setColorScheme } = useMantineColorScheme()
  const Icon = colorScheme === 'dark' ? IconMoon : colorScheme === 'light' ? IconSun : IconCircleHalf2
  return (
    <Menu position="bottom-end" withinPortal>
      <Menu.Target><ActionIcon variant="default" size="lg" aria-label={t('shell.theme')} data-tour="theme"><Icon size={18} /></ActionIcon></Menu.Target>
      <Menu.Dropdown>
        <Menu.Label>{t('shell.appearance')}</Menu.Label>
        <Menu.Item leftSection={<IconSun size={16} />} onClick={() => setColorScheme('light')}>{t('shell.themeLight')}</Menu.Item>
        <Menu.Item leftSection={<IconMoon size={16} />} onClick={() => setColorScheme('dark')}>{t('shell.themeDark')}</Menu.Item>
        <Menu.Item leftSection={<IconCircleHalf2 size={16} />} onClick={() => setColorScheme('auto')}>{t('shell.themeSystem')}</Menu.Item>
      </Menu.Dropdown>
    </Menu>
  )
}

/** Renders the user menu with language and account options. */
function UserMenu() {
  const { t } = useI18n()
  const { me } = useMe()
  return (
    <Menu position="bottom-end" width={260} withinPortal>
      <Menu.Target>
        <UnstyledButton aria-label={t('shell.account')} data-tour="user"><Avatar color="cobalt" radius="xl" size={34}>{initials(me.name || me.account)}</Avatar></UnstyledButton>
      </Menu.Target>
      <Menu.Dropdown>
        <Box px="sm" py={6}>
          <Text fw={600} size="sm" truncate>{me.name || me.account}</Text>
          <Text size="xs" c="dimmed" style={{ overflowWrap: 'anywhere' }}>{me.account}</Text>
          <Group gap={4} mt={6}>
            {me.roles.length === 0 ? <Text size="xs" c="dimmed">{t('shell.noRole')}</Text> : me.roles.map((r) => <Badge key={r} size="sm" variant="light">{r}</Badge>)}
          </Group>
        </Box>
        {me.appHome && (<><Menu.Divider /><Menu.Item component="a" href={me.appHome} leftSection={<IconLogout2 size={16} />}>{t('shell.backToAppLabel')}</Menu.Item></>)}
      </Menu.Dropdown>
    </Menu>
  )
}

export { allPages }
