import { useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { ApiError, CATEGORIES, CATEGORY_LABELS, api, type Product, type ProductCategory, type ProductInput } from '@/lib/api'

const categoryItems = CATEGORIES.map((value) => ({ value, label: CATEGORY_LABELS[value] }))

type NumberField = 'airflowLps' | 'powerW' | 'connectionSizeMm' | 'weightKg'

const numberFields: { key: NumberField; label: string; unit: string; step: string }[] = [
  { key: 'airflowLps', label: 'Airflow', unit: 'l/s', step: '0.1' },
  { key: 'powerW', label: 'Power', unit: 'W', step: '0.1' },
  { key: 'connectionSizeMm', label: 'Connection size', unit: 'mm', step: '1' },
  { key: 'weightKg', label: 'Weight', unit: 'kg', step: '0.01' },
]

interface FormState {
  manufacturer: string
  model: string
  category: ProductCategory | null
  description: string
  airflowLps: string
  powerW: string
  connectionSizeMm: string
  weightKg: string
}

function toFormState(product: ProductInput | null): FormState {
  const text = (v: number | null | undefined) => (v === null || v === undefined ? '' : String(v))
  return {
    manufacturer: product?.manufacturer ?? '',
    model: product?.model ?? '',
    category: product?.category ?? null,
    description: product?.description ?? '',
    airflowLps: text(product?.airflowLps),
    powerW: text(product?.powerW),
    connectionSizeMm: text(product?.connectionSizeMm),
    weightKg: text(product?.weightKg),
  }
}

function toInput(form: FormState): ProductInput {
  const number = (v: string) => (v.trim() === '' ? null : Number(v))
  return {
    manufacturer: form.manufacturer,
    model: form.model,
    // The API rejects a missing category with a clear validation message.
    category: form.category ?? ('Unknown' as ProductCategory),
    description: form.description || null,
    airflowLps: number(form.airflowLps),
    powerW: number(form.powerW),
    connectionSizeMm: number(form.connectionSizeMm),
    weightKg: number(form.weightKg),
  }
}

interface Props {
  /** The product to edit, or null to create a new one. */
  product: Product | null
  /** Starting values for a new product, e.g. from a datasheet. */
  draft?: ProductInput | null
  title?: string
  onClose: () => void
  onSaved?: (saved: Product) => void
}

export function ProductDialog({ product, draft, title, onClose, onSaved }: Props) {
  const queryClient = useQueryClient()
  const [form, setForm] = useState(() => toFormState(product ?? draft ?? null))
  const set = <K extends keyof FormState>(key: K, value: FormState[K]) => setForm((f) => ({ ...f, [key]: value }))

  const save = useMutation({
    mutationFn: (input: ProductInput) => (product ? api.updateProduct(product.id, input) : api.createProduct(input)),
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: ['products'] })
      toast.success(`${saved.manufacturer} ${saved.model} ${product ? 'updated' : 'added'}`)
      onSaved?.(saved)
      onClose()
    },
  })

  const error = save.error instanceof ApiError ? save.error : null
  const fieldError = (key: string) => error?.fieldErrors[key]?.[0]

  function submit(event: FormEvent) {
    event.preventDefault()
    save.mutate(toInput(form))
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{title ?? (product ? `Edit ${product.manufacturer} ${product.model}` : 'Add product')}</DialogTitle>
        </DialogHeader>

        <form id="product-form" onSubmit={submit} className="grid gap-4">
          <div className="grid grid-cols-2 gap-3">
            <Field label="Manufacturer" error={fieldError('manufacturer')}>
              <Input value={form.manufacturer} onChange={(e) => set('manufacturer', e.target.value)} autoFocus />
            </Field>
            <Field label="Model" error={fieldError('model')}>
              <Input value={form.model} onChange={(e) => set('model', e.target.value)} />
            </Field>
          </div>

          <Field label="Category" error={fieldError('category')}>
            <Select
              items={categoryItems}
              value={form.category}
              onValueChange={(value) => set('category', value as ProductCategory | null)}
            >
              <SelectTrigger className="w-full">
                <SelectValue placeholder="Choose a category" />
              </SelectTrigger>
              <SelectContent>
                {categoryItems.map((item) => (
                  <SelectItem key={item.value} value={item.value}>
                    {item.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Field>

          <Field label="Description">
            <Input value={form.description} onChange={(e) => set('description', e.target.value)} />
          </Field>

          <div className="grid grid-cols-2 gap-3">
            {numberFields.map(({ key, label, unit, step }) => (
              <Field key={key} label={`${label} (${unit})`} error={fieldError(key)}>
                <Input
                  type="number"
                  inputMode="decimal"
                  step={step}
                  min="0"
                  value={form[key]}
                  onChange={(e) => set(key, e.target.value)}
                />
              </Field>
            ))}
          </div>

          {error && Object.keys(error.fieldErrors).length === 0 && (
            <p className="text-sm text-destructive">{error.message}</p>
          )}
        </form>

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="product-form" disabled={save.isPending}>
            {save.isPending ? 'Saving…' : 'Save'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function Field({ label, error, children }: { label: string; error?: string; children: React.ReactNode }) {
  return (
    <div className="grid gap-1.5">
      <Label>{label}</Label>
      {children}
      {error && <p className="text-xs text-destructive">{error}</p>}
    </div>
  )
}
