import { useQuery } from '@tanstack/react-query'
import { ArrowRightIcon, FileBoxIcon, HistoryIcon, ScanSearchIcon } from 'lucide-react'
import { Link } from 'react-router'
import { PageHeader } from '@/components/PageHeader'
import { StatusBar } from '@/components/StatusBar'
import { buttonVariants } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { api, type ModelHistorySummary } from '@/lib/api'
import { formatRelative, percentOk } from '@/lib/format'
import { cn } from '@/lib/utils'

export function HistoryPage() {
  const history = useQuery({ queryKey: ['audit-history'], queryFn: api.auditHistory })

  return (
    <div className="grid gap-6">
      <PageHeader
        title="Audit history"
        description="Every audited model, grouped by its IFC project. A fixed or re-exported file keeps the same project, so you can follow a model as it improves."
      />

      {history.isPending && (
        <div className="grid gap-4 md:grid-cols-2">
          <Skeleton className="h-40 rounded-xl" />
          <Skeleton className="h-40 rounded-xl" />
        </div>
      )}

      {history.isError && <p className="text-destructive">{history.error.message}</p>}

      {history.data?.length === 0 && (
        <Card className="items-center gap-3 py-14 text-center">
          <span className="flex size-11 items-center justify-center rounded-full bg-muted">
            <HistoryIcon className="size-5 text-muted-foreground" />
          </span>
          <div className="grid gap-1">
            <p className="font-medium">No audits yet</p>
            <p className="text-sm text-muted-foreground">Audit a model and it appears here.</p>
          </div>
          <Link to="/audit" className={buttonVariants({ size: 'sm' })}>
            <ScanSearchIcon /> Audit a model
          </Link>
        </Card>
      )}

      {history.data && history.data.length > 0 && (
        <div className="grid gap-4 md:grid-cols-2">
          {history.data.map((model) => (
            <ModelCard key={model.projectGlobalId} model={model} />
          ))}
        </div>
      )}
    </div>
  )
}

function ModelCard({ model }: { model: ModelHistorySummary }) {
  const first = percentOk(model.first)
  const latest = percentOk(model.latest)
  const change = latest - first

  return (
    <Link
      to={`/history/${encodeURIComponent(model.projectGlobalId)}`}
      className="group rounded-xl focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
    >
      <Card className="h-full gap-4 px-5 transition-shadow group-hover:shadow-md">
        <div className="flex items-start gap-3">
          <span className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-muted">
            <FileBoxIcon className="size-4 text-muted-foreground" />
          </span>
          <div className="grid min-w-0 flex-1">
            <span className="truncate font-medium">{model.projectName ?? 'Unnamed project'}</span>
            <span className="truncate text-xs text-muted-foreground">
              {model.runs} {model.runs === 1 ? 'audit' : 'audits'} · last {formatRelative(model.latest.auditedUtc)} ·{' '}
              {model.latest.fileName}
            </span>
          </div>
          <ArrowRightIcon className="size-4 text-muted-foreground transition-transform group-hover:translate-x-0.5" />
        </div>

        <div className="flex items-end justify-between gap-4">
          <div>
            <div className="text-3xl leading-none font-semibold tabular-nums">{latest}%</div>
            <div className="mt-1 text-xs text-muted-foreground">match the catalog</div>
          </div>
          {model.runs > 1 && (
            <span
              className={cn(
                'rounded-full px-2 py-0.5 text-xs font-medium tabular-nums',
                change > 0 && 'bg-success-soft text-success',
                change < 0 && 'bg-danger-soft text-danger',
                change === 0 && 'bg-muted text-muted-foreground',
              )}
            >
              {change > 0 ? '+' : ''}
              {change} pts since first audit
            </span>
          )}
        </div>

        <StatusBar ok={model.latest.ok} fixable={model.latest.needsUpdate} designer={model.latest.needsDesigner} />
      </Card>
    </Link>
  )
}
