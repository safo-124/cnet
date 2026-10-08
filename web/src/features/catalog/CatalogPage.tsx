import { useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import {
  ChevronLeftIcon,
  ChevronRightIcon,
  FileTextIcon,
  FileUpIcon,
  LayersIcon,
  PackageSearchIcon,
  PencilIcon,
  PlusIcon,
  SearchIcon,
  Trash2Icon,
  XIcon,
} from 'lucide-react'
import { PageHeader } from '@/components/PageHeader'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { CATEGORIES, CATEGORY_LABELS, api, type Product, type ProductCategory } from '@/lib/api'
import { CategoryBadge } from '@/components/CategoryBadge'
import { CATEGORY_STYLES } from '@/lib/categories'
import { formatNumber } from '@/lib/format'
import { useDebouncedValue } from '@/lib/hooks'
import { cn } from '@/lib/utils'
import { DatasheetDialog } from './DatasheetDialog'
import { DeleteProductDialog } from './DeleteProductDialog'
import { ImportDialog } from './ImportDialog'
import { ProductDialog } from './ProductDialog'

const PAGE_SIZE = 20

type DialogState =
  | { kind: 'none' }
  | { kind: 'edit'; product: Product | null }
  | { kind: 'delete'; product: Product }
  | { kind: 'import' }
  | { kind: 'datasheet' }

export function CatalogPage() {
  const [search, setSearch] = useState('')
  const [category, setCategory] = useState<ProductCategory | null>(null)
  const [page, setPage] = useState(1)
  const [dialog, setDialog] = useState<DialogState>({ kind: 'none' })
  const debouncedSearch = useDebouncedValue(search.trim())

  const query = { search: debouncedSearch || undefined, category: category ?? undefined, page, pageSize: PAGE_SIZE }
  const products = useQuery({
    queryKey: ['products', 'list', query],
    queryFn: () => api.searchProducts(query),
    placeholderData: keepPreviousData,
  })
  const stats = useQuery({ queryKey: ['products', 'stats'], queryFn: api.productStats })

  const total = products.data?.totalCount ?? 0
  const pageCount = Math.max(1, Math.ceil(total / PAGE_SIZE))
  const close = () => setDialog({ kind: 'none' })
  const filtered = Boolean(debouncedSearch || category)

  function chooseCategory(next: ProductCategory | null) {
    setCategory((current) => (current === next ? null : next))
    setPage(1)
  }

  return (
    <div className="grid gap-6">
      <PageHeader
        title="Product catalog"
        description="HVAC and electrical products that building models are checked against. Values are stored in SI units."
        actions={
          <>
            <Button variant="outline" onClick={() => setDialog({ kind: 'datasheet' })}>
              <FileTextIcon /> From datasheet
            </Button>
            <Button variant="outline" onClick={() => setDialog({ kind: 'import' })}>
              <FileUpIcon /> Import CSV
            </Button>
            <Button onClick={() => setDialog({ kind: 'edit', product: null })}>
              <PlusIcon /> Add product
            </Button>
          </>
        }
      />

      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6" role="group" aria-label="Filter by category">
        <CategoryTile
          label="All products"
          count={stats.data?.total}
          icon={LayersIcon}
          tint="bg-primary/10 text-primary"
          active={category === null}
          onClick={() => chooseCategory(null)}
        />
        {CATEGORIES.map((c) => (
          <CategoryTile
            key={c}
            label={CATEGORY_LABELS[c]}
            count={stats.data?.byCategory[c]}
            icon={CATEGORY_STYLES[c].icon}
            tint={CATEGORY_STYLES[c].tint}
            active={category === c}
            onClick={() => chooseCategory(c)}
          />
        ))}
      </div>

      <Card className="gap-0 overflow-hidden py-0">
        <div className="flex flex-wrap items-center gap-3 border-b px-4 py-3">
          <div className="relative w-full max-w-sm">
            <SearchIcon className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
            <Input
              placeholder="Search manufacturer, model or description"
              className="bg-background pl-8"
              value={search}
              onChange={(e) => {
                setSearch(e.target.value)
                setPage(1)
              }}
            />
          </div>
          {category && (
            <button
              type="button"
              onClick={() => chooseCategory(null)}
              className="inline-flex items-center gap-1 rounded-full border bg-muted px-2.5 py-1 text-xs font-medium hover:bg-accent"
            >
              {CATEGORY_LABELS[category]}
              <XIcon className="size-3" aria-label="Clear category filter" />
            </button>
          )}
          <span className="ml-auto text-sm text-muted-foreground tabular-nums">
            {products.data ? `${total} ${total === 1 ? 'product' : 'products'}` : ''}
          </span>
        </div>

        <Table>
          <TableHeader className="bg-muted/50">
            <TableRow className="hover:bg-transparent">
              <TableHead className="pl-4">Model</TableHead>
              <TableHead>Manufacturer</TableHead>
              <TableHead>Category</TableHead>
              <TableHead className="text-right">Airflow</TableHead>
              <TableHead className="text-right">Power</TableHead>
              <TableHead className="text-right">Connection</TableHead>
              <TableHead className="text-right">Weight</TableHead>
              <TableHead className="w-20 pr-4" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {products.isPending &&
              Array.from({ length: 6 }, (_, i) => (
                <TableRow key={i}>
                  <TableCell colSpan={8} className="px-4">
                    <Skeleton className="h-8 w-full" />
                  </TableCell>
                </TableRow>
              ))}

            {products.isError && (
              <TableRow className="hover:bg-transparent">
                <TableCell colSpan={8} className="py-14 text-center text-destructive">
                  {products.error.message}
                </TableCell>
              </TableRow>
            )}

            {products.data?.items.length === 0 && (
              <TableRow className="hover:bg-transparent">
                <TableCell colSpan={8} className="py-14">
                  <EmptyState
                    filtered={filtered}
                    onImport={() => setDialog({ kind: 'import' })}
                    onClear={() => {
                      setSearch('')
                      setCategory(null)
                    }}
                  />
                </TableCell>
              </TableRow>
            )}

            {products.data?.items.map((p) => (
              <TableRow key={p.id} className="group">
                <TableCell className="py-3 pl-4">
                  <div className="font-medium">{p.model}</div>
                  {p.description && (
                    <div className="max-w-xs truncate text-xs text-muted-foreground" title={p.description}>
                      {p.description}
                    </div>
                  )}
                </TableCell>
                <TableCell className="text-muted-foreground">{p.manufacturer}</TableCell>
                <TableCell>
                  <CategoryBadge category={p.category} />
                </TableCell>
                <TableCell className="text-right">
                  <Measure value={p.airflowLps} unit="l/s" />
                </TableCell>
                <TableCell className="text-right">
                  <Measure value={p.powerW} unit="W" />
                </TableCell>
                <TableCell className="text-right">
                  <Measure value={p.connectionSizeMm} unit="mm" prefix="Ø" />
                </TableCell>
                <TableCell className="text-right">
                  <Measure value={p.weightKg} unit="kg" />
                </TableCell>
                <TableCell className="pr-4">
                  <div className="flex justify-end gap-0.5 transition-opacity focus-within:opacity-100 md:opacity-0 md:group-hover:opacity-100">
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      aria-label={`Edit ${p.model}`}
                      onClick={() => setDialog({ kind: 'edit', product: p })}
                    >
                      <PencilIcon />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      className="hover:text-destructive"
                      aria-label={`Delete ${p.model}`}
                      onClick={() => setDialog({ kind: 'delete', product: p })}
                    >
                      <Trash2Icon />
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>

        {total > 0 && (
          <div className="flex items-center justify-between border-t px-4 py-3 text-sm text-muted-foreground">
            <span className="tabular-nums">
              {(page - 1) * PAGE_SIZE + 1}–{Math.min(page * PAGE_SIZE, total)} of {total}
            </span>
            <div className="flex items-center gap-1">
              <Button
                variant="ghost"
                size="icon-sm"
                aria-label="Previous page"
                disabled={page <= 1}
                onClick={() => setPage((p) => p - 1)}
              >
                <ChevronLeftIcon />
              </Button>
              <span className="px-2 tabular-nums">
                Page {page} of {pageCount}
              </span>
              <Button
                variant="ghost"
                size="icon-sm"
                aria-label="Next page"
                disabled={page >= pageCount}
                onClick={() => setPage((p) => p + 1)}
              >
                <ChevronRightIcon />
              </Button>
            </div>
          </div>
        )}
      </Card>

      {dialog.kind === 'edit' && <ProductDialog product={dialog.product} onClose={close} />}
      {dialog.kind === 'delete' && <DeleteProductDialog product={dialog.product} onClose={close} />}
      {dialog.kind === 'import' && <ImportDialog onClose={close} />}
      {dialog.kind === 'datasheet' && <DatasheetDialog onClose={close} />}
    </div>
  )
}

function CategoryTile({
  label,
  count,
  icon: Icon,
  tint,
  active,
  onClick,
}: {
  label: string
  count: number | undefined
  icon: React.ComponentType<{ className?: string }>
  tint: string
  active: boolean
  onClick: () => void
}) {
  return (
    <button
      type="button"
      aria-pressed={active}
      onClick={onClick}
      className={cn(
        // Compact rows on phones, stacked tiles from small tablets up.
        'flex items-center gap-3 rounded-xl border bg-card p-3 text-left shadow-xs transition-all sm:flex-col sm:items-start sm:p-3.5',
        'hover:border-primary/40 hover:shadow-sm focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none',
        active && 'border-primary ring-1 ring-primary',
      )}
    >
      <span className={cn('flex size-8 shrink-0 items-center justify-center rounded-lg', tint)}>
        <Icon className="size-4" />
      </span>
      <span className="grid min-w-0 gap-0.5">
        <span className="text-lg leading-none font-semibold tabular-nums sm:text-xl">{count ?? '–'}</span>
        <span className="truncate text-xs text-muted-foreground">{label}</span>
      </span>
    </button>
  )
}

/** A number with its unit in a quieter style, e.g. "50 l/s". Shows a dash when the value is missing. */
function Measure({ value, unit, prefix }: { value: number | null; unit: string; prefix?: string }) {
  if (value === null || value === undefined) return <span className="text-muted-foreground/60">—</span>
  return (
    <span className="whitespace-nowrap tabular-nums">
      {prefix}
      {formatNumber(value)}
      <span className="ml-1 text-xs text-muted-foreground">{unit}</span>
    </span>
  )
}

function EmptyState({ filtered, onImport, onClear }: { filtered: boolean; onImport: () => void; onClear: () => void }) {
  return (
    <div className="flex flex-col items-center gap-3 text-center">
      <span className="flex size-11 items-center justify-center rounded-full bg-muted">
        <PackageSearchIcon className="size-5 text-muted-foreground" />
      </span>
      <div className="grid gap-1">
        <p className="font-medium">{filtered ? 'No matching products' : 'The catalog is empty'}</p>
        <p className="text-sm text-muted-foreground">
          {filtered ? 'Try another search or category.' : 'Import a manufacturer CSV file to get started.'}
        </p>
      </div>
      {filtered ? (
        <Button variant="outline" size="sm" onClick={onClear}>
          Clear filters
        </Button>
      ) : (
        <Button size="sm" onClick={onImport}>
          <FileUpIcon /> Import CSV
        </Button>
      )}
    </div>
  )
}
