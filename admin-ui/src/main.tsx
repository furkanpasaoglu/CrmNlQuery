import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { HashRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MantineProvider } from '@mantine/core'
import { ModalsProvider } from '@mantine/modals'
import { Notifications } from '@mantine/notifications'
import '@mantine/core/styles.css'
import '@mantine/charts/styles.css'
import '@mantine/dates/styles.css'
import '@mantine/notifications/styles.css'
import '@mantine/spotlight/styles.css'
import 'mantine-datatable/styles.css'
import './styles.css'
import { theme } from './theme'
import { I18nProvider } from './i18n'
import { MeProvider } from './auth/me'
import { AppLayout } from './layout/AppLayout'
import { takeHandoffFromOpener } from './api/client'

const nonce = document.querySelector<HTMLMetaElement>('meta[name="csp-nonce"]')?.content
const styleNonce = nonce && !nonce.startsWith('__') ? nonce : undefined

const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false, staleTime: 15_000 } } })

void takeHandoffFromOpener().finally(() => createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <MantineProvider theme={theme} defaultColorScheme="auto" getStyleNonce={styleNonce ? () => styleNonce : undefined}>
      <Notifications position="top-right" />
      <ModalsProvider>
      <QueryClientProvider client={queryClient}>
        <I18nProvider>
          <MeProvider>
            <HashRouter>
              <AppLayout />
            </HashRouter>
          </MeProvider>
        </I18nProvider>
      </QueryClientProvider>
      </ModalsProvider>
    </MantineProvider>
  </StrictMode>,
))
