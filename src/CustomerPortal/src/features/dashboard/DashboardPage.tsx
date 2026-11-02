import { useAppSelector } from "@/app/hooks";
import { Muted, Page, PageHeading, Section } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetCustomerNetWorthQuery } from "@/features/dashboard/dashboardApi";

export function DashboardPage() {
  const id = useAppSelector((state) => state.session.currentUser?.id);
  const { data, isLoading, isError } = useGetCustomerNetWorthQuery(id ?? "", { skip: !id });

  if (!id || isLoading) {
    return <Muted>Loading session…</Muted>;
  }

  if (isError || !data) {
    return <Muted>Net worth is not available.</Muted>;
  }

  return (
    <Page>
      <PageHeading title="Dashboard" />
      {data.currencies.length === 0 ? <Muted>No balances.</Muted> : (
        <Card className="gap-0 py-0">
          <CardContent className="px-0">
            <Table>
              <caption className="mb-2 px-4 text-left text-sm font-medium">Per currency</caption>
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
                {data.currencies.map((row) => (
                  <TableRow key={row.currency}>
                    <TableCell>{row.currency}</TableCell>
                    <TableCell>{row.assets.cash}</TableCell>
                    <TableCell>{row.assets.holdings}</TableCell>
                    <TableCell>{row.assets.total}</TableCell>
                    <TableCell>{row.liabilities.total}</TableCell>
                    <TableCell>{row.net}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}
      <Section title="By account type">
        {data.breakdowns.byAccountType.length === 0 ? <Muted>No balances.</Muted> : (
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Type</TableHead>
                    <TableHead>Net</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data.breakdowns.byAccountType.map((row) => (
                    <TableRow key={row.type}>
                      <TableCell>{row.type}</TableCell>
                      <TableCell>
                        {row.currencies.map((currency) => `${currency.currency} ${currency.net}`).join(", ")}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        )}
      </Section>
      <Section title="Top holdings">
        {data.breakdowns.topHoldings.length === 0 ? <Muted>No balances.</Muted> : (
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Symbol</TableHead>
                    <TableHead>Market value</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data.breakdowns.topHoldings.map((holding) => (
                    <TableRow key={holding.instrumentId}>
                      <TableCell>{holding.symbol}</TableCell>
                      <TableCell>{holding.marketValue} {holding.currency}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        )}
      </Section>
    </Page>
  );
}
