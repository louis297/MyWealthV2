import { useState } from "react";
import { useNavigate } from "react-router";
import { Page, PageHeading } from "@/components/portal";
import { useCreateTradeScheduleMutation } from "@/features/fees/feesApi";
import { draftValues, type FieldErrors, problemErrors, type ScheduleDraft } from "@/features/fees/scheduleDraft";
import { emptyDraft, TradeScheduleForm } from "@/features/fees/TradeScheduleForm";

export function CreateTradeSchedulePage() {
  const navigate = useNavigate();
  const [createSchedule] = useCreateTradeScheduleMutation();
  const [serverErrors, setServerErrors] = useState<FieldErrors>({});
  const [generalErrors, setGeneralErrors] = useState<string[]>([]);

  async function onSubmit(draft: ScheduleDraft) {
    setServerErrors({});
    setGeneralErrors([]);
    try {
      // F5: no tenantId; the token's tenant owns the row.
      const created = await createSchedule(draftValues(draft)).unwrap();
      void navigate(`/fees/trade/${created.id}`);
    } catch (error: unknown) {
      const problems = problemErrors(error);
      setServerErrors(problems.fields);
      setGeneralErrors(problems.general);
    }
  }

  return (
    <Page className="max-w-2xl">
      <PageHeading title="New trade fee schedule" />
      <TradeScheduleForm
        initial={emptyDraft()}
        currencyLocked={false}
        submitLabel="Create schedule"
        serverErrors={serverErrors}
        generalErrors={generalErrors}
        onSubmit={(draft) => void onSubmit(draft)}
      />
    </Page>
  );
}
