import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Textarea } from "@/components/ui/textarea";
import { type Order, useCancelOrderMutation } from "@/features/orders/ordersApi";
import { problemMessages } from "@/features/statementLines/statementLineLabels";

/** D73: cancel an open order, with an optional note. A 400 shows its sentence; a 409 says the order has changed. */
export function CancelOrderDialog({ order, onClose }: { order: Pick<Order, "id" | "rowVersion">; onClose: () => void }) {
  const [cancel, { isLoading }] = useCancelOrderMutation();
  const [note, setNote] = useState("");
  const [messages, setMessages] = useState<string[]>([]);

  async function confirm() {
    try {
      await cancel({ id: order.id, note: note.trim() === "" ? null : note.trim(), rowVersion: order.rowVersion }).unwrap();
      onClose();
    } catch (error: unknown) {
      if (typeof error === "object" && error !== null && "status" in error && error.status === 409) {
        // The cancel mutation has already invalidated the order, so the page shows it as it is now.
        setMessages(["This order has changed."]);
        return;
      }
      const result = problemMessages(error);
      const all = [...Object.values(result.fields).flat(), ...result.messages];
      setMessages(all.length > 0 ? all : ["The order could not be cancelled."]);
    }
  }

  return (
    <Dialog open onOpenChange={(next) => { if (!next) { onClose(); } }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Cancel this order</DialogTitle>
          <DialogDescription>If the broker has already filled part of this order, that part stays.</DialogDescription>
        </DialogHeader>
        <div className="flex flex-col gap-2">
          <label htmlFor="cancel-order-note" className="text-sm font-medium">
            Note
          </label>
          <Textarea id="cancel-order-note" value={note} maxLength={200} onChange={(event) => setNote(event.target.value)} />
          {messages.map((message) => (
            <p key={message} className="text-sm text-destructive">{message}</p>
          ))}
        </div>
        <DialogFooter>
          <Button type="button" variant="outline" onClick={onClose} disabled={isLoading}>Keep it</Button>
          <Button type="button" onClick={() => void confirm()} disabled={isLoading}>Cancel order</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
