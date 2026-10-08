import type { FieldChange } from '@/lib/api'

export const FIELD_INFO: Record<FieldChange['field'], { label: string; unit: string }> = {
  AirflowLps: { label: 'Airflow', unit: 'l/s' },
  PowerW: { label: 'Power', unit: 'W' },
  ConnectionSizeMm: { label: 'Connection', unit: 'mm' },
  WeightKg: { label: 'Weight', unit: 'kg' },
}

const numberFormat = new Intl.NumberFormat('en', { maximumFractionDigits: 2 })

export function formatNumber(value: number | null | undefined, unit?: string): string {
  if (value === null || value === undefined) return '—'
  return unit ? `${numberFormat.format(value)} ${unit}` : numberFormat.format(value)
}

const relativeTime = new Intl.RelativeTimeFormat('en', { numeric: 'auto' })
const dateTime = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })

/** "3 minutes ago", "yesterday", "2 weeks ago". */
export function formatRelative(iso: string, now = Date.now()): string {
  const seconds = Math.round((Date.parse(iso) - now) / 1000)
  const units: [Intl.RelativeTimeFormatUnit, number][] = [
    ['year', 31_536_000],
    ['month', 2_592_000],
    ['week', 604_800],
    ['day', 86_400],
    ['hour', 3_600],
    ['minute', 60],
  ]
  for (const [unit, size] of units) {
    if (Math.abs(seconds) >= size) return relativeTime.format(Math.round(seconds / size), unit)
  }
  return 'just now'
}

/** "8 Oct, 21:15" in the viewer's time zone. */
export function formatDateTime(iso: string): string {
  return dateTime.format(new Date(iso))
}

/** Share of devices that match the catalog, as a whole percentage. */
export function percentOk(run: { ok: number; total: number }): number {
  return run.total === 0 ? 0 : Math.round((run.ok / run.total) * 100)
}

export function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  URL.revokeObjectURL(url)
}
