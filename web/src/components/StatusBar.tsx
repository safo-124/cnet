import { cn } from '@/lib/utils'

interface Props {
  ok: number
  fixable: number
  designer: number
  className?: string
}

/** Horizontal stacked bar of OK / fixable / needs-designer, with a 2px gap between the parts. */
export function StatusBar({ ok, fixable, designer, className }: Props) {
  const total = ok + fixable + designer
  const parts = [
    { value: ok, className: 'bg-chart-ok' },
    { value: fixable, className: 'bg-chart-fixable' },
    { value: designer, className: 'bg-chart-designer' },
  ].filter((p) => p.value > 0)

  return (
    <div
      className={cn('flex h-2 gap-0.5 overflow-hidden rounded-full bg-muted', className)}
      role="img"
      aria-label={`${ok} OK, ${fixable} fixable, ${designer} need a designer`}
    >
      {total > 0 &&
        parts.map((p, i) => (
          <div key={i} className={cn('h-full', p.className)} style={{ width: `${(p.value / total) * 100}%` }} />
        ))}
    </div>
  )
}
