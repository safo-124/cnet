import { Navigate, NavLink, Route, Routes } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { BoxesIcon, HistoryIcon, ScanSearchIcon, type LucideIcon } from 'lucide-react'
import { ThemeCycleButton, ThemeToggle } from '@/components/ThemeToggle'
import { AuditPage } from '@/features/audit/AuditPage'
import { CatalogPage } from '@/features/catalog/CatalogPage'
import { HistoryPage } from '@/features/history/HistoryPage'
import { ModelHistoryPage } from '@/features/history/ModelHistoryPage'
import { api } from '@/lib/api'
import { cn } from '@/lib/utils'

const NAV: { to: string; label: string; hint: string; icon: LucideIcon }[] = [
  { to: '/catalog', label: 'Catalog', hint: 'Products and data', icon: BoxesIcon },
  { to: '/audit', label: 'Model audit', hint: 'Check IFC models', icon: ScanSearchIcon },
  { to: '/history', label: 'History', hint: 'Models over time', icon: HistoryIcon },
]

export default function App() {
  return (
    <div className="flex min-h-svh">
      {/* The aside stretches with the page so its background never ends; the inner column stays pinned. */}
      <aside className="hidden w-64 shrink-0 bg-sidebar text-sidebar-foreground md:block">
        <div className="sticky top-0 flex h-svh flex-col">
          <div className="flex h-16 items-center px-5">
            <Brand />
          </div>

          <nav className="grid gap-1 px-3 py-2" aria-label="Main">
            <p className="px-3 pb-1 text-[11px] font-medium tracking-wider text-sidebar-muted uppercase">Workspace</p>
            {NAV.map(({ to, label, hint, icon: Icon }) => (
              <NavLink
                key={to}
                to={to}
                className={({ isActive }) =>
                  cn(
                    'group relative flex items-center gap-3 rounded-lg px-3 py-2 transition-colors',
                    isActive
                      ? 'bg-sidebar-accent text-sidebar-accent-foreground'
                      : 'text-sidebar-muted hover:bg-sidebar-accent/60 hover:text-sidebar-foreground',
                  )
                }
              >
                {({ isActive }) => (
                  <>
                    {isActive && <span className="absolute inset-y-2 left-0 w-0.5 rounded-full bg-sidebar-primary" />}
                    <Icon className={cn('size-4', isActive && 'text-sidebar-primary')} />
                    <span className="grid leading-tight">
                      <span className="text-sm font-medium">{label}</span>
                      <span className="text-[11px] text-sidebar-muted">{hint}</span>
                    </span>
                  </>
                )}
              </NavLink>
            ))}
          </nav>

          <div className="mt-auto grid gap-3 border-t border-sidebar-border p-4">
            <ApiStatus />
            <ThemeToggle />
          </div>
        </div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        {/* Phones and small tablets: a slim top bar, and the navigation as a bottom tab bar within thumb reach. */}
        <header className="sticky top-0 z-30 flex h-14 items-center gap-3 bg-sidebar px-4 text-sidebar-foreground md:hidden">
          <Brand />
          <div className="ml-auto flex items-center gap-1">
            <ApiDot />
            <ThemeCycleButton />
          </div>
        </header>

        <main className="mx-auto w-full max-w-6xl px-4 pt-6 pb-[calc(5.5rem+env(safe-area-inset-bottom))] sm:px-6 md:py-8 lg:px-10 lg:py-10">
          <Routes>
            <Route path="/" element={<Navigate to="/catalog" replace />} />
            <Route path="/catalog" element={<CatalogPage />} />
            <Route path="/audit" element={<AuditPage />} />
            <Route path="/history" element={<HistoryPage />} />
            <Route path="/history/:projectGlobalId" element={<ModelHistoryPage />} />
            <Route path="*" element={<Navigate to="/catalog" replace />} />
          </Routes>
        </main>
      </div>

      <nav
        className="fixed inset-x-0 bottom-0 z-30 grid grid-cols-3 border-t border-sidebar-border bg-sidebar pb-[env(safe-area-inset-bottom)] text-sidebar-foreground md:hidden"
        aria-label="Main"
      >
        {NAV.map(({ to, label, icon: Icon }) => (
          <NavLink
            key={to}
            to={to}
            className={({ isActive }) =>
              cn(
                'relative flex flex-col items-center gap-1 py-2.5 text-[11px] font-medium transition-colors',
                isActive ? 'text-sidebar-primary' : 'text-sidebar-muted active:text-sidebar-foreground',
              )
            }
          >
            {({ isActive }) => (
              <>
                {isActive && <span className="absolute inset-x-6 top-0 h-0.5 rounded-full bg-sidebar-primary" />}
                <Icon className="size-5" />
                {label}
              </>
            )}
          </NavLink>
        ))}
      </nav>
    </div>
  )
}

function Brand() {
  return (
    <div className="flex items-center gap-2.5">
      <LogoMark />
      <div className="grid leading-tight">
        <span className="text-sm font-semibold tracking-tight">MepCatalog</span>
        <span className="hidden text-[11px] text-sidebar-muted md:block">Building services data</span>
      </div>
    </div>
  )
}

/** A duct cross-section with airflow lines: simple, and specific to the domain. */
function LogoMark() {
  return (
    <svg viewBox="0 0 32 32" className="size-8 shrink-0" aria-hidden="true">
      <rect width="32" height="32" rx="8" className="fill-sidebar-primary" />
      <circle cx="16" cy="16" r="8.5" fill="none" strokeWidth="2.2" className="stroke-sidebar-primary-foreground" />
      <path
        d="M11.5 14h9M11.5 18h6"
        strokeWidth="2.2"
        strokeLinecap="round"
        className="stroke-sidebar-primary-foreground"
      />
    </svg>
  )
}

/** The phone version of the API status: just the dot, with the details for screen readers and on long-press. */
function ApiDot() {
  const stats = useQuery({ queryKey: ['products', 'stats'], queryFn: api.productStats, retry: false })
  const label = stats.isPending ? 'Connecting to the API' : stats.isSuccess ? `API connected, ${stats.data.total} products` : 'API offline'
  return (
    <span className="flex size-9 items-center justify-center" title={label} role="status" aria-label={label}>
      <span className={cn('size-2 rounded-full', stats.isSuccess ? 'bg-emerald-400' : stats.isPending ? 'bg-sidebar-muted' : 'bg-red-400')} />
    </span>
  )
}

/** Shows whether the API is reachable, using the same stats query as the catalog page. */
function ApiStatus() {
  const stats = useQuery({ queryKey: ['products', 'stats'], queryFn: api.productStats, retry: false })
  const online = stats.isSuccess

  return (
    <div className="flex items-center gap-2 px-1 text-xs text-sidebar-muted">
      <span className="relative flex size-2">
        {online && <span className="absolute inline-flex size-full animate-ping rounded-full bg-emerald-400 opacity-40" />}
        <span className={cn('relative inline-flex size-2 rounded-full', online ? 'bg-emerald-400' : 'bg-red-400')} />
      </span>
      {stats.isPending ? 'Connecting…' : online ? `API connected · ${stats.data.total} products` : 'API offline'}
    </div>
  )
}
