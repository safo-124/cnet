// Typed client for the MepCatalog ASP.NET Core API. In development Vite proxies /api to the backend.

export const CATEGORIES = [
  'SupplyAirTerminal',
  'ExhaustAirTerminal',
  'Fan',
  'Damper',
  'LightFixture',
] as const

export type ProductCategory = (typeof CATEGORIES)[number]

export const CATEGORY_LABELS: Record<ProductCategory, string> = {
  SupplyAirTerminal: 'Supply air terminal',
  ExhaustAirTerminal: 'Exhaust air terminal',
  Fan: 'Fan',
  Damper: 'Damper',
  LightFixture: 'Light fixture',
}

export interface Product {
  id: number
  manufacturer: string
  model: string
  category: ProductCategory
  description: string | null
  airflowLps: number | null
  powerW: number | null
  connectionSizeMm: number | null
  weightKg: number | null
  updatedUtc: string
}

export type ProductInput = Omit<Product, 'id' | 'updatedUtc'>

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

export interface ProductStats {
  total: number
  byCategory: Record<ProductCategory, number>
}

export interface ImportRowError {
  rowNumber: number
  manufacturer: string | null
  model: string | null
  errors: string[]
}

export interface ImportReport {
  created: number
  updated: number
  failed: ImportRowError[]
}

export type AuditStatus = 'Ok' | 'NeedsUpdate' | 'Unidentified' | 'NotInCatalog' | 'CategoryMismatch'

export interface FieldChange {
  field: 'AirflowLps' | 'PowerW' | 'ConnectionSizeMm' | 'WeightKg'
  modelValue: number | null
  catalogValue: number
}

export interface AuditDevice {
  id: string
  name: string | null
  elementType: string
  level: string | null
  manufacturer: string | null
  model: string | null
  status: AuditStatus
  message: string
  productId: number | null
  changes: FieldChange[]
}

export interface AuditResponse {
  fileName: string
  summary: {
    total: number
    ok: number
    needsUpdate: number
    unidentified: number
    notInCatalog: number
    categoryMismatch: number
  }
  devices: AuditDevice[]
}

export interface DatasheetStatus {
  enabled: boolean
  model: string | null
  /** Why the feature is off, e.g. on the public demo. */
  disabledReason: string | null
}

/** Sample files served by the API, so visitors without their own data can try everything. */
export const SAMPLES = {
  model: '/samples/sample-building.ifc',
  products: '/samples/sample-products.csv',
  datasheet: '/samples/datasheets/nordic-air-ka-series.pdf',
}

export interface ExtractedProduct {
  /** Values exactly as printed in the datasheet. */
  raw: {
    manufacturer: string | null
    model: string | null
    category: string | null
    description: string | null
    airflow: string | null
    power: string | null
    connectionSize: string | null
    weight: string | null
  }
  /** Normalized values, or null when something could not be read. */
  product: ProductInput | null
  issues: string[]
  existingProductId: number | null
  /** Every value that could be read; category may be 'Unknown'. Starting point for a manual fix. */
  draft: Omit<ProductInput, 'category'> & { category: ProductCategory | 'Unknown' }
}

export interface DatasheetResponse {
  fileName: string
  notes: string | null
  products: ExtractedProduct[]
}

export class ApiError extends Error {
  readonly status: number
  readonly fieldErrors: Record<string, string[]>

  constructor(status: number, message: string, fieldErrors: Record<string, string[]> = {}) {
    super(message)
    this.status = status
    this.fieldErrors = fieldErrors
  }
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  let response: Response
  try {
    response = await fetch(url, init)
  } catch {
    throw new ApiError(0, 'Cannot reach the API. Is the backend running on port 5236?')
  }

  if (response.status === 204) return undefined as T
  if (response.ok) return (await response.json()) as T
  throw await toApiError(response)
}

/** Turns plain-string errors, ProblemDetails and validation problems from ASP.NET Core into one error type. */
async function toApiError(response: Response): Promise<ApiError> {
  const body: unknown = await response.json().catch(() => null)
  if (typeof body === 'string') return new ApiError(response.status, body)
  if (body && typeof body === 'object') {
    const problem = body as { title?: string; detail?: string; errors?: Record<string, string[]> }
    if (problem.errors) {
      // Field names come back PascalCase ("AirflowLps"); the form uses camelCase.
      const fieldErrors = Object.fromEntries(
        Object.entries(problem.errors).map(([k, v]) => [k.charAt(0).toLowerCase() + k.slice(1), v]),
      )
      return new ApiError(response.status, Object.values(problem.errors).flat().join(' '), fieldErrors)
    }
    return new ApiError(response.status, problem.detail ?? problem.title ?? response.statusText)
  }
  return new ApiError(response.status, `${response.status} ${response.statusText}`)
}

const json = (data: unknown): RequestInit => ({
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(data),
})

const formWithFile = (file: File): FormData => {
  const form = new FormData()
  form.append('file', file)
  return form
}

export interface ProductQuery {
  search?: string
  category?: ProductCategory
  page: number
  pageSize: number
}

export const api = {
  searchProducts({ search, category, page, pageSize }: ProductQuery) {
    const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
    if (search) params.set('search', search)
    if (category) params.set('category', category)
    return request<PagedResult<Product>>(`/api/products?${params}`)
  },

  productStats: () => request<ProductStats>('/api/products/stats'),

  createProduct: (input: ProductInput) =>
    request<Product>('/api/products', { method: 'POST', ...json(input) }),

  updateProduct: (id: number, input: ProductInput) =>
    request<Product>(`/api/products/${id}`, { method: 'PUT', ...json(input) }),

  deleteProduct: (id: number) => request<void>(`/api/products/${id}`, { method: 'DELETE' }),

  importProducts: (file: File) =>
    request<ImportReport>('/api/products/import', { method: 'POST', body: formWithFile(file) }),

  auditModel: (file: File) =>
    request<AuditResponse>('/api/audits', { method: 'POST', body: formWithFile(file) }),

  fixModel: (file: File) => postForFile('/api/audits/fix', file, 'model-fixed.ifc'),

  auditReport: (file: File) => postForFile('/api/audits/report', file, 'audit-report.xlsx'),

  datasheetStatus: () => request<DatasheetStatus>('/api/datasheets/status'),

  extractDatasheet: (file: File) =>
    request<DatasheetResponse>('/api/datasheets/extract', { method: 'POST', body: formWithFile(file) }),
}

export interface DownloadedFile {
  blob: Blob
  fileName: string
}

/** Uploads a file and returns the file the server sends back, named from its Content-Disposition header. */
async function postForFile(url: string, file: File, fallbackName: string): Promise<DownloadedFile> {
  let response: Response
  try {
    response = await fetch(url, { method: 'POST', body: formWithFile(file) })
  } catch {
    throw new ApiError(0, 'Cannot reach the API. Is the backend running on port 5236?')
  }
  if (!response.ok) throw await toApiError(response)
  const disposition = response.headers.get('Content-Disposition') ?? ''
  const fileName = /filename="?([^";]+)"?/.exec(disposition)?.[1] ?? fallbackName
  return { blob: await response.blob(), fileName }
}
