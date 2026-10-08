import { MonitorIcon, MoonIcon, SunIcon } from 'lucide-react'
import { useTheme } from 'next-themes'
import { cn } from '@/lib/utils'

const OPTIONS = [
  { value: 'light', label: 'Light', icon: SunIcon },
  { value: 'dark', label: 'Dark', icon: MoonIcon },
  { value: 'system', label: 'System', icon: MonitorIcon },
] as const

/** Three-way light / dark / system switch, sized for the dark sidebar. */
export function ThemeToggle() {
  const { theme = 'system', setTheme } = useTheme()

  return (
    <div className="flex rounded-lg bg-sidebar-accent p-0.5" role="radiogroup" aria-label="Color theme">
      {OPTIONS.map(({ value, label, icon: Icon }) => (
        <button
          key={value}
          type="button"
          role="radio"
          aria-checked={theme === value}
          aria-label={label}
          title={label}
          onClick={() => setTheme(value)}
          className={cn(
            'flex flex-1 items-center justify-center rounded-md py-1 text-sidebar-muted transition-colors hover:text-sidebar-foreground',
            theme === value && 'bg-sidebar text-sidebar-foreground shadow-sm',
          )}
        >
          <Icon className="size-3.5" />
        </button>
      ))}
    </div>
  )
}
