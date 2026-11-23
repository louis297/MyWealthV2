import { type FormEvent, useRef, useState } from "react";
import { Link, useParams } from "react-router";
import { ConflictNotice, DetailGrid, DetailItem, Messages, Muted, Page, PageHeading, recordLinkClass, Section, SelectField, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetInstrumentsQuery } from "@/features/instruments/instrumentsApi";
import { problemCode, staleRowVersion } from "@/shared/api/problem";
import {
  useDisablePortfolioMutation,
  useGetAssignedAccountsQuery,
  useGetPortfolioQuery,
  useRebalancePortfolioMutation,
  useUpdatePortfolioMutation,
  type PortfolioLine,
} from "@/features/portfolios/portfoliosApi";
import { AssignmentRebalanceLine } from "@/features/rebalances/AssignmentRebalanceLine";
import { useConfirmRebalanceMutation, useLazyGetRebalancesQuery } from "@/features/rebalances/rebalancesApi";

type DraftLine = {
  key: string;
  instrumentId: string;
  weight: string;
};

function isNotFound(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 404;
}

/** P12: only a stale rowVersion shows the conflict notice; any other 409 code shows the server detail. */
function isStaleRowVersion(error: unknown) {
  return problemCode(error) === staleRowVersion;
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
  const [confirmed, setConfirmed] = useState<string[]>([]);
  const [listProposals] = useLazyGetRebalancesQuery();
  const [confirmRebalance] = useConfirmRebalanceMutation();
  const lineSeq = useRef(0);

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

  /** P20: confirms each Proposed rebalance of the portfolio in turn, then says how many were confirmed and why the
   * others were refused. There is no bulk route (rebalances §11 point 4). */
  async function onConfirmProposals() {
    if (!id) {
      return;
    }
    const proposals = (await listProposals({ portfolioId: id, status: "Proposed", pageSize: 100 }).unwrap()).items;
    if (proposals.length === 0) {
      setConfirmed(["No proposals wait for confirmation."]);
      return;
    }
    if (!window.confirm(`Confirm ${proposals.length} proposals?`)) {
      return;
    }
    let count = 0;
    const refusals: string[] = [];
    for (const proposal of proposals) {
      try {
        await confirmRebalance({ id: proposal.id, rowVersion: proposal.rowVersion }).unwrap();
        count++;
      } catch (error: unknown) {
        const reasons = problemMessages(error);
        refusals.push(reasons.length > 0 ? reasons.join(" ") : "This rebalance has changed.");
      }
    }
    setConfirmed([`${count} of ${proposals.length} proposals confirmed.`, ...refusals]);
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
      if (isStaleRowVersion(error)) {
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
      if (!window.confirm("This will propose a rebalance for every account assigned to this portfolio. Nothing is traded until each proposal is confirmed. Continue?")) {
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
      if (isStaleRowVersion(error)) {
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
      {queued ? <p>Proposals queued.</p> : null}
      <Messages messages={confirmed} />
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
                  <SelectItem value="">Select an instrument</SelectItem>
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
                {lines.length > 1 ? (
                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => setLines((current) => current.filter((item) => item.key !== line.key))}
                  >
                    Remove line
                  </Button>
                ) : null}
              </div>
            ))}
            <Button
              type="button"
              variant="outline"
              onClick={() => {
                lineSeq.current += 1;
                const key = `line-${lineSeq.current}`;
                setLines((current) => [...current, { key, instrumentId: "", weight: "" }]);
              }}
            >
              Add line
            </Button>
            <Button type="submit">Save</Button>
          </form>
        </CardContent>
      </Card>
      <Section
        title="Assigned accounts"
        actions={
          <div className="flex flex-wrap gap-2">
            <Button type="button" variant="outline" onClick={() => void onRebalance()}>
              Rebalance accounts
            </Button>
            <Button type="button" variant="outline" onClick={() => void onConfirmProposals()}>
              Confirm proposals
            </Button>
          </div>
        }
      >
        {assigned.data.length === 0 ? <Muted>No accounts are assigned to this portfolio.</Muted> : (
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Account</TableHead>
                    <TableHead>Rebalance</TableHead>
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
                        <AssignmentRebalanceLine rebalance={row.assignment.rebalance} as="span" />
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
