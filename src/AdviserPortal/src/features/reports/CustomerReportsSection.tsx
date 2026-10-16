import { CustomerTransactionsGenerate } from "@/features/reports/CustomerTransactionsGenerate";
import { ReportCatalog } from "@/features/reports/ReportCatalog";

export function CustomerReportsSection({ customerId }: { customerId: string }) {
  return (
    <ReportCatalog
      scope="Customer"
      title="h2"
      note="These run for this customer."
      emptyMessage="No reports available for this customer."
      renderActions={(item) =>
        item.code === "customer-transactions" ? (
          <CustomerTransactionsGenerate reportId={item.id} customerId={customerId} />
        ) : null
      }
    />
  );
}
