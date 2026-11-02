import { useLocation } from 'react-router-dom'
import { allPages } from '../nav'
import { useT } from '../i18n'
import { EmptyState } from '../components/EmptyState'

/** Shows a temporary placeholder page for pages not yet moved to the new design. */
export default function ComingSoon() {
  const t = useT()
  const id = useLocation().pathname.replace(/^\//, '')
  const page = allPages.find((p) => p.id === id)
  return <EmptyState title={page ? t(page.titleKey) : t('shell.pageNotFound')} text={t('shell.comingSoonText')} />
}
