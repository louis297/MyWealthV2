import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { CheckField, FilterBar, Page, PageHeading, recordLinkClass, StatusBadge, TextField } from "@/components/portal";
import { buttonVariants } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetAdvisersQuery } from "@/features/advisers/advisersApi";
import { Pager } from "@/shared/components/Pager";
import { cn } from "cn";

export function AdvisersListPage() {
  const [params, setParams] = useSearchParams();
  const page = Number(params.get("page") || "1") || 1;
  const search = params.get("search") ?? "";
  const enabledOnly = params.get("enabledOnly") === "true";
  const [searchInput, setSearchInput] = useState(search);

  useEffect(() => {
    setSearchInput(search);
  }, [search]);

  useEffect(() => {
    const handle = window.setTimeout(() => {
      const next = searchInput.trim();
      if (next === search) {
        return;
      }
      const updated = new URLSearchParams(params);
      if (next) {
        updated.set("search", next);
      } else {
        updated.delete("search");
      }
      updated.set("page", "1");
      setParams(updated);
    }, 300);
    return () => window.clearTimeout(handle);
  }, [searchInput, search, params, setParams]);

  const { data } = useGetAdvisersQuery({
    page,
    search: search || undefined,
    enabledOnly: enabledOnly || undefined,
  });

  function updateParam(key: string, value: string | null) {
    const updated = new URLSearchParams(params);
    if (value) {
      updated.set(key, value);
    } else {
      updated.delete(key);
    }
    if (key !== "page") {
      updated.set("page", "1");
    }
    setParams(updated);
  }

  return (
    <Page>
      <PageHeading
        title="Advisers"
        actions={
          <Link to="/advisers/new" className={cn(buttonVariants({ size: "sm" }))}>
            New adviser
          </Link>
        }
      />
      <FilterBar>
        <TextField
          id="advisers-search"
          label="Search"
          className="w-56"
          value={searchInput}
          onChange={(event) => setSearchInput(event.target.value)}
        />
        <CheckField checked={enabledOnly} onChange={(event) => updateParam("enabledOnly", event.target.checked ? "true" : null)}>
          Active only
        </CheckField>
      </FilterBar>
      <Card className="gap-0 py-0">
        <CardContent className="px-0">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Email</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Created</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {(data?.items ?? []).map((item) => (
                <TableRow key={item.id}>
                  <TableCell>
                    <Link to={`/advisers/${item.id}`} className={recordLinkClass}>
                      {item.name}
                    </Link>
                  </TableCell>
                  <TableCell>{item.email}</TableCell>
                  <TableCell>
                    <StatusBadge status={item.status} />
                  </TableCell>
                  <TableCell>{item.created.slice(0, 10)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
      <Pager
        page={page}
        pageSize={20}
        totalCount={data?.totalCount ?? 0}
        onPage={(next) => updateParam("page", String(next))}
      />
    </Page>
  );
}
