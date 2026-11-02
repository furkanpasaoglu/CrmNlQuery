import { createContext, useCallback, useContext, useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { Button, Group, Paper, Portal, Text } from '@mantine/core'
import { IconX } from '@tabler/icons-react'
import { useI18n } from '../i18n'
import { TOURS, type TourStep } from './tours'

type ActiveTour = { id: string; steps: TourStep[]; index: number }
type Ctx = { start: (id: string) => void; has: (id: string) => boolean; seen: (id: string) => boolean; active: boolean }

const TourContext = createContext<Ctx | null>(null)
/** React hook that exposes the guided tour state to components. */
export function useTour() {
  const c = useContext(TourContext)
  if (!c) throw new Error('TourProvider yok')
  return c
}

const SEEN_KEY = 'admin-tour-seen'
const PAD = 8
const MOBILE_MAX = 768
const Z = 10000

/** Reads from local storage whether the user has already seen the tour. */
function readSeen(): Record<string, boolean> {
  try { return JSON.parse(localStorage.getItem(SEEN_KEY) || '{}') as Record<string, boolean> } catch { return {} }
}
/** Records in local storage that the user has seen the tour. */
function writeSeen(map: Record<string, boolean>) {
  try { localStorage.setItem(SEEN_KEY, JSON.stringify(map)) } catch {  }
}

/** Finds the first visible element marked with the given data-tour name. */
function findTarget(target?: string): HTMLElement | null {
  if (!target) return null
  for (const el of Array.from(document.querySelectorAll<HTMLElement>(`[data-tour="${target}"]`))) {
    const r = el.getBoundingClientRect()
    if (r.width > 0 && r.height > 0) return el
  }
  return null
}

/** Waits until the tour target element appears on screen. */
async function waitTarget(target: string | undefined, ms: number, cancelled: () => boolean): Promise<HTMLElement | null> {
  const end = Date.now() + ms
  for (;;) {
    const el = findTarget(target)
    if (el || ms <= 0 || Date.now() >= end || cancelled()) return el
    await sleep(100)
  }
}

/** Returns whether the viewport is mobile-sized. */
const isMobile = () => window.innerWidth < MOBILE_MAX
/** Returns whether the user prefers reduced motion. */
const reducedMotion = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false
/** Returns a promise that resolves after a delay. */
const sleep = (ms: number) => new Promise<void>((r) => setTimeout(r, ms))
/** Limits a number to a minimum and maximum. */
const clamp = (v: number, lo: number, hi: number) => Math.min(Math.max(v, lo), Math.max(lo, hi))

/** Positions the tour bubble below, above, beside or inside the target, whichever fits first. */
function placeBubble(rect: DOMRect | null, size: { w: number; h: number }, vw: number, vh: number): React.CSSProperties {
  const m = 12
  const w = Math.min(380, vw - m * 2)
  if (isMobile() || !rect) {
    const upper = rect ? rect.top + rect.height / 2 < vh / 2 : false
    return rect
      ? upper ? { left: m, right: m, bottom: m, width: 'auto' } : { left: m, right: m, top: m, width: 'auto' }
      : { left: clamp((vw - w) / 2, m, vw), top: clamp((vh - size.h) / 2, m, vh), width: w }
  }
  const gap = 16
  const tall = rect.height > vh * 0.6
  const cx = rect.left + rect.width / 2 - w / 2
  const cy = rect.top + rect.height / 2 - size.h / 2
  if (!tall && rect.bottom + gap + size.h <= vh - m) return { left: clamp(cx, m, vw - w - m), top: rect.bottom + gap, width: w }
  if (!tall && rect.top - gap - size.h >= m) return { left: clamp(cx, m, vw - w - m), top: rect.top - gap - size.h, width: w }
  if (rect.right + gap + w <= vw - m) return { left: rect.right + gap, top: clamp(cy, m, vh - size.h - m), width: w }
  if (rect.left - gap - w >= m) return { left: rect.left - gap - w, top: clamp(cy, m, vh - size.h - m), width: w }
  return { left: clamp(cx, m, vw - w - m), bottom: m, width: w }
}

/** React provider that runs the guided tour overlay and its state. */
export function TourProvider({ children, autoStart }: { children: ReactNode; autoStart?: string }) {
  const { t } = useI18n()
  const [tour, setTour] = useState<ActiveTour | null>(null)
  const [rect, setRect] = useState<DOMRect | null>(null)
  const [size, setSize] = useState({ w: 380, h: 220 })
  const [seenMap, setSeenMap] = useState<Record<string, boolean>>(readSeen)
  const bubbleRef = useRef<HTMLDivElement | null>(null)
  const nextRef = useRef<HTMLButtonElement | null>(null)
  const direction = useRef<1 | -1>(1)
  const autoDone = useRef(false)

  const markSeen = useCallback((id: string) => {
    setSeenMap((m) => { const n = { ...m, [id]: true }; writeSeen(n); return n })
  }, [])

  const finish = useCallback(() => {
    setTour((cur) => { if (cur) markSeen(cur.id); return null })
    setRect(null)
    if (isMobile()) window.dispatchEvent(new CustomEvent('tour:nav', { detail: { open: false } }))
  }, [markSeen])

  const start = useCallback((id: string) => {
    const all = TOURS[id]
    if (!all) return
    let afterClick = false
    const steps = all.filter((s) => {
      const keep = afterClick || !s.target || !!s.nav || !!findTarget(s.target)
      if (keep && s.action === 'click') afterClick = true
      return keep
    })
    if (steps.length === 0) return
    direction.current = 1
    setTour({ id, steps, index: 0 })
  }, [])

  const go = useCallback((delta: 1 | -1) => {
    direction.current = delta
    setTour((cur) => {
      if (!cur) return cur
      const next = cur.index + delta
      if (next < 0) return cur
      if (next >= cur.steps.length) return cur
      return { ...cur, index: next }
    })
  }, [])

  const step = tour ? tour.steps[tour.index] : null

  useEffect(() => {
    if (!tour || !step) return
    let cancelled = false
    let frame = 0
    let observed: HTMLElement | null = null
    let observer: ResizeObserver | null = null
    let removeClick = () => {  }

    const measure = () => { setRect(observed ? observed.getBoundingClientRect() : null) }
    const onMove = () => { cancelAnimationFrame(frame); frame = requestAnimationFrame(measure) }

    const run = async () => {
      if (isMobile()) window.dispatchEvent(new CustomEvent('tour:nav', { detail: { open: !!step.nav } }))
      if (step.nav && isMobile()) await sleep(320)
      if (cancelled) return
      const afterClick = direction.current === 1 && tour.index > 0 && tour.steps[tour.index - 1].action === 'click'
      const el = await waitTarget(step.target, afterClick ? 2500 : 0, () => cancelled)
      if (afterClick && el) await sleep(380)
      if (cancelled) return
      if (step.target && !el) {
        const next = tour.index + direction.current
        if (next >= 0 && next < tour.steps.length) setTour({ ...tour, index: next })
        else if (direction.current === 1) finish()
        return
      }
      observed = el
      if (step.action === 'click' && el) {
        const onTargetClick = () => { setTimeout(() => go(1), 250) }
        el.addEventListener('click', onTargetClick, true)
        removeClick = () => el.removeEventListener('click', onTargetClick, true)
      }
      el?.scrollIntoView({ block: 'center', inline: 'nearest', behavior: reducedMotion() ? 'auto' : 'auto' })
      measure()
      if (el && 'ResizeObserver' in window) { observer = new ResizeObserver(onMove); observer.observe(el) }
    }
    void run()
    window.addEventListener('resize', onMove)
    window.addEventListener('scroll', onMove, true)
    return () => {
      cancelled = true
      cancelAnimationFrame(frame)
      removeClick()
      observer?.disconnect()
      window.removeEventListener('resize', onMove)
      window.removeEventListener('scroll', onMove, true)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tour?.id, tour?.index])

  useLayoutEffect(() => {
    if (!tour || !bubbleRef.current) return
    const r = bubbleRef.current.getBoundingClientRect()
    setSize((s) => (Math.abs(s.h - r.height) > 1 || Math.abs(s.w - r.width) > 1 ? { w: r.width, h: r.height } : s))
  }, [tour?.index, rect, t])
  useEffect(() => { if (tour) nextRef.current?.focus({ preventScroll: true }) }, [tour?.id, tour?.index]) // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (!tour) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') { e.preventDefault(); finish() }
      else if (e.key === 'ArrowRight') { e.preventDefault(); if (tour.index < tour.steps.length - 1) go(1); else finish() }
      else if (e.key === 'ArrowLeft') { e.preventDefault(); go(-1) }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [tour, go, finish])

  useEffect(() => {
    if (!autoStart || autoDone.current) return
    autoDone.current = true
    if (readSeen()[autoStart]) return
    const timer = setTimeout(() => start(autoStart), 900)
    return () => { clearTimeout(timer); autoDone.current = false }
  }, [autoStart, start])

  const value = useMemo<Ctx>(() => ({ start, has: (id) => id in TOURS, seen: (id) => !!seenMap[id], active: tour !== null }), [start, seenMap, tour])

  const vw = typeof window === 'undefined' ? 1200 : window.innerWidth
  const vh = typeof window === 'undefined' ? 800 : window.innerHeight
  const hole = rect
    ? (() => {
        const top = Math.max(rect.top - PAD, 4), left = Math.max(rect.left - PAD, 4)
        const bottom = Math.min(rect.bottom + PAD, vh - 4), right = Math.min(rect.right + PAD, vw - 4)
        return { top, left, width: Math.max(0, right - left), height: Math.max(0, bottom - top) }
      })()
    : null
  const pos = tour ? placeBubble(rect, size, vw, vh) : {}
  const isLast = tour ? tour.index === tour.steps.length - 1 : false
  const key = (part: string) => (tour && step ? `tour.${tour.id}.${step.id}.${part}` : '')
  const clickStep = step?.action === 'click' && hole !== null
  const motion = reducedMotion() ? 'none' : 'all 200ms ease'

  return (
    <TourContext.Provider value={value}>
      {children}
      {tour && step && (
        <Portal>
          <div style={{ position: 'fixed', inset: 0, zIndex: Z, pointerEvents: clickStep ? 'none' : 'auto' }} onClick={(e) => e.stopPropagation()}>
            {clickStep && hole && (
              <>
                <div style={{ position: 'fixed', top: 0, left: 0, right: 0, height: hole.top, pointerEvents: 'auto' }} />
                <div style={{ position: 'fixed', top: hole.top + hole.height, left: 0, right: 0, bottom: 0, pointerEvents: 'auto' }} />
                <div style={{ position: 'fixed', top: hole.top, left: 0, width: hole.left, height: hole.height, pointerEvents: 'auto' }} />
                <div style={{ position: 'fixed', top: hole.top, left: hole.left + hole.width, right: 0, height: hole.height, pointerEvents: 'auto' }} />
              </>
            )}
            {hole
              ? <div style={{ position: 'fixed', ...hole, borderRadius: 10, boxShadow: '0 0 0 9999px rgba(8, 12, 28, 0.66)', outline: '2px solid var(--mantine-color-cobalt-4)', transition: motion, pointerEvents: 'none' }} />
              : <div style={{ position: 'fixed', inset: 0, background: 'rgba(8, 12, 28, 0.66)' }} />}
            <Paper
              ref={bubbleRef}
              role="dialog"
              aria-modal="true"
              aria-labelledby="tour-title"
              aria-describedby="tour-text"
              withBorder
              shadow="xl"
              radius="md"
              p="md"
              style={{ position: 'fixed', zIndex: Z + 1, transition: motion, pointerEvents: 'auto', ...pos }}
            >
              <Group justify="space-between" wrap="nowrap" mb={4}>
                <Text size="xs" c="dimmed">{t('tour.ui.step', { n: tour.index + 1, total: tour.steps.length })}</Text>
                <Button variant="subtle" color="gray" size="compact-xs" rightSection={<IconX size={12} />} onClick={finish}>{t('tour.ui.skip')}</Button>
              </Group>
              <Text id="tour-title" fw={600} size="md">{t(key('title'))}</Text>
              <Text id="tour-text" size="sm" mt={4} style={{ whiteSpace: 'pre-wrap' }}>{t(key('text'))}</Text>
              {step.action === 'click' && <Text size="xs" fw={600} c="cobalt" mt={8}>{t('tour.ui.clickHint')}</Text>}
              <Group justify="space-between" mt="md" wrap="nowrap">
                <Group gap={5} aria-hidden>
                  {tour.steps.map((s, i) => (
                    <span key={s.id} style={{ width: i === tour.index ? 18 : 7, height: 7, borderRadius: 4, background: i === tour.index ? 'var(--mantine-color-cobalt-5)' : 'var(--mantine-color-default-border)', transition: motion }} />
                  ))}
                </Group>
                <Group gap="xs" wrap="nowrap">
                  {tour.index > 0 && <Button variant="default" size="xs" onClick={() => go(-1)}>{t('tour.ui.back')}</Button>}
                  {step.action === 'click'
                    ? <Button ref={nextRef} size="xs" onClick={() => findTarget(step.target)?.click()}>{t('tour.ui.showMe')}</Button>
                    : <Button ref={nextRef} size="xs" onClick={() => (isLast ? finish() : go(1))}>{isLast ? t('tour.ui.done') : t('tour.ui.next')}</Button>}
                </Group>
              </Group>
            </Paper>
          </div>
        </Portal>
      )}
    </TourContext.Provider>
  )
}
