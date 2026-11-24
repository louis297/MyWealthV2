import { Link } from "react-router";
import { recordLinkClass, StatusBadge } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetAccountQuery } from "@/features/accounts/accountsApi";
import { transferStatusLabel } from "@/features/inKindTransfers/inKindTransferLabels";
import type { InKindTransfer } from "@/features/inKindTransfers/inKindTransfersApi";
import { CustomerName } from "@/features/nominatedBankAccounts/BankAccountChecksPage";
import { formatInstant } from "@/shared/time/instant";

function AccountName({ id }: { id: string }) {
  const { data } = useGetAccountQuery(id);
  return <>{data?.name ?? ""}</>;
}

/** D88 columns: Requested, Customer, Account, Direction, Instrument, Quantity, Counterparty, Status, Requested by. */
export function TransfersTable({ items, compact = false }: { items: InKindTransfer[]; compact?: boolean }) {
  return (
    <Card className="gap-0 py-0">
      <CardContent className="px-0">
        <Table aria-label="Transfers">
          <TableHeader>
            <TableRow>
              <TableHead>Requested</TableHead>
              {compact ? null : <TableHead>Customer</TableHead>}
              {compact ? null : <TableHead>Account</TableHead>}
              <TableHead>Direction</TableHead>
              <TableHead>Instrument</TableHead>
              <TableHead>Quantity</TableHead>
              <TableHead>Counterparty</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Requested by</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {items.map((item) => (
              <TableRow key={item.id}>
                <TableCell>
                  <Link to={`/transfers/${item.id}`} className={recordLinkClass}>
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
                <TableCell>{item.direction}</TableCell>
                <TableCell>{item.instrument.symbol}</TableCell>
                <TableCell>{item.quantity}</TableCell>
                <TableCell>{item.counterparty.name}</TableCell>
                <TableCell>
                  <StatusBadge status={transferStatusLabel(item.status)} />
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
