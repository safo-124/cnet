import type { AuditStatus } from '@/lib/api'
import { STATUS_INFO, TONE_CLASSES, type StatusTone } from '@/lib/status'
import { cn } from '@/lib/utils'

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
