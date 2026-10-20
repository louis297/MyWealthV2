import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { useAppSelector } from "@/app/hooks";
import { CheckField, FilterBar, Page, PageHeading, recordLinkClass, SelectField, StatusBadge, TextField } from "@/components/portal";
import { buttonVariants } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetAdvisersQuery } from "@/features/advisers/advisersApi";
import { useGetCustomersQuery } from "@/features/customers/customersApi";
import { roles } from "@/features/session/roles";
import { Pager } from "@/shared/components/Pager";
import { cn } from "cn";

function createdDate(value: string) {
  return value.slice(0, 10);
}

export function CustomersListPage() {
  const me = useAppSelector((state) => state.session.currentUser);
  const [params, setParams] = useSearchParams();
  const page = Number(params.get("page") || "1") || 1;
  const search = params.get("search") ?? "";
  const enabledOnly = params.get("enabledOnly") === "true";
  const adviserId = params.get("adviserId") ?? "";
  const isTenantAdmin = me?.role === roles.tenantAdmin;
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

  const { data } = useGetCustomersQuery({
    page,
    search: search || undefined,
    enabledOnly: enabledOnly || undefined,
    adviserId: isTenantAdmin && adviserId ? adviserId : undefined,
  });

  const { data: advisers } = useGetAdvisersQuery(
    { page: 1, pageSize: 100 },
    { skip: !isTenantAdmin },
  );

  const adviserNames = new Map((advisers?.items ?? []).map((item) => [item.id, item.name]));

  function adviserName(id: string) {
    if (!isTenantAdmin) {
      return me?.name ?? "Me";
    }
    return adviserNames.get(id) ?? "";
  }

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
        title="Customers"
        actions={
          <Link to="/customers/new" className={cn(buttonVariants({ size: "sm" }))}>
            New customer
          </Link>
        }
      />
      <FilterBar>
        <TextField
          id="customers-search"
          label="Search"
          className="w-56"
          value={searchInput}
          onChange={(event) => setSearchInput(event.target.value)}
        />
        <CheckField checked={enabledOnly} onChange={(event) => updateParam("enabledOnly", event.target.checked ? "true" : null)}>
          Active only
        </CheckField>
        {isTenantAdmin ? (
          <SelectField
            id="customers-adviser"
            label="Adviser"
            className="w-56"
            value={adviserId}
            onChange={(event) => updateParam("adviserId", event.target.value || null)}
          >
            <option value="">All</option>
            {(advisers?.items ?? []).map((item) => (
              <option key={item.id} value={item.id}>
                {item.name}
              </option>
            ))}
          </SelectField>
        ) : null}
      </FilterBar>
      <Card className="gap-0 py-0">
        <CardContent className="px-0">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Email</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Adviser</TableHead>
                <TableHead>Created</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {(data?.items ?? []).map((item) => (
                <TableRow key={item.id}>
                  <TableCell>
                    <Link to={`/customers/${item.id}`} className={recordLinkClass}>
                      {item.name}
                    </Link>
                  </TableCell>
                  <TableCell>{item.email}</TableCell>
                  <TableCell>
                    <StatusBadge status={item.status} />
                  </TableCell>
                  <TableCell>{adviserName(item.adviserId)}</TableCell>
                  <TableCell>{createdDate(item.created)}</TableCell>
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
