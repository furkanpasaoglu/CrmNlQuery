import { modals } from '@mantine/modals'

/** Shows a confirmation dialog for irreversible actions instead of the browser's confirm box. */
export function confirmAction(o: { title: string; message: string; confirmLabel: string; cancelLabel: string; danger?: boolean; onConfirm: () => void | Promise<void> }) {
  modals.openConfirmModal({
    title: o.title,
    children: o.message,
    labels: { confirm: o.confirmLabel, cancel: o.cancelLabel },
    confirmProps: { color: o.danger ? 'red' : 'cobalt' },
    onConfirm: () => { void o.onConfirm() },
  })
}
