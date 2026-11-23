import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Textarea } from "@/components/ui/textarea";
import { isRebalanceInProgress } from "@/features/rebalances/rebalanceLabels";
import { type Rebalance, useCancelRebalanceMutation } from "@/features/rebalances/rebalancesApi";
import { problemMessages } from "@/features/statementLines/statementLineLabels";

/** P18: cancel an open rebalance, with an optional note. A 400 shows its sentence; a 409 says it has changed. */
export function CancelRebalanceDialog({ rebalance, onClose }: {
  rebalance: Pick<Rebalance, "id" | "rowVersion" | "status">;
  onClose: () => void;
}) {
  const [cancel, { isLoading }] = useCancelRebalanceMutation();
  const [note, setNote] = useState("");
  const [messages, setMessages] = useState<string[]>([]);

  async function confirm() {
    try {
      await cancel({ id: rebalance.id, rowVersion: rebalance.rowVersion, note: note.trim() === "" ? null : note.trim() }).unwrap();
      onClose();
    } catch (error: unknown) {
      if (typeof error === "object" && error !== null && "status" in error && error.status === 409) {
        setMessages(["This rebalance has changed."]);
        return;
      }
      const result = problemMessages(error);
      const all = [...Object.values(result.fields).flat(), ...result.messages];
      setMessages(all.length > 0 ? all : ["The rebalance could not be cancelled."]);
    }
  }

  return (
    <Dialog open onOpenChange={(next) => { if (!next) { onClose(); } }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Cancel this rebalance</DialogTitle>
          {isRebalanceInProgress(rebalance.status) ? (
            <DialogDescription>Orders the broker has already filled stay.</DialogDescription>
          ) : null}
        </DialogHeader>
        <div className="flex flex-col gap-2">
          <label htmlFor="cancel-rebalance-note" className="text-sm font-medium">
            Note
          </label>
          <Textarea id="cancel-rebalance-note" value={note} maxLength={200} onChange={(event) => setNote(event.target.value)} />
          {messages.map((message) => (
            <p key={message} className="text-sm text-destructive">{message}</p>
          ))}
        </div>
        <DialogFooter>
          <Button type="button" variant="outline" onClick={onClose} disabled={isLoading}>Keep it</Button>
          <Button type="button" onClick={() => void confirm()} disabled={isLoading}>Cancel rebalance</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
