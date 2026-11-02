export type DocWarning = 'long' | 'nationalId' | 'iban' | 'phone' | 'email'

export const LONG_BODY = 3000
export const MAX_FILE_BYTES = 200 * 1024
export const FILE_EXTENSIONS = ['.txt', '.md', '.markdown']

const NATIONAL_ID = /(?<!\d)[1-9]\d{10}(?!\d)/
const IBAN = /\bTR\d{2}(?:\s?\d{4}){5}\s?\d{2}\b/i
const PHONE = /(?<![\d])(?:\+90[\s.-]?|0)?\(?5\d{2}\)?[\s.-]?\d{3}[\s.-]?\d{2}[\s.-]?\d{2}(?!\d)/
const EMAIL = /[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+/

/** Checks a pasted document body and returns warnings before saving. */
export function checkDocument(body: string): DocWarning[] {
  const found: DocWarning[] = []
  if (body.length > LONG_BODY) found.push('long')
  if (NATIONAL_ID.test(body)) found.push('nationalId')
  if (IBAN.test(body)) found.push('iban')
  if (PHONE.test(body)) found.push('phone')
  if (EMAIL.test(body)) found.push('email')
  return found
}

export type FileCheck = { ok: true } | { ok: false; reason: 'type' | 'empty' | 'tooBig' }

/** Checks a file's name and size before reading it, allowing only plain text files (.txt, .md). */
export function checkFile(name: string, size: number): FileCheck {
  const lower = name.toLowerCase()
  if (!FILE_EXTENSIONS.some((e) => lower.endsWith(e))) return { ok: false, reason: 'type' }
  if (size === 0) return { ok: false, reason: 'empty' }
  if (size > MAX_FILE_BYTES) return { ok: false, reason: 'tooBig' }
  return { ok: true }
}

/** Derives a suggested document title from a file name, for example kobi-tanimi.md becomes kobi-tanimi. */
export function titleFromFileName(name: string): string {
  const dot = name.lastIndexOf('.')
  return (dot > 0 ? name.slice(0, dot) : name).replace(/[_]+/g, ' ').trim()
}

/** Cleans text by removing the leading BOM and normalizing line endings. */
export function cleanText(text: string): string {
  return text.replace(/^﻿/, '').replace(/\r\n?/g, '\n').trim()
}
