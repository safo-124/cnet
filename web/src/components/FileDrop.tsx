import { useRef, useState, type DragEvent } from 'react'
import { UploadIcon } from 'lucide-react'
import { cn } from '@/lib/utils'

interface Props {
  accept: string
  onFile: (file: File) => void
  title: string
  hint?: string
  disabled?: boolean
  className?: string
}

/** Drag-and-drop area that also opens the file picker on click or keyboard. */
export function FileDrop({ accept, onFile, title, hint, disabled, className }: Props) {
  const input = useRef<HTMLInputElement>(null)
  const [dragging, setDragging] = useState(false)

  function drop(event: DragEvent) {
    event.preventDefault()
    setDragging(false)
    const file = event.dataTransfer.files[0]
    if (file && !disabled) onFile(file)
  }

  return (
    <button
      type="button"
      disabled={disabled}
      onClick={() => input.current?.click()}
      onDragOver={(e) => {
        e.preventDefault()
        setDragging(true)
      }}
      onDragLeave={() => setDragging(false)}
      onDrop={drop}
      className={cn(
        'flex w-full flex-col items-center justify-center gap-2 rounded-xl border-2 border-dashed p-8 text-center transition-colors',
        'hover:border-primary/50 hover:bg-muted/50 focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none',
        'disabled:cursor-not-allowed disabled:opacity-60',
        dragging && 'border-primary bg-muted',
        className,
      )}
    >
      <UploadIcon className="size-6 text-muted-foreground" />
      <span className="font-medium">{title}</span>
      {hint && <span className="text-sm text-muted-foreground">{hint}</span>}
      <input
        ref={input}
        type="file"
        accept={accept}
        className="hidden"
        onChange={(e) => {
          const file = e.target.files?.[0]
          if (file) onFile(file)
          e.target.value = ''
        }}
      />
    </button>
  )
}
