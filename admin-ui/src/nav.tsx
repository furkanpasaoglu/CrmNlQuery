import { lazy, type ComponentType, type LazyExoticComponent, type ReactNode } from 'react'
import {
  IconBook2, IconReportAnalytics, IconChecklist, IconDatabaseCog, IconEyeSearch, IconFileExport, IconFiles, IconHistory, IconId, IconLayoutDashboard,
  IconListDetails, IconMessageSearch, IconSettings, IconShieldLock, IconSparkles, IconThumbUp, IconUsers, IconUsersGroup,
} from '@tabler/icons-react'

export type PageDef = {
  id: string
  titleKey: string
  icon: ReactNode
  permission?: string
  component: LazyExoticComponent<ComponentType>
}
export type GroupDef = { id: string; titleKey: string; pages: PageDef[] }

const modules = import.meta.glob<{ default: ComponentType }>('./pages/*.tsx')
/** Converts a hyphenated page id to PascalCase. */
const pascal = (id: string) => id.split('-').map((p) => p[0].toUpperCase() + p.slice(1)).join('')
/** Lazily loads a page component from its id, falling back to a coming-soon page. */
const lazyPage = (id: string) => lazy(modules[`./pages/${pascal(id)}.tsx`] ?? (() => import('./pages/ComingSoon')))
const sz = 18

export const groups: GroupDef[] = [
  {
    id: 'overview', titleKey: 'shell.navOverview', pages: [
      { id: 'dashboard', titleKey: 'dashboard.title', icon: <IconLayoutDashboard size={sz} />, permission: 'Dashboard.View', component: lazyPage('dashboard') },
      { id: 'logs', titleKey: 'logs.title', icon: <IconListDetails size={sz} />, permission: 'Logs.View', component: lazyPage('logs') },
    ],
  },
  {
    id: 'access', titleKey: 'shell.navAccess', pages: [
      { id: 'users', titleKey: 'users.title', icon: <IconUsers size={sz} />, permission: 'Users.Manage', component: lazyPage('users') },
      { id: 'groups', titleKey: 'groups.title', icon: <IconUsersGroup size={sz} />, permission: 'Users.Manage', component: lazyPage('groups') },
      { id: 'roles', titleKey: 'roles.title', icon: <IconShieldLock size={sz} />, permission: 'Roles.Manage', component: lazyPage('roles') },
      { id: 'audit', titleKey: 'audit.title', icon: <IconHistory size={sz} />, permission: 'Audit.View', component: lazyPage('audit') },
      { id: 'access-log', titleKey: 'access-log.title', icon: <IconEyeSearch size={sz} />, permission: 'AccessLog.View', component: lazyPage('access-log') },
      { id: 'question-view', titleKey: 'question-view.title', icon: <IconMessageSearch size={sz} />, permission: 'Questions.View', component: lazyPage('question-view') },
    ],
  },
  {
    id: 'data', titleKey: 'shell.navData', pages: [
      { id: 'data-access', titleKey: 'data-access.title', icon: <IconDatabaseCog size={sz} />, permission: 'DataPolicy.Manage', component: lazyPage('data-access') },
      { id: 'documents', titleKey: 'documents.title', icon: <IconFiles size={sz} />, permission: 'Documents.Manage', component: lazyPage('documents') },
      { id: 'glossary', titleKey: 'glossary.title', icon: <IconBook2 size={sz} />, permission: 'Glossary.Manage', component: lazyPage('glossary') },
      { id: 'reports', titleKey: 'reports.title', icon: <IconReportAnalytics size={sz} />, permission: 'Reports.Manage', component: lazyPage('reports') },
    ],
  },
  {
    id: 'quality', titleKey: 'shell.navQuality', pages: [
      { id: 'quality', titleKey: 'quality.title', icon: <IconChecklist size={sz} />, permission: 'Quality.Manage', component: lazyPage('quality') },
      { id: 'feedback', titleKey: 'feedback.title', icon: <IconThumbUp size={sz} />, permission: 'Feedback.View', component: lazyPage('feedback') },
    ],
  },
  {
    id: 'tools', titleKey: 'shell.navTools', pages: [
      { id: 'export', titleKey: 'export.title', icon: <IconFileExport size={sz} />, permission: 'Export.Run', component: lazyPage('export') },
      { id: 'design', titleKey: 'design.title', icon: <IconSparkles size={sz} />, permission: 'Design.Use', component: lazyPage('design') },
    ],
  },
  {
    id: 'system', titleKey: 'shell.navSystem', pages: [
      { id: 'settings', titleKey: 'settings.title', icon: <IconSettings size={sz} />, permission: 'Settings.Manage', component: lazyPage('settings') },
      { id: 'identity', titleKey: 'identity.title', icon: <IconId size={sz} />, component: lazyPage('identity') },
    ],
  },
]

export const allPages = groups.flatMap((g) => g.pages.map((p) => ({ ...p, group: g })))
