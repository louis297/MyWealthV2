import type { ReactNode } from "react";
import { Link } from "react-router";
import { Muted, Section } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import type { FoldedTotal, NetWorth } from "@/features/dashboard/dashboardApi";
import { notFoldedReason } from "@/features/dashboard/valuationText";
import { formatAmount } from "@/shared/format/money";

export function NetWorthView({ data, chart }: { data: NetWorth; chart?: ReactNode }) {
  const currencies = data.currencies;
  const byCustomer = data.breakdowns.byCustomer;
  const byAccountType = data.breakdowns.byAccountType;
  const topHoldings = data.breakdowns.topHoldings;
  const byCustodyMode = data.breakdowns.byCustodyMode;

  return (
    <div className="flex flex-col gap-6">
      {currencies.length === 0 ? <Muted>No balances.</Muted> : <CurrencyTable currencies={currencies} />}
      {data.folded ? <FoldedLine folded={data.folded} /> : null}
      <div className="grid gap-6 md:grid-cols-2">
        <CustodyFigure
          title="Funds under management"
          entry={byCustodyMode.find((row) => row.custodyMode === "Custodial")}
        />
        <CustodyFigure
          title="External assets"
          entry={byCustodyMode.find((row) => row.custodyMode === "External")}
        />
      </div>
      {chart}
      <Section title="By customer">
        {byCustomer.length === 0 ? <Muted>None.</Muted> : (
          <TableCard>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Net</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {byCustomer.map((row) => (
                <TableRow key={row.customerId}>
                  <TableCell>
                    <Link to={`/customers/${row.customerId}`} className="font-medium underline-offset-4 hover:underline">
                      {row.name}
                    </Link>
                  </TableCell>
                  <TableCell>
                    {row.currencies.map((currency) => (
                      <span key={currency.currency} className="mr-3">
                        <span>{currency.currency}</span> <span>{currency.net}</span>
                      </span>
                    ))}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </TableCard>
        )}
      </Section>
      <Section title="By account type">
        {byAccountType.length === 0 ? <Muted>None.</Muted> : (
          <TableCard>
            <TableHeader>
              <TableRow>
                <TableHead>Type</TableHead>
                <TableHead>Net</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {byAccountType.map((row) => (
                <TableRow key={row.type}>
                  <TableCell>{row.type}</TableCell>
                  <TableCell>
                    {row.currencies.map((currency) => (
                      <span key={currency.currency} className="mr-3">
                        <span>{currency.currency}</span> <span>{currency.net}</span>
                      </span>
                    ))}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </TableCard>
        )}
      </Section>
      <Section title="Top holdings">
        {topHoldings.length === 0 ? <Muted>None.</Muted> : (
          <TableCard label="Top holdings">
            <TableHeader>
              <TableRow>
                <TableHead>Symbol</TableHead>
                <TableHead>Quantity</TableHead>
                <TableHead>Market value</TableHead>
                <TableHead>Currency</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {topHoldings.map((row) => (
                <TableRow key={row.instrumentId}>
                  <TableCell>{row.symbol}</TableCell>
                  <TableCell>{row.quantity}</TableCell>
                  <TableCell>{row.marketValue}</TableCell>
                  <TableCell>{row.currency}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </TableCard>
        )}
      </Section>
    </div>
  );
}

/**
 * D108: the per-currency net in the reporting currency at today's rates, with the currencies it leaves out. Not shown
 * when the only currency is the reporting currency.
 */
function FoldedLine({ folded }: { folded: FoldedTotal }) {
  const currencies = useGetCurrenciesQuery();
  const lines = folded.currencies;
  if (lines.length === 0 || (lines.length === 1 && lines[0].currency === folded.currency)) {
    return null;
  }

  const places = currencies.data?.find((currency) => currency.code === folded.currency)?.decimalPlaces;
  const left = lines.filter((line) => line.status !== "Folded");
  return (
    <div className="flex flex-col gap-1 text-sm">
      <p>{`Total in ${folded.currency}: ${formatAmount(folded.total, places)} at today's exchange rates`}</p>
      {folded.complete || left.length === 0 ? null : (
        <Muted>{`Not counted: ${left.map((line) => `${line.currency} (${notFoldedReason(line)})`).join(", ")}`}</Muted>
      )}
    </div>
  );
}

/** One custody mode's assets per currency (custody-mode-grouping D24, D25). */
function CustodyFigure({ title, entry }: { title: string; entry?: NetWorth["breakdowns"]["byCustodyMode"][number] }) {
  return (
    <Section title={title}>
      {!entry || entry.currencies.length === 0 ? <Muted>None.</Muted> : (
        <TableCard>
          <TableHeader>
            <TableRow>
              <TableHead>Currency</TableHead>
              <TableHead>Assets</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {entry.currencies.map((row) => (
              <TableRow key={row.currency}>
                <TableCell>{row.currency}</TableCell>
                <TableCell>{row.assets.total}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </TableCard>
      )}
    </Section>
  );
}

function TableCard({ label, children }: { label?: string; children: ReactNode }) {
  return (
    <Card className="gap-0 py-0">
      <CardContent className="px-0">
        <Table aria-label={label}>{children}</Table>
      </CardContent>
    </Card>
  );
}

function CurrencyTable({ currencies }: { currencies: NetWorth["currencies"] }) {
  return (
    <TableCard>
      <TableHeader>
        <TableRow>
          <TableHead>Currency</TableHead>
          <TableHead>Cash</TableHead>
          <TableHead>Holdings</TableHead>
          <TableHead>Assets</TableHead>
          <TableHead>Liabilities</TableHead>
          <TableHead>Net</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {currencies.map((row) => (
          <TableRow key={row.currency}>
            <TableHead>{row.currency}</TableHead>
            <TableCell>{row.assets.cash}</TableCell>
            <TableCell>{row.assets.holdings}</TableCell>
            <TableCell>{row.assets.total}</TableCell>
            <TableCell>{row.liabilities.total}</TableCell>
            <TableCell>{row.net}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </TableCard>
  );
}
