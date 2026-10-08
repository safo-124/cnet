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

export function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  URL.revokeObjectURL(url)
}
