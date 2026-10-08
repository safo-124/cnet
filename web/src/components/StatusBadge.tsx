import type { AuditStatus } from '@/lib/api'
import { cn } from '@/lib/utils'

export type StatusTone = 'success' | 'warning' | 'danger'

export const STATUS_INFO: Record<AuditStatus, { label: string; tone: StatusTone }> = {
  Ok: { label: 'OK', tone: 'success' },
  NeedsUpdate: { label: 'Needs update', tone: 'warning' },
  Unidentified: { label: 'Unidentified', tone: 'danger' },
  NotInCatalog: { label: 'Not in catalog', tone: 'danger' },
  CategoryMismatch: { label: 'Wrong product type', tone: 'danger' },
}

export const TONE_CLASSES: Record<StatusTone, { pill: string; dot: string; text: string; bar: string }> = {
  success: { pill: 'bg-success-soft text-success', dot: 'bg-success', text: 'text-success', bar: 'bg-success' },
  warning: { pill: 'bg-warning-soft text-warning', dot: 'bg-warning', text: 'text-warning', bar: 'bg-warning' },
  danger: { pill: 'bg-danger-soft text-danger', dot: 'bg-danger', text: 'text-danger', bar: 'bg-danger' },
}

/** A pill with a colored dot, e.g. "● Needs update". */
export function StatusPill({ tone, children, className }: { tone: StatusTone; children: React.ReactNode; className?: string }) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-medium whitespace-nowrap',
        TONE_CLASSES[tone].pill,
        className,
      )}
    >
      <span className={cn('size-1.5 rounded-full', TONE_CLASSES[tone].dot)} />
      {children}
    </span>
  )
}

export function StatusBadge({ status }: { status: AuditStatus }) {
  const { label, tone } = STATUS_INFO[status]
  return <StatusPill tone={tone}>{label}</StatusPill>
}
