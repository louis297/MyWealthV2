import { useGetReportsQuery } from "@/features/reports/reportsApi";

export function CustomerReportsSection() {
  const { data, isLoading, isError, refetch } = useGetReportsQuery({ scope: "Customer" });

  return (
    <section className="space-y-2">
      <h2 className="text-base font-medium">Reports</h2>
      <p className="text-sm text-slate-600">These run for this customer.</p>
      {isLoading ? <p>Loading reports…</p> : null}
      {isError ? (
        <div className="space-y-2 text-sm">
          <p>Reports could not be loaded.</p>
          <button type="button" onClick={() => void refetch()}>
            Retry
          </button>
        </div>
      ) : null}
      {!isLoading && !isError && data && data.length === 0 ? (
        <p>No reports available for this customer.</p>
      ) : null}
      {data && data.length > 0 ? (
        <table className="w-full text-left text-sm">
          <thead>
            <tr className="border-b border-slate-200">
              <th className="py-2">Name</th>
              <th>Function</th>
            </tr>
          </thead>
          <tbody>
            {data.map((item) => (
              <tr key={item.id} className="border-b border-slate-100">
                <td className="py-2">{item.name}</td>
                <td className="max-w-md truncate" title={item.function}>
                  {item.function}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : null}
    </section>
  );
}
