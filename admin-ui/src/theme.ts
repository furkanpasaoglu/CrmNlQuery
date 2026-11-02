import { createTheme, type MantineColorsTuple } from '@mantine/core'

const cobalt: MantineColorsTuple = ['#eef3ff', '#dbe5ff', '#b7caff', '#8dabff', '#6690ff', '#4a7aff', '#2457f5', '#1c46d0', '#1737aa', '#112a85']
const ink: MantineColorsTuple = ['#e8ecf5', '#cbd3e6', '#a9b5d1', '#8696bb', '#667aa3', '#4d5f8a', '#3a4a73', '#2a385e', '#1b274a', '#111b38']

export const theme = createTheme({
  primaryColor: 'cobalt',
  primaryShade: { light: 6, dark: 5 },
  colors: { cobalt, ink, dark: ['#d5dbea', '#aab4cd', '#8793b3', '#66739a', '#344060', '#263150', '#1c2640', '#141c33', '#0f1629', '#0a1020'] },
  fontFamily: '"IBM Plex Sans Variable", system-ui, -apple-system, "Segoe UI", sans-serif',
  fontFamilyMonospace: '"IBM Plex Mono", ui-monospace, Consolas, monospace',
  headings: {
    fontFamily: '"IBM Plex Sans Variable", system-ui, sans-serif',
    fontWeight: '600',
    sizes: {
      h1: { fontSize: '1.75rem', lineHeight: '1.2' },
      h2: { fontSize: '1.375rem', lineHeight: '1.25' },
      h3: { fontSize: '1.125rem', lineHeight: '1.3' },
      h4: { fontSize: '1rem', lineHeight: '1.35' },
    },
  },
  fontSizes: { xs: '0.75rem', sm: '0.8125rem', md: '0.875rem', lg: '1rem', xl: '1.25rem' },
  radius: { xs: '3px', sm: '6px', md: '10px', lg: '14px', xl: '20px' },
  defaultRadius: 'sm',
  shadows: {
    xs: '0 1px 2px rgba(17, 27, 56, 0.06)',
    sm: '0 2px 6px rgba(17, 27, 56, 0.08)',
    md: '0 6px 18px rgba(17, 27, 56, 0.10)',
  },
  spacing: { xs: '0.5rem', sm: '0.75rem', md: '1rem', lg: '1.5rem', xl: '2rem' },
  cursorType: 'pointer',
  components: {
    Card: { defaultProps: { withBorder: true, radius: 'md', padding: 'md', shadow: 'xs' } },
    Paper: { defaultProps: { radius: 'md' } },
    Button: { defaultProps: { radius: 'sm' } },
    ActionIcon: { defaultProps: { radius: 'sm' } },
    Badge: { defaultProps: { radius: 'xl' } },
    Modal: { defaultProps: { radius: 'md', centered: true, overlayProps: { blur: 2, backgroundOpacity: 0.45 } } },
    Drawer: { defaultProps: { radius: 0, overlayProps: { blur: 2, backgroundOpacity: 0.45 } } },
    Tooltip: { defaultProps: { withArrow: true, openDelay: 250 } },
    TextInput: { defaultProps: { size: 'sm' } },
    Select: { defaultProps: { size: 'sm' } },
    MultiSelect: { defaultProps: { size: 'sm' } },
  },
})
