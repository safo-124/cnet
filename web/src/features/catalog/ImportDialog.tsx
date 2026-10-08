import { useMutation, useQueryClient } from '@tanstack/react-query'
import { CircleAlertIcon } from 'lucide-react'
import { toast } from 'sonner'
import { FileDrop } from '@/components/FileDrop'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { api } from '@/lib/api'

export function ImportDialog({ onClose }: { onClose: () => void }) {
  const queryClient = useQueryClient()
  const importCsv = useMutation({
    mutationFn: api.importProducts,
    onSuccess: (report) => {
      void queryClient.invalidateQueries({ queryKey: ['products'] })
      toast.success(`Imported: ${report.created} created, ${report.updated} updated`)
    },
  })
  const report = importCsv.data

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>Import manufacturer CSV</DialogTitle>
          <DialogDescription>
            Columns: Manufacturer, Model, Category, Description, Airflow, Power, Connection Size, Weight. Units like
            “180 m3/h”, “0,5 kW” or “Ø160” and Finnish category names are converted automatically.
          </DialogDescription>
        </DialogHeader>

        <FileDrop
          accept=".csv,text/csv"
          title={importCsv.isPending ? 'Importing…' : 'Drop a CSV file here or click to choose'}
          hint="Existing products (same manufacturer + model) are updated, not duplicated."
          disabled={importCsv.isPending}
          onFile={(file) => importCsv.mutate(file)}
        />

        {importCsv.error && (
          <Alert variant="destructive">
            <CircleAlertIcon />
            <AlertTitle>Import failed</AlertTitle>
            <AlertDescription>{importCsv.error.message}</AlertDescription>
          </Alert>
        )}

        {report && (
          <div className="grid gap-3">
            <div className="grid grid-cols-3 gap-3 text-center">
              <Stat label="Created" value={report.created} />
              <Stat label="Updated" value={report.updated} />
              <Stat label="Failed" value={report.failed.length} danger={report.failed.length > 0} />
            </div>
            {report.failed.length > 0 && (
              <div className="max-h-60 overflow-y-auto rounded-lg border">
                {report.failed.map((row) => (
                  <div key={row.rowNumber} className="border-b px-3 py-2 text-sm last:border-b-0">
                    <span className="font-medium">
                      Row {row.rowNumber}: {[row.manufacturer, row.model].filter(Boolean).join(' ') || '(no name)'}
                    </span>
                    <ul className="ml-4 list-disc text-muted-foreground">
                      {row.errors.map((e) => (
                        <li key={e}>{e}</li>
                      ))}
                    </ul>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </DialogContent>
    </Dialog>
  )
}

function Stat({ label, value, danger }: { label: string; value: number; danger?: boolean }) {
  return (
    <div className="rounded-lg border p-3">
      <div className={danger ? 'text-2xl font-semibold text-destructive' : 'text-2xl font-semibold'}>{value}</div>
      <div className="text-xs text-muted-foreground">{label}</div>
    </div>
  )
}
