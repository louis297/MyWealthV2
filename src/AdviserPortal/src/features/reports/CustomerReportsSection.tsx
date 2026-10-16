import { useAppSelector } from "@/app/hooks";
import { CustomerTransactionsGenerate } from "@/features/reports/CustomerTransactionsGenerate";
import { ReportCatalog } from "@/features/reports/ReportCatalog";
import { ReportSchedules } from "@/features/reports/ReportSchedules";

export function CustomerReportsSection({ customerId }: { customerId: string }) {
  const role = useAppSelector((state) => state.session.currentUser?.role) ?? "";

  return (
    <ReportCatalog
      scope="Customer"
      title="h2"
      note="These run for this customer."
      emptyMessage="No reports available for this customer."
      renderActions={(item) =>
        item.code === "customer-transactions" ? (
          <>
            <CustomerTransactionsGenerate reportId={item.id} customerId={customerId} />
            <ReportSchedules report={item} scope="Customer" customerId={customerId} role={role} />
          </>
        ) : null
      }
    />
  );
}
