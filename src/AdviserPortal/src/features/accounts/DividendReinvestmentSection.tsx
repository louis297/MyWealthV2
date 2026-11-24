import { useState } from "react";
import { CheckField, Messages, Section } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { type Account, useSetDividendReinvestmentMutation } from "@/features/accounts/accountsApi";
import { problemMessages, problemStatus } from "@/features/statementLines/statementLineLabels";

export const reinvestHelp = "Applies to dividends that offer a reinvestment plan. Tax is still paid in cash.";
export const followsPortfolioSentence = "Not used while the account follows a portfolio.";
const changedSentence = "The account changed. Check the switch and try again.";

/** dividends D99: the reinvestment election of a Custodial account and the dividends still to be paid. */
export function DividendReinvestmentSection({
  account,
  canManage,
  followsPortfolio,
}: {
  account: Account;
  canManage: boolean;
  followsPortfolio: boolean;
}) {
  const [save, { isLoading }] = useSetDividendReinvestmentMutation();
  const [messages, setMessages] = useState<string[]>([]);
  const pending = account.pendingDividends ?? 0;

  async function onChange(reinvest: boolean) {
    try {
      await save({ id: account.id, reinvest, rowVersion: account.rowVersion }).unwrap();
      setMessages([]);
    } catch (error: unknown) {
      if (problemStatus(error) === 409) {
        setMessages([changedSentence]);
        return;
      }
      const result = problemMessages(error);
      setMessages([...result.messages, ...Object.values(result.fields).flat()]);
    }
  }

  return (
    <Section title="Dividends">
      <Card>
        <CardContent className="flex flex-col gap-2">
          <Messages messages={messages} />
          <CheckField
            checked={account.reinvestDividends ?? false}
            disabled={!canManage || isLoading || account.status !== "Open"}
            onChange={(event) => void onChange(event.target.checked)}
          >
            Reinvest dividends
          </CheckField>
          <p className="text-muted-foreground text-sm">{reinvestHelp}</p>
          {followsPortfolio ? <p className="text-sm text-amber-600">{followsPortfolioSentence}</p> : null}
          {pending > 0 ? <p className="text-sm">{`${pending} dividend(s) to be paid`}</p> : null}
        </CardContent>
      </Card>
    </Section>
  );
}
