import { useQuery } from '@tanstack/react-query'
import { ArrowLeftIcon, ArrowRightIcon } from 'lucide-react'
import { Link, useParams } from 'react-router'
import { PageHeader } from '@/components/PageHeader'
import { StatusBar } from '@/components/StatusBar'
import { buttonVariants } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { api, type AuditRun } from '@/lib/api'
import { formatDateTime, percentOk } from '@/lib/format'
import { AuditHistoryChart } from './AuditHistoryChart'

export function ModelHistoryPage() {
  const { projectGlobalId = '' } = useParams()
  const history = useQuery({
    queryKey: ['audit-history', projectGlobalId],
    queryFn: () => api.modelHistory(projectGlobalId),
  })
  const runs = history.data?.runs ?? []
  const first = runs[0]
  const latest = runs[runs.length - 1]

  return (
    <div className="grid gap-6">
      <Link to="/history" className={buttonVariants({ variant: 'ghost', size: 'sm', className: 'w-fit -ml-2' })}>
        <ArrowLeftIcon /> All models
      </Link>

      <PageHeader
        title={history.data?.projectName ?? (history.isPending ? 'Loading…' : 'Model history')}
        description={
          <>
            IFC project <code className="text-xs">{projectGlobalId}</code>
          </>
        }
      />

      {history.isPending && <Skeleton className="h-80 rounded-xl" />}
      {history.isError && <p className="text-destructive">{history.error.message}</p>}

      {first && latest && (
        <>
          <div className="grid gap-4 sm:grid-cols-3">
            <Stat label="First audit" value={`${percentOk(first)}%`} detail={formatDateTime(first.auditedUtc)} />
            <Stat
              label="Latest audit"
              value={`${percentOk(latest)}%`}
              detail={formatDateTime(latest.auditedUtc)}
              emphasis
            />
            <Stat
              label="Still need a designer"
              value={String(latest.needsDesigner)}
              detail={`of ${latest.total} devices`}
            />
          </div>

          <Card className="gap-4 px-6">
            <div className="flex flex-wrap items-baseline justify-between gap-2">
              <span className="font-medium">Device status per audit</span>
              <span className="text-sm text-muted-foreground">
                {runs.length} {runs.length === 1 ? 'audit' : 'audits'}, oldest first
              </span>
            </div>
            <AuditHistoryChart runs={runs} />
          </Card>

          <RunsTable runs={runs} />
        </>
      )}
    </div>
  )
}

function Stat({ label, value, detail, emphasis }: { label: string; value: string; detail: string; emphasis?: boolean }) {
  return (
    <Card className="gap-1 px-5">
      <span className="text-sm text-muted-foreground">{label}</span>
      <span className={emphasis ? 'text-3xl font-semibold text-primary tabular-nums' : 'text-3xl font-semibold tabular-nums'}>
        {value}
      </span>
      <span className="text-xs text-muted-foreground">{detail}</span>
    </Card>
  )
}

/** The chart's data as a table: the accessible view, and where the exact numbers live. Newest first. */
function RunsTable({ runs }: { runs: AuditRun[] }) {
  const newestFirst = runs.map((run, i) => ({ run, number: i + 1, previous: runs[i - 1] })).reverse()

  return (
    <Card className="gap-0 overflow-hidden py-0">
      {/* Phones: one row per audit with the essentials. */}
      <ul className="divide-y md:hidden">
        {newestFirst.map(({ run, number, previous }) => (
          <li key={run.id} className="grid gap-2 px-4 py-3">
            <div className="flex items-baseline justify-between gap-3">
              <span className="font-medium">
                <span className="text-muted-foreground tabular-nums">#{number}</span> {formatDateTime(run.auditedUtc)}
              </span>
              <span className="text-sm font-semibold tabular-nums">
                {percentOk(run)}%
                {previous && percentOk(run) !== percentOk(previous) && (
                  <span className="ml-1 text-xs font-normal text-muted-foreground">
                    ({percentOk(run) > percentOk(previous) ? '+' : ''}
                    {percentOk(run) - percentOk(previous)})
                  </span>
                )}
              </span>
            </div>
            <StatusBar ok={run.ok} fixable={run.needsUpdate} designer={run.needsDesigner} />
            <div className="flex justify-between gap-3 text-xs text-muted-foreground tabular-nums">
              <span className="min-w-0 truncate">{run.fileName}</span>
              <span className="shrink-0">
                {run.ok} OK · {run.needsUpdate} fixable · {run.needsDesigner} designer
              </span>
            </div>
          </li>
        ))}
      </ul>

      <Table className="hidden md:table">
        <TableHeader className="bg-muted/50">
          <TableRow className="hover:bg-transparent">
            <TableHead className="pl-4">#</TableHead>
            <TableHead>Audited</TableHead>
            <TableHead>File</TableHead>
            <TableHead className="text-right">OK</TableHead>
            <TableHead className="text-right">Fixable</TableHead>
            <TableHead className="text-right">Needs designer</TableHead>
            <TableHead className="w-40 pr-4">Status</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {newestFirst.map(({ run, number, previous }) => (
              <TableRow key={run.id}>
                <TableCell className="pl-4 text-muted-foreground tabular-nums">{number}</TableCell>
                <TableCell className="whitespace-nowrap">{formatDateTime(run.auditedUtc)}</TableCell>
                <TableCell className="max-w-48 truncate" title={run.fileName}>
                  {run.fileName}
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  {run.ok}
                  {previous && run.ok !== previous.ok && (
                    <span className="ml-1 text-xs text-muted-foreground">
                      ({run.ok > previous.ok ? '+' : ''}
                      {run.ok - previous.ok})
                    </span>
                  )}
                </TableCell>
                <TableCell className="text-right tabular-nums">{run.needsUpdate}</TableCell>
                <TableCell className="text-right tabular-nums">{run.needsDesigner}</TableCell>
                <TableCell className="pr-4">
                  <StatusBar ok={run.ok} fixable={run.needsUpdate} designer={run.needsDesigner} />
                </TableCell>
              </TableRow>
            ))}
        </TableBody>
      </Table>
      <div className="flex items-center gap-1 border-t px-4 py-3 text-sm text-muted-foreground">
        Audit the model again from
        <Link to="/audit" className="inline-flex items-center gap-1 font-medium text-primary hover:underline">
          Model audit <ArrowRightIcon className="size-3.5" />
        </Link>
      </div>
    </Card>
  )
}
