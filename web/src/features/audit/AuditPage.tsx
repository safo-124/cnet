import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { ArrowRightIcon, CircleAlertIcon, DownloadIcon, RotateCcwIcon } from 'lucide-react'
import { toast } from 'sonner'
import { FileDrop } from '@/components/FileDrop'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { api, type AuditDevice, type AuditResponse, type AuditStatus } from '@/lib/api'
import { downloadBlob, FIELD_INFO, formatNumber } from '@/lib/format'
import { cn } from '@/lib/utils'

const STATUS_INFO: Record<AuditStatus, { label: string; className: string }> = {
  Ok: { label: 'OK', className: 'bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300' },
  NeedsUpdate: { label: 'Needs update', className: 'bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-300' },
  Unidentified: { label: 'Unidentified', className: 'bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-300' },
  NotInCatalog: { label: 'Not in catalog', className: 'bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-300' },
  CategoryMismatch: { label: 'Wrong product type', className: 'bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-300' },
}

const NEEDS_DESIGNER: AuditStatus[] = ['Unidentified', 'NotInCatalog', 'CategoryMismatch']

type Filter = 'all' | 'fixable' | 'designer' | 'ok'

const FILTERS: { value: Filter; label: string; matches: (d: AuditDevice) => boolean }[] = [
  { value: 'all', label: 'All', matches: () => true },
  { value: 'fixable', label: 'Fixable', matches: (d) => d.status === 'NeedsUpdate' },
  { value: 'designer', label: 'Needs designer', matches: (d) => NEEDS_DESIGNER.includes(d.status) },
  { value: 'ok', label: 'OK', matches: (d) => d.status === 'Ok' },
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

  function start(selected: File) {
    setFile(selected)
    setFilter('all')
    audit.mutate(selected)
  }

  function reset() {
    setFile(null)
    audit.reset()
  }

  return (
    <div className="grid gap-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Model audit</h1>
          <p className="text-muted-foreground">
            Check every air terminal, fan, damper and light fixture in an IFC4 model against the catalog.
          </p>
        </div>
        {audit.data && (
          <div className="flex gap-2">
            <Button variant="outline" onClick={reset}>
              <RotateCcwIcon /> Audit another model
            </Button>
            <Button
              disabled={audit.data.summary.needsUpdate === 0 || fix.isPending}
              onClick={() => file && fix.mutate(file)}
            >
              <DownloadIcon /> {fix.isPending ? 'Fixing…' : `Download fixed model (${audit.data.summary.needsUpdate})`}
            </Button>
          </div>
        )}
      </div>

      {!audit.data && (
        <FileDrop
          accept=".ifc"
          className="py-16"
          disabled={audit.isPending}
          title={audit.isPending ? `Auditing ${file?.name}…` : 'Drop an IFC file here or click to choose'}
          hint="Try data/sample-building.ifc from the repository."
          onFile={start}
        />
      )}

      {audit.error && (
        <Alert variant="destructive">
          <CircleAlertIcon />
          <AlertTitle>Audit failed</AlertTitle>
          <AlertDescription>{audit.error.message}</AlertDescription>
        </Alert>
      )}

      {audit.data && <AuditResults result={audit.data} filter={filter} onFilter={setFilter} />}
    </div>
  )
}

function AuditResults({ result, filter, onFilter }: { result: AuditResponse; filter: Filter; onFilter: (f: Filter) => void }) {
  const { summary } = result
  const needsDesigner = summary.unidentified + summary.notInCatalog + summary.categoryMismatch
  const visible = result.devices.filter(FILTERS.find((f) => f.value === filter)!.matches)

  return (
    <>
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <SummaryCard label="Devices" value={summary.total} detail={result.fileName} />
        <SummaryCard label="OK" value={summary.ok} detail="Match the catalog" tone="ok" />
        <SummaryCard label="Fixable" value={summary.needsUpdate} detail="Missing or outdated values" tone="warn" />
        <SummaryCard label="Need a designer" value={needsDesigner} detail="Can't be fixed automatically" tone="bad" />
      </div>

      <div className="flex flex-wrap gap-2" role="group" aria-label="Filter devices">
        {FILTERS.map((f) => (
          <Button
            key={f.value}
            size="sm"
            variant={filter === f.value ? 'default' : 'outline'}
            aria-pressed={filter === f.value}
            onClick={() => onFilter(f.value)}
          >
            {f.label} ({result.devices.filter(f.matches).length})
          </Button>
        ))}
      </div>

      <div className="rounded-xl border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Level</TableHead>
              <TableHead>Device</TableHead>
              <TableHead>Product in model</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Details</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {visible.map((d) => (
              <TableRow key={d.id} className="align-top">
                <TableCell className="whitespace-nowrap">{d.level ?? '—'}</TableCell>
                <TableCell>
                  <div className="font-medium">{d.name ?? '(unnamed)'}</div>
                  <div className="text-xs text-muted-foreground">{d.elementType}</div>
                </TableCell>
                <TableCell>
                  {d.manufacturer || d.model ? (
                    <>
                      <div>{d.model ?? '—'}</div>
                      <div className="text-xs text-muted-foreground">{d.manufacturer ?? '—'}</div>
                    </>
                  ) : (
                    <span className="text-muted-foreground">Not set</span>
                  )}
                </TableCell>
                <TableCell>
                  <Badge className={cn('border-transparent', STATUS_INFO[d.status].className)}>
                    {STATUS_INFO[d.status].label}
                  </Badge>
                </TableCell>
                <TableCell className="whitespace-normal">
                  <div className="text-sm">{d.message}</div>
                  {d.changes.length > 0 && (
                    <ul className="mt-1 grid gap-0.5 text-xs text-muted-foreground">
                      {d.changes.map((c) => {
                        const info = FIELD_INFO[c.field]
                        return (
                          <li key={c.field} className="flex items-center gap-1.5 tabular-nums">
                            <span className="w-20">{info.label}</span>
                            <span className={c.modelValue === null ? 'italic' : 'line-through'}>
                              {c.modelValue === null ? 'missing' : formatNumber(c.modelValue, info.unit)}
                            </span>
                            <ArrowRightIcon className="size-3" />
                            <span className="font-medium text-foreground">{formatNumber(c.catalogValue, info.unit)}</span>
                          </li>
                        )
                      })}
                    </ul>
                  )}
                </TableCell>
              </TableRow>
            ))}
            {visible.length === 0 && (
              <TableRow>
                <TableCell colSpan={5} className="py-10 text-center text-muted-foreground">
                  No devices in this group.
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </div>
    </>
  )
}

function SummaryCard({
  label,
  value,
  detail,
  tone,
}: {
  label: string
  value: number
  detail: string
  tone?: 'ok' | 'warn' | 'bad'
}) {
  return (
    <Card>
      <CardContent className="grid gap-1">
        <span className="text-sm text-muted-foreground">{label}</span>
        <span
          className={cn(
            'text-3xl font-semibold tabular-nums',
            tone === 'ok' && 'text-emerald-600 dark:text-emerald-400',
            tone === 'warn' && value > 0 && 'text-amber-600 dark:text-amber-400',
            tone === 'bad' && value > 0 && 'text-red-600 dark:text-red-400',
          )}
        >
          {value}
        </span>
        <span className="truncate text-xs text-muted-foreground" title={detail}>
          {detail}
        </span>
      </CardContent>
    </Card>
  )
}
