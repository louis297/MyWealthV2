import { type FormEvent, useState } from "react";
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

export function ReportSchedules({
  report,
  scope,
  customerId,
  role,
}: {
  report: ReportCatalogItem;
  scope: ReportScope;
  customerId?: string;
  role: string;
}) {
  const [open, setOpen] = useState(false);
  const schedules = useGetReportSchedulesQuery({ scope }, { skip: !open });
  const needsAdviser = report.code === "adviser-balances" && role === roles.tenantAdmin;
  const { data: advisers } = useGetAdvisersQuery(
    { page: 1, pageSize: 100, enabledOnly: true },
    { skip: !open || !needsAdviser },
  );
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
  const customerReport = report.code === "customer-transactions";
  const blocked = needsAdviser && adviserId === "" && editingId === null;
  const rows = (schedules.data ?? []).filter((item) => {
    if (item.reportId !== report.id) {
      return false;
    }
    if (customerId && item.parameters.customerId !== customerId) {
      return false;
    }
    return true;
  });
  const editing = rows.find((item) => item.id === editingId) ?? null;

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
          reportId: report.id,
          parameters,
          cron,
          isActive,
          rowVersion: editing.rowVersion,
        }).unwrap();
      } else {
        await createSchedule({
          reportId: report.id,
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
    <div className="space-y-2">
      <button type="button" className="border border-slate-300 bg-white px-3 py-1 text-sm" onClick={() => setOpen((value) => !value)}>
        Schedules
      </button>
      {open ? (
        <div className="space-y-2">
          {schedules.isLoading ? <p>Loading schedules…</p> : null}
          {schedules.isError ? (
            <div className="space-y-2 text-sm">
              <p>Schedules could not be loaded.</p>
              <button type="button" onClick={() => void schedules.refetch()}>
                Retry
              </button>
            </div>
          ) : null}
          {!schedules.isLoading && !schedules.isError && rows.length === 0 ? <p>No schedules.</p> : null}
          {rows.map((item) => (
            <p key={item.id}>
              <span>{humaniseCron(item.cron)}</span>
              {typeof item.parameters.from === "string" ? <span>{item.parameters.from}</span> : null}
              {typeof item.parameters.to === "string" ? <span>{item.parameters.to}</span> : null}
              {item.isActive ? (
                <>
                  <button type="button" onClick={() => beginEdit(item)}>
                    Edit
                  </button>
                  <button type="button" onClick={() => void onDisable(item.id)}>
                    Disable schedule
                  </button>
                </>
              ) : (
                <span>Inactive</span>
              )}
            </p>
          ))}
          <form className="space-y-2" onSubmit={(event) => void onSubmit(event)}>
            <p className="text-sm text-slate-600">Times are sent as chosen. The server interprets the schedule in UTC.</p>
            {messages.map((message) => (
              <p key={message}>{message}</p>
            ))}
            {saved ? <p>Schedule saved</p> : null}
            {customerReport ? (
              <>
                <div>
                  <label htmlFor={`schedule-from-${report.id}`} className="block text-sm">
                    From
                  </label>
                  <input
                    id={`schedule-from-${report.id}`}
                    type="date"
                    className="mt-1 border border-slate-300 px-2 py-1"
                    value={from}
                    onChange={(event) => setFrom(event.target.value)}
                    required
                  />
                </div>
                <div>
                  <label htmlFor={`schedule-to-${report.id}`} className="block text-sm">
                    To
                  </label>
                  <input
                    id={`schedule-to-${report.id}`}
                    type="date"
                    className="mt-1 border border-slate-300 px-2 py-1"
                    value={to}
                    onChange={(event) => setTo(event.target.value)}
                    required
                  />
                </div>
              </>
            ) : null}
            {needsAdviser ? (
              <div>
                <label htmlFor={`schedule-adviser-${report.id}`} className="block text-sm">
                  Adviser
                </label>
                <select
                  id={`schedule-adviser-${report.id}`}
                  className="mt-1 w-full border border-slate-300 px-2 py-1"
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
                </select>
              </div>
            ) : null}
            <label className="flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={includeClosedAccounts}
                onChange={(event) => setIncludeClosedAccounts(event.target.checked)}
              />
              Include closed accounts
            </label>
            <div>
              <label htmlFor={`schedule-frequency-${report.id}`} className="block text-sm">
                Frequency
              </label>
              <select
                id={`schedule-frequency-${report.id}`}
                className="mt-1 border border-slate-300 px-2 py-1"
                value={preset}
                onChange={(event) => setPreset(event.target.value as CronPreset)}
              >
                <option value="daily">Daily</option>
                <option value="weekly">Weekly</option>
                <option value="monthly">Monthly</option>
              </select>
            </div>
            <div>
              <label htmlFor={`schedule-time-${report.id}`} className="block text-sm">
                Time
              </label>
              <input
                id={`schedule-time-${report.id}`}
                type="time"
                className="mt-1 border border-slate-300 px-2 py-1"
                value={time}
                onChange={(event) => setTime(event.target.value)}
                required
              />
            </div>
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} />
              Active
            </label>
            <button
              type="submit"
              className="border border-slate-300 bg-white px-3 py-1 text-sm"
              disabled={isLoading || blocked}
            >
              {editing ? "Save schedule" : "Create schedule"}
            </button>
          </form>
        </div>
      ) : null}
    </div>
  );
}
