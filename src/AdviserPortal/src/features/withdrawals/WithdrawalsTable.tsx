import { Link } from "react-router";
import { recordLinkClass, StatusBadge } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetAccountQuery } from "@/features/accounts/accountsApi";
import { CustomerName } from "@/features/nominatedBankAccounts/BankAccountChecksPage";
import { payeeLabel, withdrawalStatusLabel } from "@/features/withdrawals/withdrawalLabels";
import type { Withdrawal } from "@/features/withdrawals/withdrawalsApi";
import { formatAmount } from "@/shared/format/money";
import { formatInstant } from "@/shared/time/instant";

function AccountName({ id }: { id: string }) {
  const { data } = useGetAccountQuery(id);
  return <>{data?.name ?? ""}</>;
}

/** D46 columns: Requested, Customer, Account, Amount, Currency, To, Status, Requested by. */
export function WithdrawalsTable({ items, places, compact = false }: {
  items: Withdrawal[];
  places: (code: string) => number | undefined;
  compact?: boolean;
}) {
  return (
    <Card className="gap-0 py-0">
      <CardContent className="px-0">
        <Table aria-label="Withdrawals">
          <TableHeader>
            <TableRow>
              <TableHead>Requested</TableHead>
              {compact ? null : <TableHead>Customer</TableHead>}
              {compact ? null : <TableHead>Account</TableHead>}
              <TableHead>Amount</TableHead>
              <TableHead>Currency</TableHead>
              <TableHead>To</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Requested by</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {items.map((item) => (
              <TableRow key={item.id}>
                <TableCell>
                  <Link to={`/withdrawals/${item.id}`} className={recordLinkClass}>
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
                <TableCell>{formatAmount(item.amount, places(item.currency))}</TableCell>
                <TableCell>{item.currency}</TableCell>
                <TableCell>{payeeLabel(item.nominatedBankAccount)}</TableCell>
                <TableCell>
                  <StatusBadge status={withdrawalStatusLabel(item.status)} />
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
