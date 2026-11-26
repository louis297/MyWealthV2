import { useAppSelector } from "@/app/hooks";
import { Muted, Page, PageHeading, Section } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { custodyLabel, externalNote } from "@/features/accounts/custodyLabel";
import { useGetCustomerNetWorthQuery } from "@/features/dashboard/dashboardApi";
import { FoldedTotalLine } from "@/features/dashboard/FoldedTotalLine";
import { HeldOnPlatformChart } from "@/features/dashboard/HeldOnPlatformChart";

export function DashboardPage() {
  const id = useAppSelector((state) => state.session.currentUser?.id);
  const { data, isLoading, isError } = useGetCustomerNetWorthQuery(id ?? "", { skip: !id });

  if (!id || isLoading) {
    return <Muted>Loading session…</Muted>;
  }

  if (isError || !data) {
    return <Muted>Net worth is not available.</Muted>;
  }

  // Custodial first, then External (customer-portal C14, C15).
  const custodyRows = ["Custodial", "External"].flatMap((mode) =>
    data.breakdowns.byCustodyMode.filter((row) => row.custodyMode === mode),
  );

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
      {data.folded ? <FoldedTotalLine folded={data.folded} /> : null}
      <Section title="Held on platform and elsewhere">
        {custodyRows.length === 0 ? <Muted>No balances.</Muted> : (
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Custody</TableHead>
                    <TableHead>Assets</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {custodyRows.map((row) => (
                    <TableRow key={row.custodyMode}>
                      <TableCell>
                        <span>{custodyLabel(row.custodyMode)}</span>
                        {row.custodyMode === "External" ? (
                          <span className="mt-1 block text-xs whitespace-normal text-muted-foreground">{externalNote}</span>
                        ) : null}
                      </TableCell>
                      <TableCell>
                        {row.currencies.map((currency) => `${currency.currency} ${currency.assets.total}`).join(", ")}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        )}
      </Section>
      <HeldOnPlatformChart />
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
