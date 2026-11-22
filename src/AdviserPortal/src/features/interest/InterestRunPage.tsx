import { useParams } from "react-router";
import { DetailGrid, DetailItem, Muted, Page, PageHeading } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { useGetInterestRunQuery } from "@/features/interest/interestApi";
import { FailedCount, useTotalsText } from "@/features/interest/InterestRunsPage";
import { runsForbiddenSentence, triggerLabel } from "@/features/interest/interestText";
import { problemStatus } from "@/features/statementLines/statementLineLabels";
import { formatInstant } from "@/shared/time/instant";

/** D60: one interest run with its posting date. */
export function InterestRunPage() {
  const { id } = useParams();
  const { data, error } = useGetInterestRunQuery(id ?? "", { skip: !id });
  const totals = useTotalsText();

  return (
    <Page className="max-w-2xl">
      <PageHeading title="Interest run" />
      {problemStatus(error) === 403 ? <Muted>{runsForbiddenSentence}</Muted> : null}
      {problemStatus(error) === 404 ? <Muted>Not found</Muted> : null}
      {data ? (
        <Card>
          <CardContent>
            <DetailGrid>
              <DetailItem label="As of">{data.asOfDate}</DetailItem>
              <DetailItem label="Posting date">{data.postingDate}</DetailItem>
              <DetailItem label="Trigger">{triggerLabel(data.trigger)}</DetailItem>
              <DetailItem label="Started">{formatInstant(data.startedAt)}</DetailItem>
              <DetailItem label="Accounts">{data.accountsProcessed}</DetailItem>
              <DetailItem label="Failed"><FailedCount count={data.accountsFailed} /></DetailItem>
              <DetailItem label="Interest paid">{totals(data, "grossAmount") || "—"}</DetailItem>
              <DetailItem label="Tax withheld">{totals(data, "taxAmount") || "—"}</DetailItem>
            </DetailGrid>
          </CardContent>
        </Card>
      ) : null}
    </Page>
  );
}
