import { Link } from "react-router";
import { recordLinkClass, StatusBadge } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetAccountQuery } from "@/features/accounts/accountsApi";
import {
  adjustmentStatusLabel,
  cashChangeText,
  holdingChangeText,
} from "@/features/adjustmentRequests/adjustmentRequestLabels";
import type { AdjustmentRequest } from "@/features/adjustmentRequests/adjustmentRequestsApi";
import { CustomerName } from "@/features/nominatedBankAccounts/BankAccountChecksPage";
import { adjustmentReasonLabel } from "@/features/transactions/ReverseTransactionButton";
import { formatInstant } from "@/shared/time/instant";

function AccountName({ id }: { id: string }) {
  const { data } = useGetAccountQuery(id);
  return <>{data?.name ?? ""}</>;
}

/** D103 columns: Requested, Customer, Account, Reason, Cash, Holding, Status, Requested by. */
export function AdjustmentRequestsTable({ items, compact = false }: { items: AdjustmentRequest[]; compact?: boolean }) {
  return (
    <Card className="gap-0 py-0">
      <CardContent className="px-0">
        <Table aria-label="Adjustments">
          <TableHeader>
            <TableRow>
              <TableHead>Requested</TableHead>
              {compact ? null : <TableHead>Customer</TableHead>}
              {compact ? null : <TableHead>Account</TableHead>}
              <TableHead>Reason</TableHead>
              <TableHead>Cash</TableHead>
              <TableHead>Holding</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Requested by</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {items.map((item) => (
              <TableRow key={item.id}>
                <TableCell>
                  <Link to={`/adjustments/${item.id}`} className={recordLinkClass}>
                    {formatInstant(item.requestedAt)}
                  </Link>
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
                <TableCell>{adjustmentReasonLabel(item.reason)}</TableCell>
                <TableCell>{cashChangeText(item.amount, item.currency)}</TableCell>
                <TableCell>{holdingChangeText(item.security)}</TableCell>
                <TableCell>
                  <StatusBadge status={adjustmentStatusLabel(item.status)} />
                </TableCell>
                <TableCell>{item.requestedBy.name}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}
