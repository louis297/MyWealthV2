import { Muted, Section } from "@/components/portal";
import { type Account, useGetAccountValuationsQuery } from "@/features/accounts/accountsApi";
import { ValueChart } from "@/shared/components/ValueChart";
import { addDays } from "@/shared/time/days";

/** D109: the stored valuations of the 90 days up to the valued-through date of a Custodial account. */
export function AccountValueSection({ account, decimalPlaces }: { account: Account; decimalPlaces?: number }) {
  const to = account.valuedThrough ?? null;
  const valuations = useGetAccountValuationsQuery(
    { id: account.id, from: to ? addDays(to, -89) : "", to: to ?? "" },
    { skip: to === null },
  );
  const points = [...(valuations.data?.items ?? [])]
    .sort((left, right) => left.date.localeCompare(right.date))
    .map((item) => ({
      date: item.date,
      value: item.total,
      incomplete: item.complete ? null : "Some holdings were not valued on this day.",
    }));

  return (
    <Section title="Value over the last 90 days">
      {to === null ? (
        <Muted>Not valued yet. The first value appears two days after the account was opened.</Muted>
      ) : valuations.isLoading ? (
        <Muted>Loading…</Muted>
      ) : points.length > 0 ? (
        <ValueChart title="Value over the last 90 days" currency={account.currency} decimalPlaces={decimalPlaces} points={points} />
      ) : null}
    </Section>
  );
}
