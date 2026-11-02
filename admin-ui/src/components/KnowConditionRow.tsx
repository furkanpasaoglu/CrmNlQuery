import { ActionIcon, Group, MultiSelect, Select, Stack, TextInput, Tooltip } from '@mantine/core'
import { IconTrash } from '@tabler/icons-react'
import { useT, useTOr } from '../i18n'

export type GlossaryAttr = { attribute: string; label: string; type: string; options?: { value: number | string; label: string }[] }
export type ConditionDraft = { key: string; attribute: string; operator: string; values: string[] }

export type ValueKind = 'option' | 'boolean' | 'number' | 'text' | 'date' | 'guid' | 'unsupported'

/** Maps a glossary attribute to the kind of value it takes. */
export function kindOf(a: GlossaryAttr): ValueKind {
  switch (a.type) {
    case 'Picklist': case 'State': case 'Status': return 'option'
    case 'Boolean': return a.options && a.options.length ? 'option' : 'boolean'
    case 'Integer': case 'BigInt': case 'Decimal': case 'Double': case 'Money': return 'number'
    case 'String': case 'Memo': return 'text'
    case 'DateTime': return 'date'
    case 'Lookup': case 'Customer': case 'Owner': case 'Uniqueidentifier': return 'guid'
    default: return 'unsupported'
  }
}

const OPS: Record<Exclude<ValueKind, 'unsupported'>, string[]> = {
  option: ['eq', 'ne', 'in', 'not-in', 'null', 'not-null'],
  boolean: ['eq', 'ne', 'in', 'not-in', 'null', 'not-null'],
  number: ['eq', 'ne', 'gt', 'ge', 'lt', 'le', 'in', 'not-in', 'null', 'not-null'],
  text: ['eq', 'ne', 'like', 'in', 'not-in', 'null', 'not-null'],
  date: ['eq', 'gt', 'ge', 'lt', 'le', 'null', 'not-null'],
  guid: ['eq', 'ne', 'in', 'null', 'not-null'],
}

/** Returns the operators allowed for a glossary attribute. */
export const allowedOps = (a?: GlossaryAttr) => {
  if (!a) return []
  const k = kindOf(a)
  return k === 'unsupported' ? [] : OPS[k]
}
/** Tells whether an operator takes a list of values. */
const isMany = (op: string) => op === 'in' || op === 'not-in'
/** Tells whether an operator takes no value. */
const noValue = (op: string) => op === 'null' || op === 'not-null'
/** Builds the translation key for an operator name. */
const opKey = (op: string) => 'glossary.op_' + op.replace(/-/g, '_')

type Props = {
  attrs: GlossaryAttr[]
  row: ConditionDraft
  onChange: (next: ConditionDraft) => void
  onRemove: () => void
}

/** Renders one condition row of a glossary entry with attribute, operator and value controls. */
export function KnowConditionRow({ attrs, row, onChange, onRemove }: Props) {
  const t = useT()
  const tOr = useTOr()
  const attr = attrs.find((a) => a.attribute === row.attribute)
  const ops = allowedOps(attr)
  const kind = attr ? kindOf(attr) : 'unsupported'
  const many = isMany(row.operator)

  const fieldData = attrs.filter((a) => kindOf(a) !== 'unsupported').map((a) => ({ value: a.attribute, label: `${a.label} (${a.attribute})` }))
  const opData = ops.map((o) => ({ value: o, label: tOr(opKey(o), o) }))

  /** Handles choosing an attribute in the condition row. */
  function pickAttr(v: string | null) {
    const a = attrs.find((x) => x.attribute === v)
    onChange({ ...row, attribute: v ?? '', operator: allowedOps(a)[0] ?? '', values: [] })
  }
  /** Handles choosing an operator in the condition row. */
  function pickOp(v: string | null) {
    const op = v ?? ''
    const values = noValue(op) ? [] : isMany(op) ? row.values : row.values.slice(0, 1)
    onChange({ ...row, operator: op, values })
  }

  let valueControl = null
  if (attr && row.operator && !noValue(row.operator)) {
    if (kind === 'option' || kind === 'boolean') {
      const choices = kind === 'boolean'
        ? [{ value: '1', label: t('glossary.yes') }, { value: '0', label: t('glossary.no') }]
        : (attr.options ?? []).map((o) => ({ value: String(o.value), label: o.label }))
      valueControl = many
        ? <MultiSelect aria-label={t('glossary.fValue')} data={choices} value={row.values} onChange={(v) => onChange({ ...row, values: v })} searchable comboboxProps={{ withinPortal: true }} />
        : <Select aria-label={t('glossary.fValue')} data={choices} value={row.values[0] ?? null} onChange={(v) => onChange({ ...row, values: v ? [v] : [] })} allowDeselect={false} searchable comboboxProps={{ withinPortal: true }} />
    } else {
      const type = kind === 'date' && !many ? 'date' : kind === 'number' && !many ? 'number' : 'text'
      valueControl = (
        <TextInput
          aria-label={t('glossary.fValue')}
          type={type}
          description={many ? t('glossary.valuesHelp') : undefined}
          value={row.values.join(many ? ', ' : '')}
          onChange={(e) => {
            const raw = e.currentTarget.value
            onChange({ ...row, values: many ? raw.split(',').map((x) => x.trim()).filter(Boolean) : raw.trim() ? [raw.trim()] : [] })
          }}
        />
      )
    }
  }

  return (
    <Stack gap={6} p="xs" style={{ border: '1px solid var(--mantine-color-default-border)', borderRadius: 'var(--mantine-radius-md)' }}>
      <Group gap="xs" align="flex-start" wrap="nowrap">
        <Select style={{ flex: 1, minWidth: 0 }} aria-label={t('glossary.fField')} placeholder={t('glossary.fChooseField')} data={fieldData} value={row.attribute || null} onChange={pickAttr} searchable allowDeselect={false} comboboxProps={{ withinPortal: true }} />
        <Tooltip label={t('glossary.removeCondition')}>
          <ActionIcon variant="subtle" color="red" size="lg" aria-label={t('glossary.removeCondition')} onClick={onRemove}><IconTrash size={16} /></ActionIcon>
        </Tooltip>
      </Group>
      {attr && (
        <Group gap="xs" align="flex-start" grow preventGrowOverflow={false} wrap="wrap">
          <Select style={{ flex: '1 1 150px' }} aria-label={t('glossary.fOperator')} data={opData} value={row.operator || null} onChange={pickOp} allowDeselect={false} comboboxProps={{ withinPortal: true }} />
          {valueControl && <div style={{ flex: '2 1 200px', minWidth: 0 }}>{valueControl}</div>}
        </Group>
      )}
    </Stack>
  )
}
