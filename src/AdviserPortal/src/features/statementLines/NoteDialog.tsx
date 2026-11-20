import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Textarea } from "@/components/ui/textarea";

/** D37: Ignore, Return to payer and Undo match ask for a required note in a dialog. */
export function NoteDialog({
  open,
  title,
  warning,
  busy,
  onConfirm,
  onClose,
}: {
  open: boolean;
  title: string;
  warning?: string;
  busy?: boolean;
  onConfirm: (note: string) => void;
  onClose: () => void;
}) {
  const [note, setNote] = useState("");
  const [missing, setMissing] = useState(false);

  function confirm() {
    const trimmed = note.trim();
    if (trimmed.length === 0) {
      setMissing(true);
      return;
    }
    onConfirm(trimmed);
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) {
          setNote("");
          setMissing(false);
          onClose();
        }
      }}
    >
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          {warning ? <DialogDescription>{warning}</DialogDescription> : null}
        </DialogHeader>
        <div className="flex flex-col gap-2">
          <label htmlFor="statement-line-note" className="text-sm font-medium">
            Note
          </label>
          <Textarea
            id="statement-line-note"
            value={note}
            maxLength={200}
            aria-invalid={missing}
            onChange={(event) => {
              setNote(event.target.value);
              setMissing(false);
            }}
          />
          {missing ? <p className="text-sm text-destructive">Enter a note.</p> : null}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button onClick={confirm} disabled={busy}>
            Confirm
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
