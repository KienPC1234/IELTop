import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'

/// Confirms submit, and warns when questions are still blank, like the real
/// test. The timer auto submit never shows this.
export default function SubmitConfirm({ open, unanswered, onCancel, onConfirm }) {
  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v) onCancel?.() }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Submit test</DialogTitle>
          <DialogDescription>
            {unanswered === 0
              ? 'Submit the test now?'
              : `Submit now? ${unanswered} question(s) have no answer.`}
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button variant="outline" onClick={onCancel}>Cancel</Button>
          <Button onClick={onConfirm}>Submit</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
