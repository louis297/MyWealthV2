import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { Muted, Page, PageHeading, recordLinkClass } from "@/components/portal";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { type TradeFeeSchedule, useGetTradeScheduleQuery, useUpdateTradeScheduleMutation } from "@/features/fees/feesApi";
import { inEffectMessage } from "@/features/fees/inEffect";
import { draftValues, type FieldErrors, problemErrors, type ScheduleDraft } from "@/features/fees/scheduleDraft";
import { TradeScheduleForm } from "@/features/fees/TradeScheduleForm";
import { feeScheduleInEffect, problemCode, staleRowVersion } from "@/shared/api/problem";

function isNotFound(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 404;
}

function draftFrom(row: TradeFeeSchedule): ScheduleDraft {
  return {
    quoteCurrency: row.quoteCurrency,
    effectiveFrom: row.effectiveFrom,
    minimum: String(row.minimum),
    cap: row.cap === null ? "" : String(row.cap),
    bands: row.bands.map((band, index) => ({
      key: `band-${index}`,
      from: String(band.from),
      ratePercent: String(band.ratePercent),
    })),
  };
}

export function EditTradeSchedulePage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const schedule = useGetTradeScheduleQuery(id ?? "", { skip: !id });
  const [updateSchedule] = useUpdateTradeScheduleMutation();
  const [stale, setStale] = useState(false);
  const [refused, setRefused] = useState(false);
  const [serverErrors, setServerErrors] = useState<FieldErrors>({});
  const [generalErrors, setGeneralErrors] = useState<string[]>([]);
  const data = schedule.data;

  async function onSubmit(draft: ScheduleDraft) {
    if (!data || stale) {
      return;
    }
    setServerErrors({});
    setGeneralErrors([]);
    // F8: no quoteCurrency (immutable, R11); the loaded rowVersion.
    const { quoteCurrency: _immutable, ...values } = draftValues(draft);
    try {
      await updateSchedule({ id: data.id, body: { ...values, rowVersion: data.rowVersion } }).unwrap();
      void navigate(`/fees/trade/${data.id}`);
    } catch (error: unknown) {
      // F9: branch on the code, not on 409 alone.
      const code = problemCode(error);
      if (code === staleRowVersion) {
        setStale(true);
        return;
      }
      if (code === feeScheduleInEffect) {
        setRefused(true);
        void schedule.refetch();
        return;
      }
      const problems = problemErrors(error);
      setServerErrors(problems.fields);
      setGeneralErrors(problems.general);
    }
  }

  async function onReload() {
    await schedule.refetch();
    setStale(false);
    setServerErrors({});
    setGeneralErrors([]);
  }

  if (isNotFound(schedule.error)) {
    return <Muted>Not found</Muted>;
  }

  if (!data) {
    return <Muted>Loading…</Muted>;
  }

  return (
    <Page className="max-w-2xl">
      <PageHeading title="Edit trade fee schedule" />
      {data.inEffect || refused ? (
        <Alert>
          <AlertDescription>
            {inEffectMessage}{" "}
            <Link to={`/fees/trade/${data.id}`} className={recordLinkClass}>
              Back to the schedule
            </Link>
          </AlertDescription>
        </Alert>
      ) : (
        <>
          {stale ? (
            <Alert>
              <AlertDescription className="flex flex-wrap items-center gap-3">
                Someone else changed this schedule. Reload to see the latest version.
                <Button type="button" variant="outline" size="sm" onClick={() => void onReload()}>
                  Reload
                </Button>
              </AlertDescription>
            </Alert>
          ) : null}
          <TradeScheduleForm
            key={data.rowVersion}
            initial={draftFrom(data)}
            currencyLocked
            submitLabel="Save schedule"
            serverErrors={serverErrors}
            generalErrors={generalErrors}
            submitDisabled={stale}
            onSubmit={(draft) => void onSubmit(draft)}
          />
        </>
      )}
    </Page>
  );
}
