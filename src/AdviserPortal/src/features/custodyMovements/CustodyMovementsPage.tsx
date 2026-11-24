import { useState } from "react";
import { Link, useSearchParams } from "react-router";
import { FilterBar, Messages, Muted, Page, PageHeading, recordLinkClass, SelectField, StatusBadge } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  ignoreHint,
  movementFilters,
  movementReasonLabel,
  movementsForbiddenSentence,
  movementStatusLabel,
} from "@/features/custodyMovements/custodyMovementLabels";
import {
  type CustodyMovement,
  useActOnCustodyMovementMutation,
  useGetCustodyMovementsQuery,
} from "@/features/custodyMovements/custodyMovementsApi";
import { NoteDialog } from "@/features/statementLines/NoteDialog";
import { problemMessages, problemStatus } from "@/features/statementLines/statementLineLabels";
import { Pager } from "@/shared/components/Pager";
import { formatInstant } from "@/shared/time/instant";

/** D90: a matched movement links to its transfer, order or corporate action. */
function MatchLink({ item }: { item: CustodyMovement }) {
  if (item.inKindTransferId) {
    return <Link to={`/transfers/${item.inKindTransferId}`} className={recordLinkClass}>Transfer</Link>;
  }
  if (item.orderId) {
    return <Link to={`/orders/${item.orderId}`} className={recordLinkClass}>Order</Link>;
  }
  if (item.corporateActionId) {
    return <Link to={`/corporate-actions/${item.corporateActionId}`} className={recordLinkClass}>Corporate action</Link>;
  }
  return null;
}

/** D90: the Tenant's custody movements, those in suspense by default, with Ignore and Reopen. */
export function CustodyMovementsPage() {
  const [params, setParams] = useSearchParams();
  const filter = movementFilters[params.get("status") ?? ""] ? params.get("status")! : "Unmatched";
  const page = Number(params.get("page") ?? "1") || 1;
  const instrumentId = params.get("instrumentId") ?? undefined;
  const { data, error } = useGetCustodyMovementsQuery({ status: movementFilters[filter].status, instrumentId, page });
  const [act, { isLoading }] = useActOnCustodyMovementMutation();
  const [ignoring, setIgnoring] = useState<CustodyMovement | null>(null);
  const [messages, setMessages] = useState<string[]>([]);

  function update(key: string, value: string | null) {
    const next = new URLSearchParams(params);
    if (value === null) {
      next.delete(key);
    } else {
      next.set(key, value);
    }
    if (key !== "page") {
      next.delete("page");
    }
    setParams(next);
  }

  async function run(item: CustodyMovement, action: "ignore" | "reopen", note: string | null) {
    try {
      await act({ id: item.id, action, note, rowVersion: item.rowVersion }).unwrap();
      setMessages([]);
    } catch (problem: unknown) {
      if (problemStatus(problem) === 409) {
        setMessages(["This movement has changed."]);
        return;
      }
      const result = problemMessages(problem);
      setMessages([...result.messages, ...Object.values(result.fields).flat()]);
    }
  }

  if (problemStatus(error) === 403) {
    return (
      <Page>
        <PageHeading title="Custody movements" />
        <Muted>{movementsForbiddenSentence}</Muted>
      </Page>
    );
  }

  return (
    <Page>
      <PageHeading title="Custody movements" />
      <Messages messages={messages} />
      <FilterBar>
        <SelectField id="movements-status" label="Status" className="w-56" value={filter}
          onChange={(event) => update("status", event.target.value || null)}>
          {Object.entries(movementFilters).map(([value, item]) => (
            <SelectItem key={value} value={value}>
              {item.label}
            </SelectItem>
          ))}
        </SelectField>
      </FilterBar>
      <Card className="gap-0 py-0">
        <CardContent className="px-0">
          <Table aria-label="Custody movements">
            <TableHeader>
              <TableRow>
                <TableHead>Received</TableHead>
                <TableHead>Direction</TableHead>
                <TableHead>Kind</TableHead>
                <TableHead>Instrument</TableHead>
                <TableHead>Quantity</TableHead>
                <TableHead>Counterparty</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Reason</TableHead>
                <TableHead>
                  <span className="sr-only">Actions</span>
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {(data?.items ?? []).map((item) => (
                <TableRow key={item.id}>
                  <TableCell>{formatInstant(item.receivedAt)}</TableCell>
                  <TableCell>{item.direction}</TableCell>
                  <TableCell>{item.kind ?? "Unknown"}</TableCell>
                  <TableCell>{item.instrument ? item.instrument.symbol : `${item.symbol} ${item.currency}`}</TableCell>
                  <TableCell>{item.quantity}</TableCell>
                  <TableCell>{item.counterpartyName ?? "—"}</TableCell>
                  <TableCell>
                    <StatusBadge status={movementStatusLabel(item.status)} />
                  </TableCell>
                  <TableCell>{movementReasonLabel(item.unmatchedReason)}</TableCell>
                  <TableCell>
                    {item.status === "Matched" ? <MatchLink item={item} /> : null}
                    {item.status === "Unmatched" ? (
                      <Button size="sm" variant="outline" disabled={isLoading} onClick={() => setIgnoring(item)}>Ignore</Button>
                    ) : null}
                    {item.status === "Ignored" ? (
                      <Button size="sm" variant="outline" disabled={isLoading} onClick={() => void run(item, "reopen", null)}>Reopen</Button>
                    ) : null}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
      <Pager page={page} pageSize={20} totalCount={data?.totalCount ?? 0} onPage={(next) => update("page", String(next))} />
      {ignoring ? (
        <NoteDialog
          open
          title="Ignore this movement"
          warning={ignoreHint(ignoring.unmatchedReason)}
          busy={isLoading}
          onConfirm={(note) => { const item = ignoring; setIgnoring(null); void run(item, "ignore", note); }}
          onClose={() => setIgnoring(null)}
        />
      ) : null}
    </Page>
  );
}
