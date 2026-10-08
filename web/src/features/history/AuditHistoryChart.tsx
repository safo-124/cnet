import { useLayoutEffect, useRef, useState } from 'react'
import type { AuditRun } from '@/lib/api'
import { formatDateTime, percentOk } from '@/lib/format'

/** Bottom-to-top order: OK first, so the green part growing over time reads as progress. */
const SEGMENTS = [
  { key: 'ok', label: 'OK', fill: 'var(--chart-ok)' },
  { key: 'needsUpdate', label: 'Fixable', fill: 'var(--chart-fixable)' },
  { key: 'needsDesigner', label: 'Needs designer', fill: 'var(--chart-designer)' },
] as const

const HEIGHT = 240
const MARGIN = { top: 24, right: 8, bottom: 40, left: 32 }
const GAP = 2 // surface gap between stacked segments
const RADIUS = 4 // rounded data end (top of the bar only; the base sits on the axis)

/** Stacked bars of device status per audit run, oldest on the left. */
export function AuditHistoryChart({ runs }: { runs: AuditRun[] }) {
  const container = useRef<HTMLDivElement>(null)
  const width = useWidth(container)
  const [active, setActive] = useState<number | null>(null)

  const plotWidth = Math.max(0, width - MARGIN.left - MARGIN.right)
  const plotHeight = HEIGHT - MARGIN.top - MARGIN.bottom
  const maxTotal = Math.max(1, ...runs.map((r) => r.total))
  const step = Math.max(1, Math.ceil(maxTotal / 4))
  const yMax = step * Math.ceil(maxTotal / step)
  const y = (value: number) => MARGIN.top + plotHeight - (value / yMax) * plotHeight
  const band = runs.length === 0 ? 0 : plotWidth / runs.length
  const barWidth = Math.min(40, band * 0.6)
  const labelEvery = Math.max(1, Math.ceil(runs.length / Math.max(1, Math.floor(plotWidth / 56))))

  return (
    <div className="grid gap-3">
      <Legend />
      <div ref={container} className="relative">
        {width > 0 && (
          <svg width={width} height={HEIGHT} role="img" aria-label="Device status per audit, oldest first">
            {Array.from({ length: yMax / step + 1 }, (_, i) => i * step).map((tick) => (
              <g key={tick}>
                <line x1={MARGIN.left} x2={width - MARGIN.right} y1={y(tick)} y2={y(tick)} stroke="var(--border)" />
                <text x={MARGIN.left - 8} y={y(tick)} dy="0.32em" textAnchor="end" className="fill-muted-foreground text-[11px] tabular-nums">
                  {tick}
                </text>
              </g>
            ))}

            {runs.map((run, i) => {
              const cx = MARGIN.left + band * i + band / 2
              const x = cx - barWidth / 2
              const dimmed = active !== null && active !== i
              let base = 0
              const parts = SEGMENTS.map((s) => ({ ...s, value: run[s.key] })).filter((s) => s.value > 0)

              return (
                <g key={run.id} opacity={dimmed ? 0.45 : 1} className="transition-opacity">
                  {parts.map((part, p) => {
                    const top = y(base + part.value)
                    const bottom = y(base) - (p === 0 ? 0 : GAP)
                    base += part.value
                    const isTop = p === parts.length - 1
                    return (
                      <path key={part.key} d={barPath(x, top, barWidth, Math.max(0, bottom - top), isTop ? RADIUS : 0)} fill={part.fill} />
                    )
                  })}

                  {/* Selective direct labels: only the first and latest audit carry their score. */}
                  {(i === 0 || i === runs.length - 1) && (
                    <text x={cx} y={y(run.total) - 6} textAnchor="middle" className="fill-foreground text-[11px] font-medium tabular-nums">
                      {percentOk(run)}%
                    </text>
                  )}

                  {i % labelEvery === 0 && (
                    <text x={cx} y={HEIGHT - MARGIN.bottom + 16} textAnchor="middle" className="fill-muted-foreground text-[11px] tabular-nums">
                      #{i + 1}
                    </text>
                  )}

                  {/* Hit target: the whole column, wider than the bar, and reachable by keyboard. */}
                  <rect
                    x={MARGIN.left + band * i}
                    y={MARGIN.top}
                    width={band}
                    height={plotHeight}
                    fill="transparent"
                    tabIndex={0}
                    aria-label={`Audit ${i + 1}, ${formatDateTime(run.auditedUtc)}: ${run.ok} OK, ${run.needsUpdate} fixable, ${run.needsDesigner} need a designer`}
                    className="outline-none focus-visible:stroke-ring"
                    onMouseEnter={() => setActive(i)}
                    onMouseLeave={() => setActive(null)}
                    onFocus={() => setActive(i)}
                    onBlur={() => setActive(null)}
                  />
                </g>
              )
            })}

            <line x1={MARGIN.left} x2={width - MARGIN.right} y1={y(0)} y2={y(0)} stroke="var(--muted-foreground)" strokeOpacity={0.5} />
          </svg>
        )}

        {active !== null && runs[active] && (
          <Tooltip run={runs[active]} index={active} left={MARGIN.left + band * active + band / 2} width={width} />
        )}
      </div>
    </div>
  )
}

function Tooltip({ run, index, left, width }: { run: AuditRun; index: number; left: number; width: number }) {
  // Keep the card inside the chart: anchor it left, centered or right depending on where the bar is.
  const align = left < 110 ? 'translate-x-0' : left > width - 110 ? '-translate-x-full' : '-translate-x-1/2'
  return (
    <div
      className={`pointer-events-none absolute top-0 z-10 w-52 rounded-lg border bg-popover p-3 text-sm shadow-md ${align}`}
      style={{ left }}
    >
      <div className="font-medium">
        Audit #{index + 1} · {percentOk(run)}% OK
      </div>
      <div className="mb-2 truncate text-xs text-muted-foreground">
        {formatDateTime(run.auditedUtc)} · {run.fileName}
      </div>
      {SEGMENTS.map((s) => (
        <div key={s.key} className="flex items-center gap-2">
          <span className="size-2.5 rounded-sm" style={{ background: s.fill }} />
          <span className="text-muted-foreground">{s.label}</span>
          <span className="ml-auto font-medium tabular-nums">{run[s.key]}</span>
        </div>
      ))}
    </div>
  )
}

function Legend() {
  return (
    <div className="flex flex-wrap gap-x-5 gap-y-1 text-sm" aria-hidden="true">
      {SEGMENTS.map((s) => (
        <span key={s.key} className="flex items-center gap-2 text-muted-foreground">
          <span className="size-2.5 rounded-sm" style={{ background: s.fill }} />
          {s.label}
        </span>
      ))}
    </div>
  )
}

/** A rectangle whose top corners are rounded by r; the base stays square on the axis. */
function barPath(x: number, top: number, w: number, h: number, r: number): string {
  const radius = Math.min(r, w / 2, h)
  return [
    `M${x},${top + h}`,
    `V${top + radius}`,
    `Q${x},${top} ${x + radius},${top}`,
    `H${x + w - radius}`,
    `Q${x + w},${top} ${x + w},${top + radius}`,
    `V${top + h}`,
    'Z',
  ].join(' ')
}

function useWidth(ref: React.RefObject<HTMLElement | null>): number {
  const [width, setWidth] = useState(0)
  useLayoutEffect(() => {
    if (!ref.current) return
    const observer = new ResizeObserver(([entry]) => setWidth(Math.floor(entry.contentRect.width)))
    observer.observe(ref.current)
    return () => observer.disconnect()
  }, [ref])
  return width
}
