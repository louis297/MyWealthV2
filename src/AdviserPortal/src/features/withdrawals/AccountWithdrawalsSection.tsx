import { Section } from "@/components/portal";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { WithdrawalsTable } from "@/features/withdrawals/WithdrawalsTable";
import { useGetWithdrawalsQuery } from "@/features/withdrawals/withdrawalsApi";

/** D44: the open withdrawals of an account, under its holdings. */
export function AccountWithdrawalsSection({ accountId }: { accountId: string }) {
  const { data } = useGetWithdrawalsQuery({ accountId, status: "Requested,Approved,Sent" });
  const currencies = useGetCurrenciesQuery();
  const places = (code: string) => currencies.data?.find((item) => item.code === code)?.decimalPlaces;
  if (!data || data.items.length === 0) {
    return null;
  }

  return (
    <Section title="Open withdrawals">
      <WithdrawalsTable items={data.items} places={places} compact />
    </Section>
  );
}
