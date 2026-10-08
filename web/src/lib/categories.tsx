import { AirVentIcon, BlindsIcon, FanIcon, LightbulbIcon, WindIcon, type LucideIcon } from 'lucide-react'
import { CATEGORY_LABELS, type ProductCategory } from '@/lib/api'
import { cn } from '@/lib/utils'

interface CategoryStyle {
  icon: LucideIcon
  /** Soft tinted background + matching text, for badges and icon tiles. */
  tint: string
}

export const CATEGORY_STYLES: Record<ProductCategory, CategoryStyle> = {
  SupplyAirTerminal: { icon: AirVentIcon, tint: 'bg-sky-50 text-sky-700 dark:bg-sky-950/60 dark:text-sky-300' },
  ExhaustAirTerminal: { icon: WindIcon, tint: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-950/60 dark:text-indigo-300' },
  Fan: { icon: FanIcon, tint: 'bg-teal-50 text-teal-700 dark:bg-teal-950/60 dark:text-teal-300' },
  Damper: { icon: BlindsIcon, tint: 'bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-300' },
  LightFixture: { icon: LightbulbIcon, tint: 'bg-amber-50 text-amber-700 dark:bg-amber-950/60 dark:text-amber-300' },
}

export function CategoryBadge({ category, className }: { category: ProductCategory; className?: string }) {
  const { icon: Icon, tint } = CATEGORY_STYLES[category]
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-md px-2 py-0.5 text-xs font-medium whitespace-nowrap',
        tint,
        className,
      )}
    >
      <Icon className="size-3.5" />
      {CATEGORY_LABELS[category]}
    </span>
  )
}
