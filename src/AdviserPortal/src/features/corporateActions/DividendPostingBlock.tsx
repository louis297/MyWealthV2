import { Link } from "react-router";
import { DetailGrid, DetailItem, recordLinkClass, Section } from "@/components/portal";
import { perShare } from "@/features/corporateActions/corporateActionText";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { taxKindLabel } from "@/features/interest/interestText";
import type { Transaction } from "@/features/transactions/transactionsApi";
import { formatMoney, sumAmounts } from "@/shared/format/money";

/** dividends D100: the dividend a Dividend pays, its gross, each tax, the credit, the reinvestment and the net. */
export function DividendPostingBlock({ transaction }: { transaction: Transaction }) {
  const currencies = useGetCurrenciesQuery();
  const detail = transaction.dividend!;
  const principal = transaction.cash.find((leg) => leg.role === "Principal");
  const currency = principal?.currency ?? detail.currency;
  const places = (code: string) => currencies.data?.find((item) => item.code === code)?.decimalPlaces;
  const money = (value: number, code = currency) => formatMoney(value, code, places(code));
  const taxes = transaction.cash.filter((leg) => leg.role === "Tax" && leg.tax);
  const net = sumAmounts(transaction.cash.map((leg) => leg.amount));

  return (
    <Section title={detail.instrument.name}>
      <p className="text-sm">{`${detail.quantity} shares × ${perShare(detail.amountPerShare)}`}</p>
      <DetailGrid>
        <DetailItem label="Gross">{principal ? money(principal.amount) : "—"}</DetailItem>
        <DetailItem label="Tax withheld">
          {taxes.length === 0 ? "None" : taxes.map((leg) => (
            <p key={leg.tax!.kind}>{`${taxKindLabel(leg.tax!.kind)} ${leg.tax!.ratePercent} %: ${money(leg.tax!.amount)}`}</p>
          ))}
        </DetailItem>
        <DetailItem label="Ex-date">{detail.exDate}</DetailItem>
        <DetailItem label="Record date">{detail.recordDate ?? "—"}</DetailItem>
        <DetailItem label="Payment date">{detail.paymentDate ?? "—"}</DetailItem>
      </DetailGrid>
      {detail.imputationCredit > 0 ? <p className="text-sm">{`Imputation credit ${money(detail.imputationCredit, detail.currency)}`}</p> : null}
      {detail.reinvestedQuantity != null && detail.reinvestmentPrice != null ? (
        <p className="text-sm">{`Reinvested: ${detail.reinvestedQuantity} shares at ${perShare(detail.reinvestmentPrice)}`}</p>
      ) : null}
      <p className="text-sm font-medium">{`Net cash ${money(net)}`}</p>
      <Link to={`/corporate-actions/${detail.corporateActionId}`} className={recordLinkClass}>Corporate action</Link>
    </Section>
  );
}
