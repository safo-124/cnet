import { Navigate, NavLink, Route, Routes } from 'react-router'
import { BoxesIcon, ScanSearchIcon } from 'lucide-react'
import { AuditPage } from '@/features/audit/AuditPage'
import { CatalogPage } from '@/features/catalog/CatalogPage'
import { cn } from '@/lib/utils'

const NAV = [
  { to: '/catalog', label: 'Catalog', icon: BoxesIcon },
  { to: '/audit', label: 'Model audit', icon: ScanSearchIcon },
]

export default function App() {
  return (
    <div className="min-h-svh bg-background">
      <header className="border-b">
        <div className="mx-auto flex h-14 max-w-7xl items-center gap-6 px-4">
          <span className="font-semibold tracking-tight">MepCatalog</span>
          <nav className="flex gap-1">
            {NAV.map(({ to, label, icon: Icon }) => (
              <NavLink
                key={to}
                to={to}
                className={({ isActive }) =>
                  cn(
                    'flex items-center gap-2 rounded-lg px-3 py-1.5 text-sm transition-colors',
                    isActive ? 'bg-muted font-medium text-foreground' : 'text-muted-foreground hover:text-foreground',
                  )
                }
              >
                <Icon className="size-4" />
                {label}
              </NavLink>
            ))}
          </nav>
        </div>
      </header>

      <main className="mx-auto max-w-7xl px-4 py-8">
        <Routes>
          <Route path="/" element={<Navigate to="/catalog" replace />} />
          <Route path="/catalog" element={<CatalogPage />} />
          <Route path="/audit" element={<AuditPage />} />
          <Route path="*" element={<Navigate to="/catalog" replace />} />
        </Routes>
      </main>
    </div>
  )
}
