import { useState } from "react";
import { DetailGrid, DetailItem, Section } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { useGetInterestAccrualsQuery } from "@/features/interest/interestApi";
import { taxKindLabel } from "@/features/interest/interestText";
import type { Transaction } from "@/features/transactions/transactionsApi";
import { formatAmount, formatMoney } from "@/shared/format/money";

function DailyInterest({ accountId, transactionId, decimalPlaces }: { accountId: string; transactionId: string; decimalPlaces?: number }) {
  const { data } = useGetInterestAccrualsQuery({ accountId, transactionId });
  return (
    <Table aria-label="Daily interest">
      <TableHeader>
        <TableRow>
          <TableHead>Day</TableHead>
          <TableHead>Balance</TableHead>
          <TableHead>Rate</TableHead>
          <TableHead>Interest</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {(data?.items ?? []).map((row) => (
          <TableRow key={row.date}>
            <TableCell>{row.date}</TableCell>
            <TableCell>{formatAmount(row.balance, decimalPlaces)}</TableCell>
            <TableCell>{`${row.ratePercent} %`}</TableCell>
            <TableCell>{row.amount.toFixed(8)}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

/** D61: the period, gross, tax withheld and net of an interest posting, and its daily accruals on request. */
export function InterestPostingBlock({ transaction }: { transaction: Transaction }) {
  const [open, setOpen] = useState(false);
  const currencies = useGetCurrenciesQuery();
  const interest = transaction.interest!;
  const principal = transaction.cash.find((leg) => leg.role === "Principal");
  const tax = transaction.cash.find((leg) => leg.role === "Tax");
  const currency = principal?.currency ?? "";
  const places = currencies.data?.find((item) => item.code === currency)?.decimalPlaces;
  const net = transaction.cash.reduce((sum, leg) => sum + Math.round(leg.amount * 10000), 0) / 10000;

  return (
    <Section title={`Interest ${interest.periodFrom} to ${interest.periodTo} (${interest.days} days)`}>
      <DetailGrid>
        <DetailItem label="Gross">{formatMoney(interest.grossAmount, currency, places)}</DetailItem>
        <DetailItem label="Tax withheld">
          {tax?.tax
            ? `${taxKindLabel(tax.tax.kind)} ${tax.tax.ratePercent} %: ${formatMoney(tax.tax.amount, currency, places)}`
            : "None"}
        </DetailItem>
        <DetailItem label="Net">{formatMoney(net, currency, places)}</DetailItem>
      </DetailGrid>
      {principal ? (
        <>
          <Button type="button" size="sm" variant="outline" className="w-fit" onClick={() => setOpen((value) => !value)}>
            Daily interest
          </Button>
          {open ? <DailyInterest accountId={principal.accountId} transactionId={transaction.id} decimalPlaces={places} /> : null}
        </>
      ) : null}
    </Section>
  );
}
