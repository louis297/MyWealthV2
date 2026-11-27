import { DetailGrid, DetailItem, Section } from "@/components/portal";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { taxKindLabel } from "@/features/interest/interestText";
import type { Transaction } from "@/features/transactions/transactionsApi";
import { formatMoney, sumAmounts } from "@/shared/format/money";

/**
 * external-income D113: a hand `Interest` with the taxes an outside payer withheld. It has no period and no daily
 * interest: the gross, each tax with its rate, and the net.
 */
export function EnteredInterestBlock({ transaction }: { transaction: Transaction }) {
  const currencies = useGetCurrenciesQuery();
  const principal = transaction.cash.find((leg) => leg.role === "Principal");
  const taxes = transaction.cash.filter((leg) => leg.role === "Tax" && leg.tax);
  const currency = principal?.currency ?? "";
  const places = currencies.data?.find((item) => item.code === currency)?.decimalPlaces;
  const net = sumAmounts(transaction.cash.map((leg) => leg.amount));

  return (
    <Section title="Interest">
      <DetailGrid>
        <DetailItem label="Gross">{principal ? formatMoney(principal.amount, currency, places) : "—"}</DetailItem>
        <DetailItem label="Tax withheld">
          {taxes.map((leg) => (
            <p key={leg.tax!.kind}>
              {`${taxKindLabel(leg.tax!.kind)} ${leg.tax!.ratePercent} %: ${formatMoney(leg.tax!.amount, currency, places)}`}
            </p>
          ))}
        </DetailItem>
        <DetailItem label="Net">{formatMoney(net, currency, places)}</DetailItem>
      </DetailGrid>
    </Section>
  );
}
