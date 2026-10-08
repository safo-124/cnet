import { CATEGORY_LABELS, type ProductCategory } from '@/lib/api'
import { CATEGORY_STYLES } from '@/lib/categories'
import { cn } from '@/lib/utils'

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
