import { Fragment, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import {
  AirVentIcon,
  ArrowRightIcon,
  BlindsIcon,
  BoxIcon,
  CircleAlertIcon,
  DownloadIcon,
  FanIcon,
  FileBoxIcon,
  FileSpreadsheetIcon,
  LightbulbIcon,
  ListChecksIcon,
  RotateCcwIcon,
  UploadIcon,
  WrenchIcon,
  type LucideIcon,
} from 'lucide-react'
import { toast } from 'sonner'
import { FileDrop } from '@/components/FileDrop'
import { PageHeader } from '@/components/PageHeader'
import { StatusBadge, STATUS_INFO, TONE_CLASSES, type StatusTone } from '@/components/StatusBadge'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { api, SAMPLES, type AuditDevice, type AuditResponse } from '@/lib/api'
import { downloadBlob, FIELD_INFO, formatNumber } from '@/lib/format'
import { cn } from '@/lib/utils'

const ELEMENT_TYPES: Record<string, { label: string; icon: LucideIcon }> = {
  IfcAirTerminal: { label: 'Air terminal', icon: AirVentIcon },
  IfcFan: { label: 'Fan', icon: FanIcon },
  IfcDamper: { label: 'Damper', icon: BlindsIcon },
  IfcLightFixture: { label: 'Light fixture', icon: LightbulbIcon },
}

type Filter = 'all' | 'fixable' | 'designer' | 'ok'

const toneOf = (d: AuditDevice): StatusTone => STATUS_INFO[d.status].tone

const FILTERS: { value: Filter; label: string; matches: (d: AuditDevice) => boolean }[] = [
  { value: 'all', label: 'All', matches: () => true },
  { value: 'fixable', label: 'Fixable', matches: (d) => toneOf(d) === 'warning' },
  { value: 'designer', label: 'Needs designer', matches: (d) => toneOf(d) === 'danger' },
  { value: 'ok', label: 'OK', matches: (d) => toneOf(d) === 'success' },
]

export function AuditPage() {
  const [file, setFile] = useState<File | null>(null)
  const [filter, setFilter] = useState<Filter>('all')

  const audit = useMutation({ mutationFn: api.auditModel })
  const fix = useMutation({
    mutationFn: api.fixModel,
    onSuccess: ({ blob, fileName }) => {
      downloadBlob(blob, fileName)
      toast.success(`Downloaded ${fileName}`, { description: 'Upload it here again to check the result.' })
    },
    onError: (error) => toast.error(error.message),
  })
  const report = useMutation({
    mutationFn: api.auditReport,
    onSuccess: ({ blob, fileName }) => {
      downloadBlob(blob, fileName)
      toast.success(`Downloaded ${fileName}`)
    },
    onError: (error) => toast.error(error.message),
  })

  function start(selected: File) {
    setFile(selected)
    setFilter('all')
    audit.mutate(selected)
  }

  function reset() {
    setFile(null)
    audit.reset()
  }

  const result = audit.data

  return (
    <div className="grid gap-6">
      <PageHeader
        title="Model audit"
        description="Check every air terminal, fan, damper and light fixture in an IFC4 model against the catalog."
        actions={
          result && (
            <Button variant="outline" onClick={reset}>
              <RotateCcwIcon /> Audit another model
            </Button>
          )
        }
      />

      {!result && (
        <Card className="gap-0 overflow-hidden py-0">
          <div className="p-4 sm:p-6">
            <FileDrop
              accept=".ifc"
              className="bg-muted/30 py-16"
              disabled={audit.isPending}
              title={audit.isPending ? `Auditing ${file?.name}…` : 'Drop an IFC model here or click to choose'}
              hint={audit.isPending ? 'Reading devices and checking the catalog' : 'IFC4 · up to 100 MB'}
              onFile={start}
            />
            <p className="mt-3 text-center text-sm text-muted-foreground">
              No IFC model at hand?{' '}
              <a href={SAMPLES.model} download className="font-medium text-primary underline-offset-4 hover:underline">
                Download the sample office model
              </a>{' '}
              (14 devices on 2 levels) and drop it above.
            </p>
          </div>
          <div className="grid gap-px border-t bg-border sm:grid-cols-3">
            <Step n={1} icon={UploadIcon} title="Upload a model" text="Any IFC4 export from Revit, MagiCAD or another design tool." />
            <Step n={2} icon={ListChecksIcon} title="Review the results" text="See which devices are missing data or are linked to the wrong product." />
            <Step n={3} icon={DownloadIcon} title="Fix and report" text="Download the fixed model and an Excel to-do list for designers." />
          </div>
        </Card>
      )}

      {audit.error && (
        <Alert variant="destructive">
          <CircleAlertIcon />
          <AlertTitle>Audit failed</AlertTitle>
          <AlertDescription>{audit.error.message}</AlertDescription>
        </Alert>
      )}

      {result && (
        <>
          <div className="grid gap-4 lg:grid-cols-3">
            <HealthCard result={result} />
            <NextStepsCard
              result={result}
              fixing={fix.isPending}
              reporting={report.isPending}
              onFix={() => file && fix.mutate(file)}
              onReport={() => file && report.mutate(file)}
            />
          </div>
          <DeviceTable devices={result.devices} filter={filter} onFilter={setFilter} />
        </>
      )}
    </div>
  )
}

function Step({ n, icon: Icon, title, text }: { n: number; icon: LucideIcon; title: string; text: string }) {
  return (
    <div className="flex gap-3 bg-card p-5">
      <span className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
        <Icon className="size-4" />
      </span>
      <div className="grid gap-0.5">
        <span className="text-sm font-medium">
          <span className="text-muted-foreground tabular-nums">{n}.</span> {title}
        </span>
        <span className="text-sm text-muted-foreground">{text}</span>
      </div>
    </div>
  )
}

function HealthCard({ result }: { result: AuditResponse }) {
  const { summary } = result
  const designer = summary.unidentified + summary.notInCatalog + summary.categoryMismatch
  const levels = new Set(result.devices.map((d) => d.level ?? '—')).size
  const percentOk = summary.total === 0 ? 0 : Math.round((summary.ok / summary.total) * 100)
  const segments: { tone: StatusTone; label: string; count: number }[] = [
    { tone: 'success', label: 'OK', count: summary.ok },
    { tone: 'warning', label: 'Fixable', count: summary.needsUpdate },
    { tone: 'danger', label: 'Needs designer', count: designer },
  ]

  return (
    <Card className="justify-between gap-5 px-6 lg:col-span-2">
      <div className="flex items-start gap-3">
        <span className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-muted">
          <FileBoxIcon className="size-5 text-muted-foreground" />
        </span>
        <div className="grid min-w-0 gap-0.5">
          <span className="truncate font-medium" title={result.fileName}>
            {result.fileName}
          </span>
          <span className="text-sm text-muted-foreground">
            {summary.total} devices on {levels} {levels === 1 ? 'level' : 'levels'}
          </span>
        </div>
        <div className="ml-auto text-right">
          <div className="text-3xl leading-none font-semibold tabular-nums">{percentOk}%</div>
          <div className="mt-1 text-xs text-muted-foreground">match the catalog</div>
        </div>
      </div>

      <div className="grid gap-3">
        <div className="flex h-2.5 overflow-hidden rounded-full bg-muted" role="img" aria-label={`${percentOk}% of devices match the catalog`}>
          {segments.map(
            (s) =>
              s.count > 0 && (
                <div
                  key={s.tone}
                  className={cn('h-full first:rounded-l-full last:rounded-r-full', TONE_CLASSES[s.tone].bar)}
                  style={{ width: `${(s.count / summary.total) * 100}%` }}
                />
              ),
          )}
        </div>
        <div className="flex flex-wrap gap-x-6 gap-y-2">
          {segments.map((s) => (
            <div key={s.tone} className="flex items-center gap-2 text-sm">
              <span className={cn('size-2 rounded-full', TONE_CLASSES[s.tone].dot)} />
              <span className="font-semibold tabular-nums">{s.count}</span>
              <span className="text-muted-foreground">{s.label}</span>
            </div>
          ))}
        </div>
      </div>

      <div className="grid grid-cols-2 gap-px overflow-hidden rounded-lg border bg-border xl:grid-cols-4">
        {Object.entries(ELEMENT_TYPES).map(([type, { label, icon: Icon }]) => {
          const ofType = result.devices.filter((d) => d.elementType === type)
          const ok = ofType.filter((d) => d.status === 'Ok').length
          return (
            <div key={type} className="flex items-center gap-2.5 bg-card px-3 py-2.5">
              <Icon className="size-4 shrink-0 text-muted-foreground" />
              <div className="grid leading-tight">
                <span className="text-sm whitespace-nowrap">{label}s</span>
                <span className="text-xs text-muted-foreground tabular-nums">
                  {ok} of {ofType.length} OK
                </span>
              </div>
            </div>
          )
        })}
      </div>
    </Card>
  )
}

function NextStepsCard({
  result,
  fixing,
  reporting,
  onFix,
  onReport,
}: {
  result: AuditResponse
  fixing: boolean
  reporting: boolean
  onFix: () => void
  onReport: () => void
}) {
  const { summary } = result
  const designer = summary.unidentified + summary.notInCatalog + summary.categoryMismatch

  return (
    <Card className="gap-4 px-6">
      <span className="text-sm font-medium">Next steps</span>
      <div className="grid gap-3">
        <div className="grid gap-2">
          <p className="text-sm text-muted-foreground">
            {summary.needsUpdate > 0
              ? `${summary.needsUpdate} devices can be filled in from the catalog automatically.`
              : 'Nothing to fix automatically.'}
          </p>
          <Button disabled={summary.needsUpdate === 0 || fixing} onClick={onFix}>
            <WrenchIcon /> {fixing ? 'Fixing…' : 'Download fixed model'}
          </Button>
        </div>
        <div className="grid gap-2 border-t pt-3">
          <p className="text-sm text-muted-foreground">
            {designer > 0 ? `${designer} devices need a designer's decision.` : 'No devices need a designer.'}
          </p>
          <Button variant="outline" disabled={reporting} onClick={onReport}>
            <FileSpreadsheetIcon /> {reporting ? 'Creating…' : 'Excel report'}
          </Button>
        </div>
      </div>
    </Card>
  )
}

function DeviceTable({
  devices,
  filter,
  onFilter,
}: {
  devices: AuditDevice[]
  filter: Filter
  onFilter: (f: Filter) => void
}) {
  const visible = devices.filter(FILTERS.find((f) => f.value === filter)!.matches)
  // Devices arrive sorted by level, so grouping keeps the building order.
  const byLevel = new Map<string, AuditDevice[]>()
  for (const d of visible) {
    const level = d.level ?? 'No level'
    byLevel.set(level, [...(byLevel.get(level) ?? []), d])
  }

  return (
    <Card className="gap-0 overflow-hidden py-0">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b px-4 py-3">
        <span className="text-sm font-medium">Devices</span>
        <div className="flex rounded-lg bg-muted p-0.5" role="group" aria-label="Filter devices">
          {FILTERS.map((f) => (
            <button
              key={f.value}
              type="button"
              aria-pressed={filter === f.value}
              onClick={() => onFilter(f.value)}
              className={cn(
                'rounded-md px-3 py-1 text-sm text-muted-foreground transition-colors hover:text-foreground',
                filter === f.value && 'bg-card font-medium text-foreground shadow-sm',
              )}
            >
              {f.label}
              <span className="ml-1.5 text-xs text-muted-foreground tabular-nums">{devices.filter(f.matches).length}</span>
            </button>
          ))}
        </div>
      </div>

      <Table>
        <TableHeader className="bg-muted/50">
          <TableRow className="hover:bg-transparent">
            <TableHead className="pl-4">Device</TableHead>
            <TableHead>Product in model</TableHead>
            <TableHead>Status</TableHead>
            <TableHead className="pr-4">Details</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {[...byLevel].map(([level, rows]) => (
            <Fragment key={level}>
              <TableRow className="bg-muted/30 hover:bg-muted/30">
                <TableCell colSpan={4} className="py-1.5 pl-4 text-xs font-medium tracking-wide text-muted-foreground uppercase">
                  {level} <span className="font-normal normal-case">· {rows.length} devices</span>
                </TableCell>
              </TableRow>
              {rows.map((d) => (
                <DeviceRow key={d.id} device={d} />
              ))}
            </Fragment>
          ))}
          {visible.length === 0 && (
            <TableRow className="hover:bg-transparent">
              <TableCell colSpan={4} className="py-12 text-center text-muted-foreground">
                No devices in this group.
              </TableCell>
            </TableRow>
          )}
        </TableBody>
      </Table>
    </Card>
  )
}

function DeviceRow({ device: d }: { device: AuditDevice }) {
  const type = ELEMENT_TYPES[d.elementType] ?? { label: d.elementType, icon: BoxIcon }
  const Icon = type.icon

  return (
    <TableRow className="align-top">
      <TableCell className="py-3 pl-4">
        <div className="flex items-start gap-2.5">
          <span className="mt-0.5 flex size-7 shrink-0 items-center justify-center rounded-md bg-muted text-muted-foreground">
            <Icon className="size-3.5" />
          </span>
          <div className="grid">
            <span className="font-medium">{d.name ?? '(unnamed)'}</span>
            <span className="text-xs text-muted-foreground">{type.label}</span>
          </div>
        </div>
      </TableCell>
      <TableCell className="py-3">
        {d.manufacturer || d.model ? (
          <div className="grid">
            <span>{d.model ?? '—'}</span>
            <span className="text-xs text-muted-foreground">{d.manufacturer ?? '—'}</span>
          </div>
        ) : (
          <span className="text-muted-foreground italic">Not set</span>
        )}
      </TableCell>
      <TableCell className="py-3">
        <StatusBadge status={d.status} />
      </TableCell>
      <TableCell className="py-3 pr-4 whitespace-normal">
        <div className="text-sm">{d.message}</div>
        {d.changes.length > 0 && (
          <div className="mt-1.5 grid w-fit grid-cols-[auto_auto_auto_auto] items-center gap-x-2 gap-y-0.5 text-xs tabular-nums">
            {d.changes.map((c) => {
              const info = FIELD_INFO[c.field]
              return (
                <Fragment key={c.field}>
                  <span className="text-muted-foreground">{info.label}</span>
                  <span className={c.modelValue === null ? 'text-muted-foreground italic' : 'text-muted-foreground line-through'}>
                    {c.modelValue === null ? 'missing' : formatNumber(c.modelValue, info.unit)}
                  </span>
                  <ArrowRightIcon className="size-3 text-muted-foreground" />
                  <span className="font-medium text-foreground">{formatNumber(c.catalogValue, info.unit)}</span>
                </Fragment>
              )
            })}
          </div>
        )}
      </TableCell>
    </TableRow>
  )
}
