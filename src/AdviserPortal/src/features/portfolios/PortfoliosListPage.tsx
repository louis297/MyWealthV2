import { Link, useSearchParams } from "react-router";
import { Muted, Page, PageHeading, recordLinkClass } from "@/components/portal";
import { buttonVariants } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetPortfoliosQuery } from "@/features/portfolios/portfoliosApi";
import { Pager } from "@/shared/components/Pager";
import { cn } from "cn";

export function PortfoliosListPage() {
  const [params, setParams] = useSearchParams();
  const page = Number(params.get("page") || "1") || 1;
  const { data } = useGetPortfoliosQuery({ page });
  const items = data?.items ?? [];

  return (
    <Page>
      <PageHeading
        title="Portfolios"
        actions={
          <Link to="/portfolios/new" className={cn(buttonVariants({ size: "sm" }))}>
            Create
          </Link>
        }
      />
      {data && items.length === 0 ? <Muted>No portfolios yet. Create one to get started.</Muted> : null}
      {items.length > 0 ? (
        <Card className="gap-0 py-0">
          <CardContent className="px-0">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead>Base currency</TableHead>
                  <TableHead>Status</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((item) => (
                  <TableRow key={item.id}>
                    <TableCell>
                      <Link to={`/portfolios/${item.id}`} className={recordLinkClass}>
                        {item.name}
                      </Link>
                    </TableCell>
                    <TableCell>{item.baseCurrency}</TableCell>
                    <TableCell>{item.isActive ? "Active" : "Inactive"}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      ) : null}
      <Pager
        page={page}
        pageSize={20}
        totalCount={data?.totalCount ?? 0}
        onPage={(next) => {
          const updated = new URLSearchParams(params);
          updated.set("page", String(next));
          setParams(updated);
        }}
      />
    </Page>
  );
}
