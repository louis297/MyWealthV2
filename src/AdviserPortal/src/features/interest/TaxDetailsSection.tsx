import { type FormEvent, useState } from "react";
import { CheckField, DetailGrid, DetailItem, Messages, Muted, Section, SelectField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { type TaxProfile, useGetCustomerTaxProfileQuery, useSetCustomerTaxProfileMutation } from "@/features/interest/interestApi";
import {
  basisWords,
  irdNotStoredSentence,
  residencyLabel,
  taxKindLabel,
  taxProfileConflictSentence,
} from "@/features/interest/interestText";
import { problemMessages, problemStatus } from "@/features/statementLines/statementLineLabels";

const rates = [10.5, 17.5, 30, 33, 39];

/** D58: "Tax withheld from interest: RWT 17.5 % (chosen rate)". */
export function withholdingLine(profile: TaxProfile) {
  const { kind, ratePercent, basis } = profile.withholding;
  return `Tax withheld from interest: ${taxKindLabel(kind)} ${ratePercent} % (${basisWords(basis)})`;
}

function TaxProfileForm({
  customerId,
  profile,
  onDone,
}: {
  customerId: string;
  profile: TaxProfile;
  onDone: () => void;
}) {
  const [residency, setResidency] = useState<string>(profile.taxResidency);
  const [irdHeld, setIrdHeld] = useState(profile.irdNumberHeld);
  const [rate, setRate] = useState(profile.rwtRatePercent === null ? "" : String(profile.rwtRatePercent));
  const [fields, setFields] = useState<Record<string, string[]>>({});
  const [messages, setMessages] = useState<string[]>([]);
  const [save, { isLoading }] = useSetCustomerTaxProfileMutation();
  const rateAllowed = residency === "NewZealand" && irdHeld;

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    try {
      await save({
        customerId,
        body: {
          taxResidency: residency,
          irdNumberHeld: irdHeld,
          rwtRatePercent: rateAllowed && rate !== "" ? Number(rate) : null,
          rowVersion: profile.rowVersion,
        },
      }).unwrap();
      onDone();
    } catch (error: unknown) {
      if (problemStatus(error) === 409) {
        setFields({});
        setMessages([taxProfileConflictSentence]);
        return;
      }
      const result = problemMessages(error);
      const lower: Record<string, string[]> = {};
      for (const [key, value] of Object.entries(result.fields)) {
        lower[key.toLowerCase()] = value;
      }
      setFields(lower);
      setMessages(result.messages);
    }
  }

  return (
    <form className="flex flex-col gap-4" onSubmit={onSubmit}>
      <Messages messages={messages} />
      <SelectField id="tax-residency" label="Tax residency" value={residency} onChange={(event) => setResidency(event.target.value)}>
        <SelectItem value="NewZealand">New Zealand</SelectItem>
        <SelectItem value="Overseas">Overseas</SelectItem>
      </SelectField>
      <Messages messages={fields.taxresidency ?? []} />
      <CheckField checked={irdHeld} onChange={(event) => setIrdHeld(event.target.checked)}>
        The firm holds the IRD number
      </CheckField>
      <Messages messages={fields.irdnumberheld ?? []} />
      <SelectField id="rwt-rate" label="RWT rate" value={rateAllowed ? rate : ""} disabled={!rateAllowed}
        onChange={(event) => setRate(event.target.value)}>
        <SelectItem value="">Not chosen</SelectItem>
        {rates.map((value) => (
          <SelectItem key={value} value={String(value)}>{`${value} %`}</SelectItem>
        ))}
      </SelectField>
      <Messages messages={fields.rwtratepercent ?? []} />
      <p className="text-muted-foreground text-sm">{irdNotStoredSentence}</p>
      <div className="flex gap-2">
        <Button type="submit" disabled={isLoading}>Save</Button>
        <Button type="button" variant="outline" onClick={onDone}>Cancel</Button>
      </div>
    </form>
  );
}

/** D58: the customer page's tax details, read from the tax profile and edited in place. */
export function TaxDetailsSection({ customerId }: { customerId: string }) {
  const { data, refetch } = useGetCustomerTaxProfileQuery(customerId);
  const [editing, setEditing] = useState(false);

  if (!data) {
    return null;
  }

  return (
    <Section
      title="Tax details"
      actions={editing ? null : <Button size="sm" variant="outline" onClick={() => setEditing(true)}>Edit</Button>}
    >
      <Card>
        <CardContent className="flex flex-col gap-3">
          {editing ? (
            <TaxProfileForm
              key={data.rowVersion ?? "new"}
              customerId={customerId}
              profile={data}
              onDone={() => {
                setEditing(false);
                void refetch();
              }}
            />
          ) : (
            <>
              {data.rowVersion === null ? <Muted>Not provided</Muted> : null}
              <DetailGrid>
                <DetailItem label="Tax residency">{residencyLabel(data.taxResidency)}</DetailItem>
                <DetailItem label="IRD number">{data.irdNumberHeld ? "Held" : "Not held"}</DetailItem>
                <DetailItem label="RWT rate">{data.rwtRatePercent === null ? "Not chosen" : `${data.rwtRatePercent} %`}</DetailItem>
              </DetailGrid>
              <p className="text-sm">{withholdingLine(data)}</p>
            </>
          )}
        </CardContent>
      </Card>
    </Section>
  );
}
