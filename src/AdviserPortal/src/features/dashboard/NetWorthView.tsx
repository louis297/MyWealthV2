import type { ReactNode } from "react";
import { Link } from "react-router";
import { Muted, Section } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { NetWorth } from "@/features/dashboard/dashboardApi";

export function NetWorthView({ data }: { data: NetWorth }) {
  const currencies = data.currencies;
  const byCustomer = data.breakdowns.byCustomer;
  const byAccountType = data.breakdowns.byAccountType;
  const topHoldings = data.breakdowns.topHoldings;
  const byCustodyMode = data.breakdowns.byCustodyMode;

  return (
    <div className="flex flex-col gap-6">
      {currencies.length === 0 ? <Muted>No balances.</Muted> : <CurrencyTable currencies={currencies} />}
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
