import { type FormEvent, useState } from "react";
import { CheckField, Messages, Muted, SelectField, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { useGetAdvisersQuery } from "@/features/advisers/advisersApi";
import { fromCron, humaniseCron, toCron, type CronPreset } from "@/features/reports/cron";
import {
  useCreateReportScheduleMutation,
  useDisableReportScheduleMutation,
  useGetReportSchedulesQuery,
  useUpdateReportScheduleMutation,
  type ReportCatalogItem,
  type ReportParameters,
  type ReportSchedule,
  type ReportScope,
} from "@/features/reports/reportsApi";
import { roles } from "@/features/session/roles";

function isStatus(error: unknown, status: number) {
  return typeof error === "object" && error !== null && "status" in error && error.status === status;
}

function allowsScheduled(report: ReportCatalogItem | undefined) {
  return report?.allowedTriggers.includes("scheduled") ?? false;
}

export function ReportSchedules({
  reports,
  scope,
  selected,
  customerId,
  role,
}: {
  reports: ReportCatalogItem[];
  scope: ReportScope;
  selected: ReportCatalogItem | null;
  customerId?: string;
  role: string;
}) {
  const schedules = useGetReportSchedulesQuery({ scope });
  const [filterId, setFilterId] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [includeClosedAccounts, setIncludeClosedAccounts] = useState(false);
  const [adviserId, setAdviserId] = useState("");
  const [preset, setPreset] = useState<CronPreset>("daily");
  const [time, setTime] = useState("09:00");
  const [isActive, setIsActive] = useState(true);
  const [messages, setMessages] = useState<string[]>([]);
  const [saved, setSaved] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [createSchedule, { isLoading: isCreating }] = useCreateReportScheduleMutation();
  const [updateSchedule, { isLoading: isUpdating }] = useUpdateReportScheduleMutation();
  const [disableSchedule] = useDisableReportScheduleMutation();
  const isLoading = isCreating || isUpdating;
  const rows = (schedules.data ?? []).filter((item) => {
    if (!item.parameters || typeof item.cron !== "string") {
      return false;
    }
    if (filterId && item.reportId !== filterId) {
      return false;
    }
    if (customerId && item.parameters.customerId !== customerId) {
      return false;
    }
    return true;
  });
  const editing = rows.find((item) => item.id === editingId) ?? null;
  const formReport = editing ? reports.find((item) => item.id === editing.reportId) ?? selected : selected;
  const canSchedule = allowsScheduled(selected ?? undefined);
  const customerReport = formReport?.code === "customer-transactions";
  const needsAdviser = formReport?.code === "adviser-balances" && role === roles.tenantAdmin;
  const { data: advisers } = useGetAdvisersQuery(
    { page: 1, pageSize: 100, enabledOnly: true },
    { skip: !needsAdviser },
  );
  const blocked = needsAdviser && adviserId === "" && editingId === null;

  function reportName(id: string) {
    return reports.find((item) => item.id === id)?.name ?? id;
  }

  function beginEdit(item: ReportSchedule) {
    setEditingId(item.id);
    setFrom(typeof item.parameters.from === "string" ? item.parameters.from : "");
    setTo(typeof item.parameters.to === "string" ? item.parameters.to : "");
    setIncludeClosedAccounts(item.parameters.includeClosedAccounts === true);
    setAdviserId(typeof item.parameters.adviserId === "string" ? item.parameters.adviserId : "");
    setIsActive(item.isActive);
    const parsed = fromCron(item.cron);
    if (parsed) {
      setPreset(parsed.preset);
      setTime(parsed.time);
    }
    setMessages([]);
    setSaved(false);
  }

  async function onDisable(id: string) {
    await disableSchedule(id).unwrap();
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!formReport) {
      return;
    }
    if (needsAdviser && adviserId === "") {
      return;
    }
    if (customerReport && to < from) {
      setMessages(["To must be on or after from."]);
      return;
    }

    const parameters: ReportParameters = customerReport
      ? { customerId, from, to, includeClosedAccounts }
      : needsAdviser
        ? { adviserId, includeClosedAccounts }
        : { includeClosedAccounts };
    const cron = toCron(preset, time);

    try {
      if (editing) {
        await updateSchedule({
          id: editing.id,
          reportId: formReport.id,
          parameters,
          cron,
          isActive,
          rowVersion: editing.rowVersion,
        }).unwrap();
      } else {
        await createSchedule({
          reportId: formReport.id,
          parameters,
          cron,
          isActive,
        }).unwrap();
      }
      setMessages([]);
      setSaved(true);
      setEditingId(null);
    } catch (error: unknown) {
      setSaved(false);
      if (isStatus(error, 409)) {
        setMessages(["This row changed. Reload and try again."]);
        return;
      }
      setMessages(error instanceof Error ? [error.message] : ["Schedules could not be loaded."]);
    }
  }

  return (
    <section className="flex w-full flex-col gap-3">
      <h2 className="text-base font-medium">Schedules</h2>
      <SelectField
        id={`schedule-report-${scope}`}
        label="Schedule report"
        value={filterId}
        onChange={(event) => setFilterId(event.target.value)}
      >
        <option value="">All</option>
        {reports.map((item) => (
          <option key={item.id} value={item.id}>
            {item.name}
          </option>
        ))}
      </SelectField>
      {schedules.isLoading ? <Muted>Loading schedules…</Muted> : null}
      {schedules.isError ? (
        <div className="flex flex-col items-start gap-2 text-sm">
          <p>Schedules could not be loaded.</p>
          <Button type="button" variant="outline" size="sm" onClick={() => void schedules.refetch()}>
            Retry
          </Button>
        </div>
      ) : null}
      {!schedules.isLoading && !schedules.isError && rows.length === 0 ? <Muted>No schedules.</Muted> : null}
      {rows.map((item) => (
        <div key={item.id} className="flex flex-wrap items-center gap-2 rounded-md border bg-card px-2 py-1.5 text-sm">
          <span>{reportName(item.reportId)}</span>
          <span>{humaniseCron(item.cron)}</span>
          {typeof item.parameters?.from === "string" ? <span>{item.parameters.from}</span> : null}
          {typeof item.parameters?.to === "string" ? <span>{item.parameters.to}</span> : null}
          {item.isActive ? (
            <>
              {allowsScheduled(reports.find((report) => report.id === item.reportId)) ? (
                <Button type="button" variant="outline" size="sm" onClick={() => beginEdit(item)}>
                  Edit
                </Button>
              ) : null}
              <Button type="button" variant="destructive" size="sm" onClick={() => void onDisable(item.id)}>
                Disable schedule
              </Button>
            </>
          ) : (
            <span>Inactive</span>
          )}
        </div>
      ))}
      {canSchedule && formReport ? (
        <form className="flex w-full flex-col gap-3 rounded-lg bg-muted/40 p-3" onSubmit={(event) => void onSubmit(event)}>
          <p className="text-sm text-muted-foreground">Times are sent as chosen. The server interprets the schedule in UTC.</p>
          <Messages messages={messages} />
          {saved ? <p className="text-sm">Schedule saved</p> : null}
          {customerReport ? (
            <>
              <TextField
                id={`schedule-from-${formReport.id}`}
                label="From"
                type="date"
                value={from}
                onChange={(event) => setFrom(event.target.value)}
                required
              />
              <TextField
                id={`schedule-to-${formReport.id}`}
                label="To"
                type="date"
                value={to}
                onChange={(event) => setTo(event.target.value)}
                required
              />
            </>
          ) : null}
          {needsAdviser ? (
            <SelectField
              id={`schedule-adviser-${formReport.id}`}
              label="Adviser"
              value={adviserId}
              onChange={(event) => setAdviserId(event.target.value)}
              required
            >
              <option value="">Select an adviser</option>
              {advisers?.items.map((adviser) => (
                <option key={adviser.id} value={adviser.id}>
                  {adviser.name}
                </option>
              ))}
            </SelectField>
          ) : null}
          <CheckField
            checked={includeClosedAccounts}
            onChange={(event) => setIncludeClosedAccounts(event.target.checked)}
          >
            Include closed accounts
          </CheckField>
          <SelectField
            id={`schedule-frequency-${formReport.id}`}
            label="Frequency"
            value={preset}
            onChange={(event) => setPreset(event.target.value as CronPreset)}
          >
            <option value="daily">Daily</option>
            <option value="weekly">Weekly</option>
            <option value="monthly">Monthly</option>
          </SelectField>
          <TextField
            id={`schedule-time-${formReport.id}`}
            label="Time"
            type="time"
            value={time}
            onChange={(event) => setTime(event.target.value)}
            required
          />
          <CheckField checked={isActive} onChange={(event) => setIsActive(event.target.checked)}>
            Active
          </CheckField>
          <Button type="submit" size="sm" disabled={isLoading || blocked}>
            {editing ? "Save schedule" : "Create schedule"}
          </Button>
        </form>
      ) : null}
    </section>
  );
}
