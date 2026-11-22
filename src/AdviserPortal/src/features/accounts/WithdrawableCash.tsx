import type { Account } from "@/features/accounts/accountsApi";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { formatMoney } from "@/shared/format/money";

/** D75: the cash that can leave a Custodial account now, and the part held back until sales settle (orders R44). */
export function WithdrawableCash({ account }: { account: Account }) {
  const currencies = useGetCurrenciesQuery();
  const places = currencies.data?.find((item) => item.code === account.currency)?.decimalPlaces ?? 2;
  const withdrawable = account.withdrawableCash ?? account.availableCash ?? account.cashBalance;
  const unsettled = account.unsettledCash ?? 0;
  return (
    <div className="text-muted-foreground text-sm">
      <p>{`Available to withdraw ${formatMoney(withdrawable, account.currency, places)}`}</p>
      {unsettled > 0 ? <p>{`${formatMoney(unsettled, account.currency, places)} from sales settles later.`}</p> : null}
    </div>
  );
}
