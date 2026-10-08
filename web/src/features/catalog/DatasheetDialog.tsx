import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CircleAlertIcon, FileTextIcon, InfoIcon, PencilIcon } from 'lucide-react'
import { toast } from 'sonner'
import { FileDrop } from '@/components/FileDrop'
import { StatusPill } from '@/components/StatusBadge'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { api, SAMPLES, type ExtractedProduct, type ProductCategory, type ProductInput } from '@/lib/api'
import { CategoryBadge } from '@/components/CategoryBadge'
import { formatNumber } from '@/lib/format'
import { ProductDialog } from './ProductDialog'

type RowState = 'pending' | 'saved'

/** The starting point for the edit form: every value that could be read, so only real gaps need typing. */
function draftFrom(item: ExtractedProduct): ProductInput {
  if (item.product) return item.product
  const { category, ...rest } = item.draft
  // The form shows "Choose a category" for a null category.
  return { ...rest, category: (category === 'Unknown' ? null : category) as ProductCategory }
}

export function DatasheetDialog({ onClose }: { onClose: () => void }) {
  const queryClient = useQueryClient()
  const status = useQuery({ queryKey: ['datasheet-status'], queryFn: api.datasheetStatus })
  const [selected, setSelected] = useState<Set<number>>(new Set())
  const [rowState, setRowState] = useState<Record<number, RowState>>({})
  const [editing, setEditing] = useState<number | null>(null)

  const extract = useMutation({
    mutationFn: api.extractDatasheet,
    onSuccess: (result) => {
      // Pre-select everything that was read cleanly; rows with issues need a look first.
      setSelected(new Set(result.products.flatMap((p, i) => (p.product ? [i] : []))))
      setRowState({})
    },
  })
  const products = extract.data?.products ?? []

  const save = useMutation({
    mutationFn: async (indexes: number[]) => {
      let saved = 0
      const failed: string[] = []
      for (const i of indexes) {
        const { product, existingProductId } = products[i]
        if (!product) continue
        try {
          if (existingProductId) await api.updateProduct(existingProductId, product)
          else await api.createProduct(product)
          setRowState((s) => ({ ...s, [i]: 'saved' }))
          saved++
        } catch (error) {
          failed.push(`${product.model}: ${(error as Error).message}`)
        }
      }
      return { saved, failed }
    },
    onSuccess: ({ saved, failed }) => {
      void queryClient.invalidateQueries({ queryKey: ['products'] })
      setSelected(new Set())
      if (saved > 0) toast.success(`${saved} product(s) saved to the catalog`)
      for (const message of failed) toast.error(message)
    },
  })

  const toggle = (i: number) =>
    setSelected((s) => {
      const next = new Set(s)
      if (next.has(i)) next.delete(i)
      else next.add(i)
      return next
    })

  const editingItem = editing === null ? null : products[editing]

  return (
    <>
      <Dialog open={editing === null} onOpenChange={(open) => !open && onClose()}>
        <DialogContent className="sm:max-w-4xl">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <FileTextIcon className="size-4" /> Add products from a datasheet
            </DialogTitle>
            <DialogDescription>
              Upload a manufacturer PDF. AI reads the product table, the values are converted to catalog units, and you
              review everything before it is saved.
            </DialogDescription>
          </DialogHeader>

          {status.data && !status.data.enabled && (
            <Alert>
              <InfoIcon />
              <AlertTitle>AI datasheet reading is off</AlertTitle>
              <AlertDescription>{status.data.disabledReason}</AlertDescription>
            </Alert>
          )}

          {status.data?.enabled && !extract.data && (
            <FileDrop
              accept=".pdf,application/pdf"
              disabled={extract.isPending}
              title={extract.isPending ? 'Reading the datasheet…' : 'Drop a PDF datasheet here or click to choose'}
              hint={extract.isPending ? 'This usually takes 10–40 seconds.' : 'Text-based PDFs up to 20 MB'}
              onFile={(file) => extract.mutate(file)}
            />
          )}

          {status.data && !extract.data && (
            <p className="text-sm text-muted-foreground">
              Sample:{' '}
              <a href={SAMPLES.datasheet} download className="font-medium text-primary underline-offset-4 hover:underline">
                Nordic Air KA series datasheet (PDF)
              </a>
            </p>
          )}

          {extract.error && (
            <Alert variant="destructive">
              <CircleAlertIcon />
              <AlertTitle>Could not read the datasheet</AlertTitle>
              <AlertDescription>{extract.error.message}</AlertDescription>
            </Alert>
          )}

          {extract.data && (
            <div className="grid gap-3">
              {extract.data.notes && (
                <Alert>
                  <InfoIcon />
                  <AlertTitle>Notes from the AI</AlertTitle>
                  <AlertDescription>{extract.data.notes}</AlertDescription>
                </Alert>
              )}

              {products.length === 0 && (
                <p className="py-6 text-center text-muted-foreground">No products were found in this document.</p>
              )}

              <div className="max-h-[50vh] divide-y overflow-y-auto rounded-lg border">
                {products.map((item, i) => (
                  <ExtractedRow
                    key={i}
                    item={item}
                    checked={selected.has(i)}
                    state={rowState[i] ?? 'pending'}
                    onToggle={() => toggle(i)}
                    onEdit={() => setEditing(i)}
                  />
                ))}
              </div>
            </div>
          )}

          {extract.data && (
            <DialogFooter>
              <Button variant="outline" onClick={() => extract.reset()} disabled={save.isPending}>
                Read another datasheet
              </Button>
              <Button
                disabled={selected.size === 0 || save.isPending}
                onClick={() => save.mutate([...selected].sort((a, b) => a - b))}
              >
                {save.isPending ? 'Saving…' : `Save ${selected.size} product(s)`}
              </Button>
            </DialogFooter>
          )}
        </DialogContent>
      </Dialog>

      {editingItem && editing !== null && (
        <ProductDialog
          // A product that is already in the catalog is updated in place instead of duplicated.
          product={
            editingItem.existingProductId
              ? { ...draftFrom(editingItem), id: editingItem.existingProductId, updatedUtc: '' }
              : null
          }
          draft={draftFrom(editingItem)}
          title={`Review ${editingItem.raw.model ?? 'product'} from datasheet`}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setRowState((s) => ({ ...s, [editing]: 'saved' }))
            setSelected((s) => {
              const next = new Set(s)
              next.delete(editing)
              return next
            })
          }}
        />
      )}
    </>
  )
}

function ExtractedRow({
  item,
  checked,
  state,
  onToggle,
  onEdit,
}: {
  item: ExtractedProduct
  checked: boolean
  state: RowState
  onToggle: () => void
  onEdit: () => void
}) {
  const { product, raw, issues, existingProductId } = item
  const saved = state === 'saved'

  return (
    <div className="flex items-start gap-3 px-3 py-2.5">
      <input
        type="checkbox"
        className="mt-1 size-4 accent-primary"
        checked={checked}
        disabled={!product || saved}
        onChange={onToggle}
        aria-label={`Select ${raw.model ?? 'product'}`}
      />
      <div className="grid flex-1 gap-1">
        <div className="flex flex-wrap items-center gap-2">
          <span className="font-medium">{product?.model ?? raw.model ?? '(no model code)'}</span>
          <span className="text-sm text-muted-foreground">{product?.manufacturer ?? raw.manufacturer}</span>
          {product && <CategoryBadge category={product.category} />}
          {saved ? (
            <StatusPill tone="success">Saved</StatusPill>
          ) : issues.length > 0 ? (
            <StatusPill tone="warning">Needs review</StatusPill>
          ) : existingProductId ? (
            <Badge variant="outline">Updates existing</Badge>
          ) : (
            <Badge variant="outline">New</Badge>
          )}
        </div>

        <div className="flex flex-wrap gap-x-4 gap-y-0.5 text-sm tabular-nums">
          <Value label="Airflow" value={product ? formatNumber(product.airflowLps, 'l/s') : null} printed={raw.airflow} />
          <Value label="Power" value={product ? formatNumber(product.powerW, 'W') : null} printed={raw.power} />
          <Value
            label="Connection"
            value={product?.connectionSizeMm ? `Ø${product.connectionSizeMm}` : product ? '—' : null}
            printed={raw.connectionSize}
          />
          <Value label="Weight" value={product ? formatNumber(product.weightKg, 'kg') : null} printed={raw.weight} />
        </div>

        {issues.length > 0 && (
          <ul className="ml-4 list-disc text-xs text-amber-700 dark:text-amber-400">
            {issues.map((issue) => (
              <li key={issue}>{issue}</li>
            ))}
          </ul>
        )}
      </div>
      {!saved && (
        <Button variant="ghost" size="sm" onClick={onEdit}>
          <PencilIcon /> {product ? 'Edit' : 'Fix'}
        </Button>
      )}
    </div>
  )
}

/** Shows the converted value, with the value as printed in the datasheet underneath for checking. */
function Value({ label, value, printed }: { label: string; value: string | null; printed: string | null }) {
  return (
    <span>
      <span className="text-muted-foreground">{label} </span>
      {value ?? printed ?? '—'}
      {printed && value && value !== '—' && value !== printed && <span className="text-xs text-muted-foreground"> (printed: {printed})</span>}
    </span>
  )
}
