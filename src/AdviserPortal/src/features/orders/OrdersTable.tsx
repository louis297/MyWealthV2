import { useState } from "react";
import { Link } from "react-router";
import { recordLinkClass, StatusBadge } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetAccountQuery } from "@/features/accounts/accountsApi";
import { CustomerName } from "@/features/nominatedBankAccounts/BankAccountChecksPage";
import { CancelOrderDialog } from "@/features/orders/CancelOrderDialog";
import { canCancelOrder, isOpenOrder, orderStatusLabel } from "@/features/orders/orderLabels";
import type { Order } from "@/features/orders/ordersApi";
import { formatInstant } from "@/shared/time/instant";

function AccountName({ id }: { id: string }) {
  const { data } = useGetAccountQuery(id);
  return <>{data?.name ?? ""}</>;
}

/** D71: an order's status, "Cancelling" while a cancel request waits, and "Needs attention" for a fill not booked. */
export function OrderStatus({ order, needsAttention }: { order: Order; needsAttention?: boolean }) {
  return (
    <span className="flex flex-wrap items-center gap-2">
      <StatusBadge status={orderStatusLabel(order.status)} />
      {order.cancelRequest && isOpenOrder(order) ? <span className="text-muted-foreground text-xs">Cancelling</span> : null}
      {needsAttention ? <span className="text-xs font-medium text-amber-600">Needs attention</span> : null}
    </span>
  );
}

/** D71 columns: Placed, Customer, Account, Side, Instrument, Quantity, Filled, Status, Placed by. */
export function OrdersTable({ items, attention, compact = false }: {
  items: Order[];
  /** The ids of the orders with a fill that is not booked yet. */
  attention?: ReadonlySet<string>;
  compact?: boolean;
}) {
  const [cancelling, setCancelling] = useState<Order | null>(null);
  return (
    <Card className="gap-0 py-0">
      <CardContent className="px-0">
        <Table aria-label="Orders">
          <TableHeader>
            <TableRow>
              <TableHead>Placed</TableHead>
              {compact ? null : <TableHead>Customer</TableHead>}
              {compact ? null : <TableHead>Account</TableHead>}
              <TableHead>Side</TableHead>
              <TableHead>Instrument</TableHead>
              <TableHead>Quantity</TableHead>
              <TableHead>Filled</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Placed by</TableHead>
              <TableHead>
                <span className="sr-only">Actions</span>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {items.map((item) => (
              <TableRow key={item.id}>
                <TableCell>
                  <Link to={`/orders/${item.id}`} className={recordLinkClass}>
                    {formatInstant(item.placedAt)}
                  </Link>
                  {item.rebalanceId ? (
                    <Link to={`/rebalances/${item.rebalanceId}`} className={`${recordLinkClass} block text-xs`}>
                      Rebalance
                    </Link>
                  ) : null}
                </TableCell>
                {compact ? null : (
                  <TableCell>
                    <CustomerName id={item.customerId} />
                  </TableCell>
                )}
                {compact ? null : (
                  <TableCell>
                    <AccountName id={item.accountId} />
                  </TableCell>
                )}
                <TableCell>{item.side}</TableCell>
                <TableCell>{item.instrument.symbol}</TableCell>
                <TableCell>{item.quantity}</TableCell>
                <TableCell>{item.filledQuantity}</TableCell>
                <TableCell>
                  <OrderStatus order={item} needsAttention={attention?.has(item.id)} />
                </TableCell>
                <TableCell>{item.placedBy.name}</TableCell>
                <TableCell>
                  {canCancelOrder(item) ? (
                    <Button type="button" size="sm" variant="outline" onClick={() => setCancelling(item)}>
                      Cancel
                    </Button>
                  ) : null}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </CardContent>
      {cancelling ? <CancelOrderDialog order={cancelling} onClose={() => setCancelling(null)} /> : null}
    </Card>
  );
}
