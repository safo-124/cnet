import { useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { FileUpIcon, PencilIcon, PlusIcon, SearchIcon, Trash2Icon } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { CATEGORIES, CATEGORY_LABELS, api, type Product, type ProductCategory } from '@/lib/api'
import { formatNumber } from '@/lib/format'
import { useDebouncedValue } from '@/lib/hooks'
import { DeleteProductDialog } from './DeleteProductDialog'
import { ImportDialog } from './ImportDialog'
import { ProductDialog } from './ProductDialog'

const PAGE_SIZE = 20
const ALL = 'all'
const categoryFilterItems = [
  { value: ALL, label: 'All categories' },
  ...CATEGORIES.map((value) => ({ value, label: CATEGORY_LABELS[value] })),
]

type DialogState =
  | { kind: 'none' }
  | { kind: 'edit'; product: Product | null }
  | { kind: 'delete'; product: Product }
  | { kind: 'import' }

export function CatalogPage() {
  const [search, setSearch] = useState('')
  const [category, setCategory] = useState<string>(ALL)
  const [page, setPage] = useState(1)
  const [dialog, setDialog] = useState<DialogState>({ kind: 'none' })
  const debouncedSearch = useDebouncedValue(search.trim())

  const query = {
    search: debouncedSearch || undefined,
    category: category === ALL ? undefined : (category as ProductCategory),
    page,
    pageSize: PAGE_SIZE,
  }
  const products = useQuery({
    queryKey: ['products', query],
    queryFn: () => api.searchProducts(query),
    placeholderData: keepPreviousData,
  })

  const total = products.data?.totalCount ?? 0
  const pageCount = Math.max(1, Math.ceil(total / PAGE_SIZE))
  const close = () => setDialog({ kind: 'none' })

  return (
    <div className="grid gap-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Product catalog</h1>
          <p className="text-muted-foreground">HVAC and electrical products used to fill in device data in building models.</p>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" onClick={() => setDialog({ kind: 'import' })}>
            <FileUpIcon /> Import CSV
          </Button>
          <Button onClick={() => setDialog({ kind: 'edit', product: null })}>
            <PlusIcon /> Add product
          </Button>
        </div>
      </div>

      <div className="flex flex-wrap gap-3">
        <div className="relative w-full max-w-sm">
          <SearchIcon className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            placeholder="Search manufacturer, model or description"
            className="pl-8"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value)
              setPage(1)
            }}
          />
        </div>
        <Select
          items={categoryFilterItems}
          value={category}
          onValueChange={(value) => {
            setCategory(value ?? ALL)
            setPage(1)
          }}
        >
          <SelectTrigger className="w-52">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {categoryFilterItems.map((item) => (
              <SelectItem key={item.value} value={item.value}>
                {item.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="rounded-xl border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Manufacturer</TableHead>
              <TableHead>Model</TableHead>
              <TableHead>Category</TableHead>
              <TableHead className="text-right">Airflow</TableHead>
              <TableHead className="text-right">Power</TableHead>
              <TableHead className="text-right">Connection</TableHead>
              <TableHead className="text-right">Weight</TableHead>
              <TableHead className="w-24" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {products.isPending &&
              Array.from({ length: 5 }, (_, i) => (
                <TableRow key={i}>
                  <TableCell colSpan={8}>
                    <Skeleton className="h-5 w-full" />
                  </TableCell>
                </TableRow>
              ))}

            {products.isError && (
              <TableRow>
                <TableCell colSpan={8} className="py-10 text-center text-destructive">
                  {products.error.message}
                </TableCell>
              </TableRow>
            )}

            {products.data?.items.length === 0 && (
              <TableRow>
                <TableCell colSpan={8} className="py-10 text-center text-muted-foreground">
                  No products found. Try another search, or import a CSV file.
                </TableCell>
              </TableRow>
            )}

            {products.data?.items.map((p) => (
              <TableRow key={p.id}>
                <TableCell>{p.manufacturer}</TableCell>
                <TableCell className="font-medium">
                  {p.model}
                  {p.description && <div className="text-xs font-normal text-muted-foreground">{p.description}</div>}
                </TableCell>
                <TableCell>
                  <Badge variant="secondary">{CATEGORY_LABELS[p.category]}</Badge>
                </TableCell>
                <TableCell className="text-right tabular-nums">{formatNumber(p.airflowLps, 'l/s')}</TableCell>
                <TableCell className="text-right tabular-nums">{formatNumber(p.powerW, 'W')}</TableCell>
                <TableCell className="text-right tabular-nums">
                  {p.connectionSizeMm ? `Ø${p.connectionSizeMm}` : '—'}
                </TableCell>
                <TableCell className="text-right tabular-nums">{formatNumber(p.weightKg, 'kg')}</TableCell>
                <TableCell>
                  <div className="flex justify-end gap-1">
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
      </div>

      <div className="flex items-center justify-between text-sm text-muted-foreground">
        <span>
          {total === 0
            ? 'No products'
            : `Showing ${(page - 1) * PAGE_SIZE + 1}–${Math.min(page * PAGE_SIZE, total)} of ${total}`}
        </span>
        <div className="flex items-center gap-2">
          <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
            Previous
          </Button>
          <span>
            Page {page} of {pageCount}
          </span>
          <Button variant="outline" size="sm" disabled={page >= pageCount} onClick={() => setPage((p) => p + 1)}>
            Next
          </Button>
        </div>
      </div>

      {dialog.kind === 'edit' && <ProductDialog product={dialog.product} onClose={close} />}
      {dialog.kind === 'delete' && <DeleteProductDialog product={dialog.product} onClose={close} />}
      {dialog.kind === 'import' && <ImportDialog onClose={close} />}
    </div>
  )
}
