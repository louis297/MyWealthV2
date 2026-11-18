import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useAppSelector } from "@/app/hooks";
import { DetailGrid, DetailItem, Messages, Muted, Page, PageHeading } from "@/components/portal";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button, buttonVariants } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { FeePreviewPanel, useFeePreview } from "@/features/fees/FeePreview";
import { useDeleteTradeScheduleMutation, useGetTradeScheduleQuery } from "@/features/fees/feesApi";
import { inEffectMessage } from "@/features/fees/inEffect";
import { problemErrors } from "@/features/fees/scheduleDraft";
import { canManageFees } from "@/features/session/roles";
import { feeScheduleInEffect, problemCode } from "@/shared/api/problem";
import { formatMoney } from "@/shared/format/money";
import { custodialOnlyLine } from "@/features/fees/feeText";
import { cn } from "cn";

function isNotFound(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 404;
}

export function TradeScheduleDetailPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const role = useAppSelector((state) => state.session.currentUser?.role);
  const schedule = useGetTradeScheduleQuery(id ?? "", { skip: !id });
  const currencies = useGetCurrenciesQuery();
  const [deleteSchedule] = useDeleteTradeScheduleMutation();
  const [refused, setRefused] = useState(false);
  const [messages, setMessages] = useState<string[]>([]);
  const data = schedule.data;
  const preview = useFeePreview(
    data ? { quoteCurrency: data.quoteCurrency, minimum: data.minimum, cap: data.cap, bands: data.bands } : null,
  );

  async function onDelete() {
    if (!data) {
      return;
    }
    const question = `Delete this schedule? Trades from ${data.effectiveFrom} will use the schedule before it, or have no brokerage if there is none.`;
    if (!window.confirm(question)) {
      return;
    }
    try {
      await deleteSchedule(data.id).unwrap();
      void navigate("/fees/trade");
    } catch (error: unknown) {
      // F9: branch on the code, not on 409 alone.
      if (problemCode(error) === feeScheduleInEffect) {
        setRefused(true);
        setMessages([]);
        void schedule.refetch();
        return;
      }
      setMessages(problemErrors(error).general);
    }
  }

  if (isNotFound(schedule.error)) {
    return <Muted>Not found</Muted>;
  }

  if (!data) {
    return <Muted>Loading…</Muted>;
  }

  const manage = canManageFees(role);
  const pending = !data.inEffect && !refused;
  const places = currencies.data?.find((item) => item.code === data.quoteCurrency)?.decimalPlaces;
  const money = (value: number) => formatMoney(value, data.quoteCurrency, places);

  return (
    <Page className="max-w-2xl">
      <PageHeading
        title="Trade fee schedule"
        description={custodialOnlyLine}
        actions={
          manage && pending ? (
            <>
              <Link to={`/fees/trade/${data.id}/edit`} className={cn(buttonVariants({ variant: "outline", size: "sm" }))}>
                Edit
              </Link>
              <Button type="button" variant="destructive" size="sm" onClick={() => void onDelete()}>
                Delete
              </Button>
            </>
          ) : null
        }
      />
      {manage && !pending ? (
        <Alert>
          <AlertDescription>{inEffectMessage}</AlertDescription>
        </Alert>
      ) : null}
      <Messages messages={messages} />
      <Card>
        <CardContent className="flex flex-col gap-4">
          <DetailGrid>
            <DetailItem label="Quote currency">{data.quoteCurrency}</DetailItem>
            <DetailItem label="Effective from">{`${data.effectiveFrom} UTC`}</DetailItem>
            <DetailItem label="Minimum">{money(data.minimum)}</DetailItem>
            <DetailItem label="Cap">{data.cap === null ? "None" : money(data.cap)}</DetailItem>
          </DetailGrid>
          <Table aria-label="Bands">
            <TableHeader>
              <TableRow>
                <TableHead>From</TableHead>
                <TableHead>Rate</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.bands.map((band) => (
                <TableRow key={band.from}>
                  <TableCell>{money(band.from)}</TableCell>
                  <TableCell>{`${band.ratePercent}%`}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
      <FeePreviewPanel preview={preview} decimalPlaces={places} />
    </Page>
  );
}
