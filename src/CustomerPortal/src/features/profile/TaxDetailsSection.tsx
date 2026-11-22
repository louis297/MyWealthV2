import { DetailGrid, DetailItem } from "@/components/portal";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { useGetMyTaxProfileQuery } from "@/features/profile/profileApi";

/** C28: the Customer's own tax details, read only. */
export function TaxDetailsSection() {
  const { data } = useGetMyTaxProfileQuery();
  if (!data) {
    return null;
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Tax details</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        <DetailGrid>
          <DetailItem label="Tax residency">{data.taxResidency === "Overseas" ? "Overseas" : "New Zealand"}</DetailItem>
          <DetailItem label="IRD number">{data.irdNumberHeld ? "On file" : "Not on file"}</DetailItem>
          <DetailItem label="RWT rate">{data.rwtRatePercent === null ? "Not chosen" : `${data.rwtRatePercent} %`}</DetailItem>
        </DetailGrid>
        <p className="text-sm">{`Tax taken from your interest: ${data.withholding.ratePercent} %`}</p>
        <p className="text-muted-foreground text-sm">To change these details, contact your adviser.</p>
      </CardContent>
    </Card>
  );
}
