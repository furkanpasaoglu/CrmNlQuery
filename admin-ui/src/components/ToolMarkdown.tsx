import { Fragment, type ReactNode } from 'react'
import { Box, Code, List, ScrollArea, Table, Text, Title } from '@mantine/core'

/** Renders inline Markdown parts into React elements without running HTML. */
function inline(text: string): ReactNode[] {
  return text.split(/(\*\*[^*]+\*\*|`[^`]+`)/).filter(Boolean).map((part, i) => {
    if (part.length > 4 && part.startsWith('**') && part.endsWith('**')) return <strong key={i}>{part.slice(2, -2)}</strong>
    if (part.length > 2 && part.startsWith('`') && part.endsWith('`')) return <Code key={i}>{part.slice(1, -1)}</Code>
    return <Fragment key={i}>{part}</Fragment>
  })
}

/** Renders table cells in the small Markdown renderer. */
function cells(line: string): string[] {
  return line.trim().replace(/^\|/, '').replace(/\|$/, '').split(/(?<!\\)\|/).map((c) => c.trim().replace(/\\\|/g, '|'))
}

/** Renders Markdown text safely: headings, paragraphs, lists, tables and code, without executing HTML. */
export function ToolMarkdown({ source }: { source: string }) {
  const lines = String(source).split(/\r?\n/)
  const out: ReactNode[] = []
  let i = 0
  let key = 0
  while (i < lines.length) {
    const line = lines[i]
    if (/^```/.test(line)) {
      const code: string[] = []
      i++
      while (i < lines.length && !/^```/.test(lines[i])) { code.push(lines[i]); i++ }
      i++
      out.push(<Code key={key++} block my="xs" style={{ overflowX: 'auto' }}>{code.join('\n')}</Code>)
      continue
    }
    if (/^\s*\|/.test(line)) {
      const rows: string[] = []
      while (i < lines.length && /^\s*\|/.test(lines[i])) { rows.push(lines[i]); i++ }
      const head = cells(rows[0])
      const body = rows.slice(2).map(cells)
      out.push(
        <ScrollArea key={key++} my="xs" type="auto">
          <Table withTableBorder withColumnBorders verticalSpacing={4} fz="sm">
            <Table.Thead><Table.Tr>{head.map((c, j) => <Table.Th key={j}>{inline(c)}</Table.Th>)}</Table.Tr></Table.Thead>
            <Table.Tbody>{body.map((r, a) => <Table.Tr key={a}>{r.map((c, j) => <Table.Td key={j}>{inline(c)}</Table.Td>)}</Table.Tr>)}</Table.Tbody>
          </Table>
        </ScrollArea>,
      )
      continue
    }
    const h = /^(#{1,3})\s+(.*)$/.exec(line)
    if (h) {
      const order = Math.min(h[1].length + 1, 4) as 2 | 3 | 4
      out.push(<Title key={key++} order={order} mt="md" mb={4}>{inline(h[2])}</Title>)
      i++
      continue
    }
    if (/^>\s?/.test(line)) {
      out.push(
        <Box key={key++} my="xs" pl="sm" style={{ borderLeft: '3px solid var(--mantine-color-gray-4)' }}>
          <Text size="sm" c="dimmed">{inline(line.replace(/^>\s?/, ''))}</Text>
        </Box>,
      )
      i++
      continue
    }
    let li = /^(\s*)-\s+(.*)$/.exec(line)
    if (li) {
      const items: ReactNode[] = []
      while (i < lines.length && (li = /^(\s*)-\s+(.*)$/.exec(lines[i]))) {
        items.push(<List.Item key={i} ml={Math.min(li[1].length, 8) * 4}>{inline(li[2])}</List.Item>)
        i++
      }
      out.push(<List key={key++} size="sm" my="xs" spacing={2}>{items}</List>)
      continue
    }
    if (line.trim() === '') { i++; continue }
    out.push(<Text key={key++} size="sm" my={6}>{inline(line)}</Text>)
    i++
  }
  return <Box style={{ overflowWrap: 'anywhere' }}>{out}</Box>
}
