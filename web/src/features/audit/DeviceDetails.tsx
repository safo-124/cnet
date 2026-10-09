import { ArrowRightIcon, XIcon } from 'lucide-react'
import { StatusBadge } from '@/components/StatusBadge'
import { Button } from '@/components/ui/button'
import type { AuditDevice } from '@/lib/api'
import { FIELD_INFO, formatNumber } from '@/lib/format'

/** The selected device from the 3D view, stacked so it reads well on a phone as well as a desktop. */
export function DeviceDetails({ device, typeLabel, onClose }: { device: AuditDevice; typeLabel: string; onClose: () => void }) {
  return (
    <div className="grid gap-3 border-t px-4 py-4">
      <div className="flex items-start gap-3">
        <div className="grid min-w-0 flex-1 gap-0.5">
          <span className="font-medium">{device.name ?? '(unnamed)'}</span>
          <span className="text-xs text-muted-foreground">
            {typeLabel}
            {device.level && ` · ${device.level}`}
          </span>
        </div>
        <StatusBadge status={device.status} />
        <Button variant="ghost" size="icon-sm" aria-label="Close details" onClick={onClose}>
          <XIcon />
        </Button>
      </div>

      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
        <dt className="text-muted-foreground">Product</dt>
        <dd>{device.model ? `${device.manufacturer ?? '—'} ${device.model}` : <span className="italic text-muted-foreground">Not set</span>}</dd>
        <dt className="text-muted-foreground">Result</dt>
        <dd>{device.message}</dd>
      </dl>

      {device.changes.length > 0 && (
        <div className="grid gap-1 rounded-lg bg-muted/50 p-3 text-sm tabular-nums">
          {device.changes.map((c) => {
            const info = FIELD_INFO[c.field]
            return (
              <div key={c.field} className="flex flex-wrap items-center gap-x-2">
                <span className="w-24 text-muted-foreground">{info.label}</span>
                <span className={c.modelValue === null ? 'italic text-muted-foreground' : 'text-muted-foreground line-through'}>
                  {c.modelValue === null ? 'missing' : formatNumber(c.modelValue, info.unit)}
                </span>
                <ArrowRightIcon className="size-3.5 text-muted-foreground" />
                <span className="font-medium">{formatNumber(c.catalogValue, info.unit)}</span>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}
