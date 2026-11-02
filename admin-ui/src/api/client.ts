export class ApiError extends Error {
  status: number
  code?: string
  hasMessage: boolean
  constructor(status: number, message: string, code?: string, hasMessage = false) {
    super(message)
    this.status = status
    this.code = code
    this.hasMessage = hasMessage
  }
}

const base = ''
const SESSION_KEY = 'nlq_session'

/** Returns the Authorization header for the session token kept in this tab, or nothing when there is none (the service then relies on the Windows sign-in). */
function authHeader(): Record<string, string> {
  try {
    const raw = sessionStorage.getItem(SESSION_KEY)
    const session = raw ? (JSON.parse(raw) as { token?: string; expiresAt?: number }) : null
    if (session?.token && session.expiresAt && session.expiresAt > Date.now()) return { Authorization: 'Bearer ' + session.token }
  } catch {  }
  return {}
}

/** Exchanges a hand-over code for a session token and keeps the token for this tab. */
async function redeemCode(code: string): Promise<void> {
  try {
    const res = await fetch('/api/auth/redeem', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ code }) })
    if (!res.ok) return
    const data = (await res.json()) as { token: string; expiresAt: string }
    sessionStorage.setItem(SESSION_KEY, JSON.stringify({ token: data.token, expiresAt: Date.parse(data.expiresAt) }))
  } catch {  }
}

/** When this page was opened by the chat widget, asks it for a hand-over code and accepts the answer only from the page that opened this one and only from an allowed CRM origin. */
export async function takeHandoffFromOpener(): Promise<void> {
  if (!window.opener || window.opener === window || Object.keys(authHeader()).length > 0) return
  let origins: string[] = []
  try {
    const res = await fetch('/api/auth/origins', { cache: 'no-store' })
    origins = ((await res.json()) as { origins?: string[] }).origins ?? []
  } catch {  }
  if (origins.length === 0) return
  await new Promise<void>((resolve) => {
    const timer = setTimeout(() => { window.removeEventListener('message', onMessage); resolve() }, 4000)
    function onMessage(e: MessageEvent) {
      const data = e.data as { type?: string; code?: unknown } | null
      if (e.source !== window.opener || !origins.includes(e.origin) || data?.type !== 'nlq-code' || typeof data.code !== 'string') return
      clearTimeout(timer)
      window.removeEventListener('message', onMessage)
      void redeemCode(data.code).finally(resolve)
    }
    window.addEventListener('message', onMessage)
    window.opener.postMessage({ type: 'nlq-ready' }, '*')
  })
}

/** Downloads a file from an admin endpoint with the session token and saves it under the given name. */
export async function downloadFile(url: string, fileName: string): Promise<void> {
  const res = await fetch(base + url, { cache: 'no-store', credentials: 'same-origin', headers: authHeader() })
  if (!res.ok) {
    let message = `HTTP ${res.status}`
    try { const d = (await res.json()) as { error?: string; message?: string }; message = d.error || d.message || message } catch {  }
    throw new ApiError(res.status, message)
  }
  const blobUrl = URL.createObjectURL(await res.blob())
  const a = document.createElement('a')
  a.href = blobUrl
  a.download = fileName
  a.rel = 'noopener'
  document.body.append(a)
  a.click()
  a.remove()
  setTimeout(() => URL.revokeObjectURL(blobUrl), 1000)
}

/** Calls an admin API endpoint with the given method and returns the typed response. */
export async function api<T = unknown>(method: string, url: string, body?: unknown): Promise<T> {
  let res: Response
  try {
    res = await fetch(base + url, {
      method,
      cache: 'no-store',
      credentials: 'same-origin',
      headers: body === undefined ? authHeader() : { 'Content-Type': 'application/json', ...authHeader() },
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch (e) {
    throw new ApiError(0, e instanceof Error ? e.message : 'network')
  }
  let data: unknown = null
  try { data = await res.json() } catch {  }
  if (!res.ok) {
    const d = (data ?? {}) as { error?: string; message?: string; code?: string }
    const msg = d.error || d.message
    throw new ApiError(res.status, msg || `HTTP ${res.status}`, d.code, Boolean(msg))
  }
  return data as T
}

/** Calls an admin API endpoint with GET. */
export const get = <T = unknown>(url: string) => api<T>('GET', url)
/** Calls an admin API endpoint with POST and an optional body. */
export const post = <T = unknown>(url: string, body?: unknown) => api<T>('POST', url, body ?? {})
/** Calls an admin API endpoint with PUT and an optional body. */
export const put = <T = unknown>(url: string, body?: unknown) => api<T>('PUT', url, body ?? {})
/** Calls an admin API endpoint with DELETE. */
export const del = <T = unknown>(url: string) => api<T>('DELETE', url)
