import { createContext, useContext, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button, Center, Loader, Stack, Text, Title } from '@mantine/core'
import { ApiError, get } from '../api/client'
import { useT, type Lang } from '../i18n'

export type Me = {
  account: string
  name: string
  roles: string[]
  isAdministrator: boolean
  language: Lang
  languages: Lang[]
  appHome: string
  permissions: string[]
}

type Ctx = { me: Me; can: (permission?: string) => boolean; refresh: () => void }
const MeContext = createContext<Ctx | null>(null)

/** Returns the current admin identity from the context. */
export function useMe() {
  const c = useContext(MeContext)
  if (!c) throw new Error('MeProvider yok')
  return c
}

/** Provides the admin identity to the app and blocks rendering until the user is authenticated. */
export function MeProvider({ children }: { children: ReactNode }) {
  const t = useT()
  const q = useQuery({ queryKey: ['me'], queryFn: () => get<Me>('/api/admin/me'), retry: false, staleTime: 60_000 })

  if (q.isLoading) return <Center h="100vh"><Loader size="sm" /></Center>
  if (q.isError || !q.data) {
    const e = q.error instanceof ApiError ? q.error : null
    const title = e?.status === 401 ? t('shell.signInRequired') : e?.status === 404 ? t('shell.noPermission') : t('shell.unreachable', { msg: e?.message ?? '' })
    const text = e?.status === 401 ? t('shell.signedInNote') : e?.status === 404 ? t('shell.noPermissionText') : ''
    return (
      <Center h="100vh" p="lg">
        <Stack align="center" gap="xs" maw={460}>
          <Title order={3} ta="center">{title}</Title>
          {text && <Text c="dimmed" ta="center">{text}</Text>}
          <Button variant="light" mt="sm" onClick={() => q.refetch()}>{t('shell.tryAgain')}</Button>
        </Stack>
      </Center>
    )
  }
  const me = q.data
  const can = (p?: string) => !p || me.isAdministrator || me.permissions.includes(p)
  return <MeContext.Provider value={{ me, can, refresh: () => { void q.refetch() } }}>{children}</MeContext.Provider>
}
