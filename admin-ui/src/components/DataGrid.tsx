import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { ActionIcon, Badge, Button, Card, Checkbox, Group, Menu, MultiSelect, Pagination, Select, Stack, Text, TextInput, Tooltip, SegmentedControl } from '@mantine/core'
import { DatePickerInput, type DatesRangeValue } from '@mantine/dates'
import { useMediaQuery } from '@mantine/hooks'
import { DataTable, type DataTableSortStatus } from 'mantine-datatable'
import { IconAdjustments, IconDownload, IconFilterOff, IconLayoutColumns, IconSearch, IconX } from '@tabler/icons-react'
import { useI18n } from '../i18n'
import { EmptyState } from './EmptyState'

export type FilterKind = 'text' | 'multi' | 'date'

export type GridColumn<T> = {
  id: string
  title: string
  value: (row: T) => string | number | Date | null | undefined
  render?: (row: T) => ReactNode
  sortable?: boolean
  filter?: FilterKind
  width?: number | string
  hidden?: boolean
  align?: 'left' | 'right' | 'center'
  mobile?: 'title' | 'hide' | 'action'
}

type FilterState = Record<string, string | string[] | DatesRangeValue>

type Props<T> = {
  records: T[]
  columns: GridColumn<T>[]
  getId: (row: T) => string
  storageKey: string
  exportName?: string
  loading?: boolean
  pageSize?: number
  toolbarExtra?: ReactNode
  emptyTitle?: string
  emptyText?: string
  emptyAction?: ReactNode
  onRowClick?: (row: T) => void
}

const SIZES = ['10', '25', '50', '100']

/** Returns a string from an unknown value for display in the data grid. */
function text(v: unknown): string {
  if (v === null || v === undefined) return ''
  if (v instanceof Date) return v.toISOString()
  return String(v)
}

/** Reads a JSON value from local storage by key, returning the fallback when missing or unreadable. */
function load<T>(key: string, fallback: T): T {
  try { const raw = localStorage.getItem(key); return raw ? (JSON.parse(raw) as T) : fallback } catch { return fallback }
}
/** Writes a JSON-serialized value to local storage under the given key. */
function save(key: string, value: unknown) {
  try { localStorage.setItem(key, JSON.stringify(value)) } catch {  }
}

/** Builds CSV text from rows of string cells. */
function csv(rows: string[][]) {
  const safe = (s: string) => (/^[=+\-@\t\r]/.test(s) ? `'${s}` : s)
  return '﻿' + rows.map((r) => r.map((c) => `"${safe(c).replace(/"/g, '""')}"`).join(',')).join('\r\n')
}

/** Renders a generic data grid with search, column filters, sorting, paging, column visibility, CSV export and a mobile card layout. */
export function DataGrid<T>({ records, columns, getId, storageKey, exportName = 'export', loading, pageSize = 25, toolbarExtra, emptyTitle, emptyText, emptyAction, onRowClick }: Props<T>) {
  const { t, locale } = useI18n()
  const mobile = useMediaQuery('(max-width: 48em)')

  const prefs = load<{ hidden?: string[]; dense?: boolean; size?: string }>(`grid:${storageKey}`, {})
  const [hiddenCols, setHiddenCols] = useState<string[]>(prefs.hidden ?? columns.filter((c) => c.hidden).map((c) => c.id))
  const [dense, setDense] = useState(prefs.dense ?? false)
  const [size, setSize] = useState(prefs.size ?? String(pageSize))
  useEffect(() => { save(`grid:${storageKey}`, { hidden: hiddenCols, dense, size }) }, [storageKey, hiddenCols, dense, size])

  const [search, setSearch] = useState('')
  const [filters, setFilters] = useState<FilterState>({})
  const [page, setPage] = useState(1)
  const [sort, setSort] = useState<DataTableSortStatus<T>>({ columnAccessor: '', direction: 'asc' })

  const colMap = useMemo(() => Object.fromEntries(columns.map((c) => [c.id, c])), [columns])
  const visibleCols = columns.filter((c) => !hiddenCols.includes(c.id))

  const options = useMemo(() => {
    const out: Record<string, string[]> = {}
    for (const c of columns) {
      if (c.filter === 'multi') out[c.id] = Array.from(new Set(records.map((r) => text(c.value(r))).filter(Boolean))).sort((a, b) => a.localeCompare(b, locale))
    }
    return out
  }, [records, columns, locale])

  /** Sets or clears the filter for a column and resets to the first page. */
  function setFilter(id: string, v: FilterState[string] | undefined) {
    setFilters((f) => {
      const next = { ...f }
      if (v === undefined) delete next[id]; else next[id] = v
      return next
    })
    setPage(1)
  }

  /** Tells whether a column currently has an active filter. */
  function active(c: GridColumn<T>): boolean {
    const f = filters[c.id]
    if (c.filter === 'text') return typeof f === 'string' && f.trim() !== ''
    if (c.filter === 'multi') return Array.isArray(f) && (f as string[]).length > 0
    if (c.filter === 'date') return Array.isArray(f) && Boolean((f as DatesRangeValue)[0] || (f as DatesRangeValue)[1])
    return false
  }

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    return records.filter((r) => {
      for (const c of columns) {
        const f = filters[c.id]
        if (f === undefined) continue
        const v = c.value(r)
        if (c.filter === 'text' && typeof f === 'string' && f.trim() && !text(v).toLowerCase().includes(f.trim().toLowerCase())) return false
        if (c.filter === 'multi' && Array.isArray(f) && (f as string[]).length && !(f as string[]).includes(text(v))) return false
        if (c.filter === 'date' && Array.isArray(f)) {
          const [from, to] = f as DatesRangeValue
          const d = v ? new Date(v as string) : null
          if (from && (!d || d < startOfDay(new Date(from)))) return false
          if (to && (!d || d > endOfDay(new Date(to)))) return false
        }
      }
      if (q && !columns.some((c) => text(c.value(r)).toLowerCase().includes(q))) return false
      return true
    })
  }, [records, columns, filters, search])

  const sorted = useMemo(() => {
    const c = colMap[String(sort.columnAccessor)]
    if (!c) return filtered
    const list = [...filtered].sort((a, b) => text(c.value(a)).localeCompare(text(c.value(b)), locale, { numeric: true }))
    return sort.direction === 'desc' ? list.reverse() : list
  }, [filtered, sort, colMap, locale])

  const perPage = Number(size)
  const pages = Math.max(1, Math.ceil(sorted.length / perPage))
  const current = Math.min(page, pages)
  const visible = sorted.slice((current - 1) * perPage, current * perPage)

  const chips: { key: string; label: string }[] = []
  for (const c of columns) {
    if (!active(c)) continue
    const f = filters[c.id]
    if (c.filter === 'text') chips.push({ key: c.id, label: t('filter.contains', { column: c.title, value: String(f).trim() }) })
    if (c.filter === 'multi') chips.push({ key: c.id, label: t('filter.selected', { column: c.title, n: (f as string[]).length }) })
    if (c.filter === 'date') {
      const [a, b] = f as DatesRangeValue
      const fmt = (d: string | Date | null) => (d ? new Date(d).toLocaleDateString(locale) : '…')
      chips.push({ key: c.id, label: t('filter.range', { column: c.title, from: fmt(a), to: fmt(b) }) })
    }
  }
  const anyFilter = chips.length > 0 || search.trim() !== ''
  /** Clears all column filters and the search text and returns to the first page. */
  function clearAll() { setFilters({}); setSearch(''); setPage(1) }

  /** Exports the currently filtered rows as a CSV file download. */
  function exportCsv() {
    const head = visibleCols.map((c) => c.title)
    const body = sorted.map((r) => visibleCols.map((c) => { const v = c.value(r); return v instanceof Date ? v.toISOString() : text(v) }))
    const blob = new Blob([csv([head, ...body])], { type: 'text/csv;charset=utf-8' })
    const a = document.createElement('a')
    a.href = URL.createObjectURL(blob)
    a.download = `${exportName}.csv`
    a.rel = 'noopener'
    document.body.append(a)
    a.click()
    a.remove()
    URL.revokeObjectURL(a.href)
  }

  /** Builds the filter input control for a column according to its filter type. */
  function filterNode(c: GridColumn<T>): ReactNode {
    const f = filters[c.id]
    if (c.filter === 'text') {
      return (
        <TextInput
          label={t('filter.containsLabel')}
          placeholder={t('filter.typeToFilter')}
          leftSection={<IconSearch size={14} />}
          value={typeof f === 'string' ? f : ''}
          onChange={(e) => setFilter(c.id, e.currentTarget.value || undefined)}
          rightSection={f ? <ActionIcon size="sm" variant="transparent" aria-label={t('ui.close')} onClick={() => setFilter(c.id, undefined)}><IconX size={14} /></ActionIcon> : null}
        />
      )
    }
    if (c.filter === 'multi') {
      return <MultiSelect label={c.title} placeholder={t('filter.pick')} data={options[c.id] ?? []} value={Array.isArray(f) ? (f as string[]) : []} onChange={(v) => setFilter(c.id, v.length ? v : undefined)} searchable clearable comboboxProps={{ withinPortal: false }} />
    }
    if (c.filter === 'date') {
      return (
        <DatePickerInput
          type="range"
          label={c.title}
          description={t('filter.dateHint')}
          placeholder={t('filter.pickDates')}
          value={Array.isArray(f) ? (f as DatesRangeValue) : [null, null]}
          onChange={(v) => setFilter(c.id, v[0] || v[1] ? v : undefined)}
          clearable
          locale={locale === 'tr-TR' ? 'tr' : 'en'}
        />
      )
    }
    return undefined
  }

  const emptyNode = anyFilter || records.length > 0
    ? <EmptyState title={t('grid.noMatchTitle')} text={t('grid.noMatchText')} action={<Button variant="light" size="xs" onClick={clearAll}>{t('grid.clearFilters')}</Button>} />
    : <EmptyState title={emptyTitle ?? t('grid.noDataTitle')} text={emptyText} action={emptyAction} />

  return (
    <Card padding={0} className="grid-card" data-tour="grid">
      <Stack gap="xs" p="sm" className="grid-toolbar">
        <Group justify="space-between" gap="xs" wrap="wrap">
          <Group gap="xs" wrap="wrap" style={{ flex: 1, minWidth: 0 }}>
            <TextInput
              wrapperProps={{ 'data-tour': 'grid-search' }}
              placeholder={t('grid.search')}
              aria-label={t('grid.searchAria')}
              leftSection={<IconSearch size={16} />}
              value={search}
              onChange={(e) => { setSearch(e.currentTarget.value); setPage(1) }}
              style={{ flex: '1 1 220px', maxWidth: 360 }}
            />
            {anyFilter && <Button variant="subtle" size="xs" leftSection={<IconFilterOff size={14} />} onClick={clearAll}>{t('grid.clearFilters')}</Button>}
            {toolbarExtra}
          </Group>
          <Group gap={4} wrap="nowrap">
            <Menu position="bottom-end" closeOnItemClick={false} withinPortal>
              <Menu.Target>
                <Tooltip label={t('grid.columns')}><ActionIcon variant="default" size="lg" aria-label={t('grid.columns')}><IconLayoutColumns size={18} /></ActionIcon></Tooltip>
              </Menu.Target>
              <Menu.Dropdown miw={200}>
                <Menu.Label>{t('grid.columns')}</Menu.Label>
                {columns.map((c) => (
                  <Menu.Item key={c.id} component="div" py={4}>
                    <Checkbox label={c.title} size="xs" checked={!hiddenCols.includes(c.id)} onChange={(e) => {
                      const on = e.currentTarget.checked
                      setHiddenCols((h) => (on ? h.filter((x) => x !== c.id) : [...h, c.id]))
                    }} />
                  </Menu.Item>
                ))}
              </Menu.Dropdown>
            </Menu>
            <Menu position="bottom-end" withinPortal>
              <Menu.Target>
                <Tooltip label={t('grid.density')}><ActionIcon variant="default" size="lg" aria-label={t('grid.density')}><IconAdjustments size={18} /></ActionIcon></Tooltip>
              </Menu.Target>
              <Menu.Dropdown>
                <Menu.Label>{t('grid.density')}</Menu.Label>
                <div style={{ padding: '4px 8px 8px' }}>
                  <SegmentedControl size="xs" value={dense ? 'dense' : 'roomy'} onChange={(v) => setDense(v === 'dense')} data={[{ value: 'dense', label: t('grid.compact') }, { value: 'roomy', label: t('grid.comfortable') }]} />
                </div>
              </Menu.Dropdown>
            </Menu>
            <Tooltip label={t('grid.export')}><ActionIcon variant="default" size="lg" aria-label={t('grid.export')} onClick={exportCsv} disabled={sorted.length === 0}><IconDownload size={18} /></ActionIcon></Tooltip>
          </Group>
        </Group>
        {chips.length > 0 && (
          <Group gap={6} role="list" aria-label={t('grid.activeFilters')}>
            {chips.map((c) => (
              <Badge key={c.key} variant="light" size="lg" tt="none" fw={500} role="listitem"
                rightSection={<ActionIcon size="xs" variant="transparent" color="cobalt" aria-label={t('grid.removeFilter', { label: c.label })} onClick={() => setFilter(c.key, undefined)}><IconX size={12} /></ActionIcon>}>
                {c.label}
              </Badge>
            ))}
          </Group>
        )}
      </Stack>

      {mobile ? (
        <MobileList rows={visible} columns={visibleCols} getId={getId} empty={emptyNode} onRowClick={onRowClick} />
      ) : (
        <DataTable<T>
          minHeight={sorted.length === 0 ? 220 : undefined}
          fetching={loading}
          records={visible}
          idAccessor={(r) => getId(r)}
          verticalSpacing={dense ? 4 : 'sm'}
          highlightOnHover
          onRowClick={onRowClick ? ({ record }) => onRowClick(record) : undefined}
          sortStatus={sort}
          onSortStatusChange={setSort}
          noRecordsText=""
          noRecordsIcon={emptyNode}
          columns={visibleCols.map((c) => ({
            accessor: c.id,
            title: c.title,
            sortable: c.sortable,
            width: c.width,
            textAlign: c.align,
            render: (r: T) => (c.render ? c.render(r) : text(c.value(r))),
            filter: c.filter ? filterNode(c) : undefined,
            filtering: c.filter ? active(c) : false,
          }))}
        />
      )}

      <Group justify="space-between" p="sm" gap="sm" wrap="wrap" style={{ borderTop: '1px solid var(--mantine-color-default-border)' }}>
        <Text size="sm" c="dimmed">{t('grid.counter', { shown: sorted.length, total: records.length })}</Text>
        <Group gap="sm" wrap="wrap">
          <Select aria-label={t('grid.rowsPerPage')} w={96} data={SIZES.map((s) => ({ value: s, label: s }))} value={size} onChange={(v) => { if (v) { setSize(v); setPage(1) } }} allowDeselect={false} />
          <Pagination total={pages} value={current} onChange={setPage} size="sm" siblings={mobile ? 0 : 1} />
        </Group>
      </Group>
    </Card>
  )
}

/** Returns a copy of the date set to the start of its day. */
function startOfDay(d: Date) { const x = new Date(d); x.setHours(0, 0, 0, 0); return x }
/** Returns a copy of the date set to the last millisecond of its day. */
function endOfDay(d: Date) { const x = new Date(d); x.setHours(23, 59, 59, 999); return x }

/** Renders rows as a mobile card list with the title column as heading and the other columns as label-value pairs. */
function MobileList<T>({ rows, columns, getId, empty, onRowClick }: { rows: T[]; columns: GridColumn<T>[]; getId: (r: T) => string; empty: ReactNode; onRowClick?: (r: T) => void }) {
  if (rows.length === 0) return <>{empty}</>
  const usable = columns.filter((c) => c.mobile !== 'hide')
  const actionCol = usable.find((c) => c.mobile === 'action' || c.id === 'actions')
  const body = usable.filter((c) => c !== actionCol)
  const titleCol = body.find((c) => c.mobile === 'title') ?? body[0]
  const rest = body.filter((c) => c !== titleCol)
  return (
    <Stack gap={0}>
      {rows.map((r) => (
        <div key={getId(r)} onClick={onRowClick ? () => onRowClick(r) : undefined} style={{ padding: 'var(--mantine-spacing-sm) var(--mantine-spacing-md)', borderBottom: '1px solid var(--mantine-color-default-border)', cursor: onRowClick ? 'pointer' : undefined }}>
          <Group justify="space-between" wrap="nowrap" align="flex-start" gap="xs">
            <Text fw={600} size="sm" style={{ minWidth: 0, overflowWrap: 'anywhere' }}>{titleCol.render ? titleCol.render(r) : text(titleCol.value(r))}</Text>
            {actionCol?.render && <div style={{ flex: 'none' }} onClick={(e) => e.stopPropagation()}>{actionCol.render(r)}</div>}
          </Group>
          <Stack gap={2} mt={4}>
            {rest.map((c) => (
              <Group key={c.id} gap={8} wrap="nowrap" align="flex-start">
                <Text size="xs" c="dimmed" w={110} style={{ flex: 'none' }}>{c.title}</Text>
                <Text size="sm" style={{ minWidth: 0, overflowWrap: 'anywhere' }}>{c.render ? c.render(r) : text(c.value(r))}</Text>
              </Group>
            ))}
          </Stack>
        </div>
      ))}
    </Stack>
  )
}
