import { type FormEvent, useState } from "react";
import { Link, useParams } from "react-router";
import { ConflictNotice, DetailGrid, DetailItem, Messages, Muted, Page, PageHeading, recordLinkClass, Section, SelectField, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetInstrumentsQuery } from "@/features/instruments/instrumentsApi";
import {
  useDisablePortfolioMutation,
  useGetAssignedAccountsQuery,
  useGetPortfolioQuery,
  useRebalancePortfolioMutation,
  useUpdatePortfolioMutation,
  type PortfolioLine,
} from "@/features/portfolios/portfoliosApi";

type DraftLine = {
  key: string;
  instrumentId: string;
  weight: string;
};

function isNotFound(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 404;
}

function isStatus(error: unknown, status: number) {
  return typeof error === "object" && error !== null && "status" in error && error.status === status;
}

function problemMessages(error: unknown): string[] {
  if (typeof error !== "object" || error === null || !("data" in error)) {
    return [];
  }
  const data = (error as { data?: { errors?: Record<string, string[]>; detail?: string } }).data;
  if (data?.errors) {
    return Object.values(data.errors).flat();
  }
  if (data?.detail) {
    return [data.detail];
  }
  return [];
}

function draftFrom(lines: PortfolioLine[]): DraftLine[] {
  return lines.map((line, index) => ({
    key: `${line.instrumentId}-${index}`,
    instrumentId: line.instrumentId,
    weight: String(line.targetWeight),
  }));
}

function weightsChanged(loaded: PortfolioLine[], draft: DraftLine[]) {
  if (loaded.length !== draft.length) {
    return true;
  }
  return loaded.some(
    (line, index) =>
      line.instrumentId !== draft[index]?.instrumentId || Number(line.targetWeight) !== Number(draft[index]?.weight),
  );
}

function lineMessages(lines: DraftLine[], activeIds: ReadonlySet<string>): string[] {
  const messages: string[] = [];
  const seen = new Set<string>();
  let cents = 0;
  let weightsValid = true;
  for (const line of lines) {
    const weight = Number(line.weight);
    const rounded = Math.round(weight * 100);
    if (line.weight.trim() === "" || !Number.isFinite(weight) || rounded <= 0 || rounded > 10000) {
      weightsValid = false;
    } else {
      cents += rounded;
    }
    if (!line.instrumentId || (activeIds.size > 0 && !activeIds.has(line.instrumentId)) || seen.has(line.instrumentId)) {
      messages.push("Choose an active instrument.");
    }
    if (line.instrumentId) {
      seen.add(line.instrumentId);
    }
  }
  if (!weightsValid) {
    messages.push("Each weight must be greater than 0 and no more than 100.");
  } else if (cents !== 10000) {
    messages.push("Weights must total 100.00.");
  }
  return [...new Set(messages)];
}

export function PortfolioDetailPage() {
  const { id } = useParams();
  const portfolio = useGetPortfolioQuery(id ?? "", { skip: !id });
  const assigned = useGetAssignedAccountsQuery(id ?? "", { skip: !id });
  const instruments = useGetInstrumentsQuery();
  const [updatePortfolio] = useUpdatePortfolioMutation();
  const [disablePortfolio] = useDisablePortfolioMutation();
  const [rebalancePortfolio] = useRebalancePortfolioMutation();
  const [name, setName] = useState("");
  const [lines, setLines] = useState<DraftLine[]>([]);
  const [syncedId, setSyncedId] = useState<string | null>(null);
  const [messages, setMessages] = useState<string[]>([]);
  const [conflict, setConflict] = useState(false);
  const [queued, setQueued] = useState(false);

  if (portfolio.data && syncedId !== portfolio.data.id) {
    setName(portfolio.data.name);
    setLines(draftFrom(portfolio.data.lines));
    setSyncedId(portfolio.data.id);
  }

  async function onRebalance() {
    if (!id) {
      return;
    }
    try {
      const result = await rebalancePortfolio(id).unwrap();
      setQueued(Boolean(result?.jobId));
      setMessages([]);
    } catch (error: unknown) {
      setQueued(false);
      setMessages(problemMessages(error));
    }
  }

  async function onDisable() {
    const current = portfolio.data;
    if (!current?.isActive) {
      return;
    }
    try {
      await disablePortfolio({ id: current.id, rowVersion: current.rowVersion }).unwrap();
      setConflict(false);
      setMessages([]);
    } catch (error: unknown) {
      if (isStatus(error, 409)) {
        setConflict(true);
        setMessages([]);
        void portfolio.refetch();
        return;
      }
      setConflict(false);
      setMessages(problemMessages(error));
    }
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    const current = portfolio.data;
    if (!current) {
      return;
    }
    const activeIds = new Set((instruments.data?.items ?? []).filter((item) => item.isActive).map((item) => item.id));
    const problems = lineMessages(lines, activeIds);
    if (problems.length > 0) {
      setMessages(problems);
      return;
    }
    if (weightsChanged(current.lines, lines) && (assigned.data?.length ?? 0) > 0) {
      if (!window.confirm("This will rebalance every account currently assigned to this portfolio. Continue?")) {
        return;
      }
    }
    try {
      const result = await updatePortfolio({
        id: current.id,
        body: {
          rowVersion: current.rowVersion,
          name: name.trim(),
          lines: lines.map((line) => ({
            instrumentId: line.instrumentId,
            targetWeight: Number(line.weight),
          })),
        },
      }).unwrap();
      setConflict(false);
      setMessages([]);
      setQueued(Boolean(result && typeof result === "object" && "jobId" in result));
    } catch (error: unknown) {
      setQueued(false);
      if (isStatus(error, 409)) {
        setConflict(true);
        setMessages([]);
        void portfolio.refetch();
        return;
      }
      setConflict(false);
      setMessages(problemMessages(error));
    }
  }

  if (isNotFound(portfolio.error)) {
    return <Muted>Not found</Muted>;
  }

  if (portfolio.isLoading || assigned.isLoading || !portfolio.data || !assigned.data || lines.length === 0) {
    return <Muted>Loading…</Muted>;
  }

  const data = portfolio.data;
  const activeInstruments = instruments.data?.items ?? [];

  return (
    <Page>
      <PageHeading
        title={data.name}
        actions={
          data.isActive ? (
            <Button type="button" variant="destructive" onClick={() => void onDisable()}>
              Disable
            </Button>
          ) : null
        }
      />
      {queued ? <p>Rebalance queued.</p> : null}
      {conflict ? <ConflictNotice /> : null}
      <Messages messages={messages} />
      <Card>
        <CardContent>
          <DetailGrid>
            <DetailItem label="Base currency">{data.baseCurrency}</DetailItem>
            <DetailItem label="Status">{data.isActive ? "Active" : "Inactive"}</DetailItem>
          </DetailGrid>
        </CardContent>
      </Card>
      <Card>
        <CardContent>
          <form className="flex flex-col gap-4" onSubmit={(event) => void onSubmit(event)}>
            <TextField id="portfolio-name" label="Name" value={name} onChange={(event) => setName(event.target.value)} required />
            {lines.map((line) => (
              <div key={line.key} className="grid gap-4 sm:grid-cols-2">
                <SelectField
                  id={`portfolio-instrument-${line.key}`}
                  label="Instrument"
                  value={line.instrumentId}
                  onChange={(event) =>
                    setLines((current) =>
                      current.map((item) => (item.key === line.key ? { ...item, instrumentId: event.target.value } : item)),
                    )
                  }
                >
                  {activeInstruments.map((item) => (
                    <SelectItem key={item.id} value={item.id}>
                      {item.symbol}
                    </SelectItem>
                  ))}
                </SelectField>
                <TextField
                  id={`portfolio-weight-${line.key}`}
                  label="Weight"
                  value={line.weight}
                  onChange={(event) =>
                    setLines((current) =>
                      current.map((item) => (item.key === line.key ? { ...item, weight: event.target.value } : item)),
                    )
                  }
                  inputMode="decimal"
                />
              </div>
            ))}
            <Button type="submit">Save</Button>
          </form>
        </CardContent>
      </Card>
      <Section
        title="Assigned accounts"
        actions={
          <Button type="button" variant="outline" onClick={() => void onRebalance()}>
            Rebalance accounts
          </Button>
        }
      >
        {assigned.data.length === 0 ? <Muted>No accounts are assigned to this portfolio.</Muted> : (
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Account</TableHead>
                    <TableHead>Last rebalance</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {assigned.data.map((row) => (
                    <TableRow key={row.account.id}>
                      <TableCell>
                        <Link to={`/accounts/${row.account.id}`} className={recordLinkClass}>
                          {row.account.name}
                        </Link>
                      </TableCell>
                      <TableCell>
                        {row.assignment.lastRebalanceStatus ?? "No rebalance has run yet."}
                        {row.assignment.lastRebalanceMessage ? ` ${row.assignment.lastRebalanceMessage}` : ""}
                      </TableCell>
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
