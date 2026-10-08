import type { AuditStatus } from '@/lib/api'

export type StatusTone = 'success' | 'warning' | 'danger'

export const STATUS_INFO: Record<AuditStatus, { label: string; tone: StatusTone }> = {
  Ok: { label: 'OK', tone: 'success' },
  NeedsUpdate: { label: 'Needs update', tone: 'warning' },
  Unidentified: { label: 'Unidentified', tone: 'danger' },
  NotInCatalog: { label: 'Not in catalog', tone: 'danger' },
  CategoryMismatch: { label: 'Wrong product type', tone: 'danger' },
}

export const TONE_CLASSES: Record<StatusTone, { pill: string; dot: string; text: string; bar: string }> = {
  // `dot` and `bar` use the color-blind-validated chart fills; text keeps the higher-contrast status tokens.
  success: { pill: 'bg-success-soft text-success', dot: 'bg-chart-ok', text: 'text-success', bar: 'bg-chart-ok' },
  warning: { pill: 'bg-warning-soft text-warning', dot: 'bg-chart-fixable', text: 'text-warning', bar: 'bg-chart-fixable' },
  danger: { pill: 'bg-danger-soft text-danger', dot: 'bg-chart-designer', text: 'text-danger', bar: 'bg-chart-designer' },
}
