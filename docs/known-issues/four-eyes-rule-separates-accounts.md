---
title: "Known issue — the four-eyes rule separates accounts, not people"
status: open
language: en
created: 2026-11-27
updated: 2026-11-27
related:
  - ../features/withdrawals.md
  - ../features/in-kind-transfers.md
  - ../features/account-closure.md
  - ../features/advisers.md
  - ../features/identity-auth.md
  - ../custodial-roadmap.md
  - ../adr/0013-roles-authorization-single-user-table.md
---

# Known issue: the four-eyes rule separates accounts, not people

**Status: open.** Accepted for the showcase. Read this before a real firm or a real bank connection uses withdrawals or in-kind transfers.

## Problem

The four-eyes rule on cash and securities that leave a Custodial account compares two person ids:

- a nominated bank account is verified by a TenantAdmin other than the person who added it ([withdrawals.md](../features/withdrawals.md));
- a withdrawal is approved by a TenantAdmin other than its requester ([withdrawals.md](../features/withdrawals.md)); the payout of an account closure is an ordinary withdrawal ([account-closure.md](../features/account-closure.md));
- an in-kind transfer is approved by a TenantAdmin other than its requester ([in-kind-transfers.md](../features/in-kind-transfers.md)).

The rule does not check that the two ids belong to two people, and a TenantAdmin decides who the other person is:

- A TenantAdmin creates Advisers and chooses their password ([advisers.md](../features/advisers.md) R5). There is no invitation and no password change at first sign-in ([identity-auth.md](../features/identity-auth.md) §2).
- A TenantAdmin reassigns any Customer of the Tenant to any Adviser.

So one TenantAdmin who signs in as an Adviser whose password they know can act on both sides of a withdrawal or an outgoing in-kind transfer: add and request as the Adviser, verify and approve as themselves. Anyone who takes over one TenantAdmin's sign-in can do the same. The `worker` sends the payment straight to the bank through `IBankPayments`, and there is no release step at the bank, so the platform's rule is the last control.

Not affected: adjustment requests and migration batches. Both sides of those need a permission only a TenantAdmin holds, and only a SystemAdmin creates TenantAdmins.

## Why it is accepted now

- The platform is a showcase. Its tenants are demo firms and money moves through the simulator bank.
- [custodial-roadmap.md](../custodial-roadmap.md) D5 maps the approval permissions to TenantAdmin first; a separate operations role comes only when it is needed.
- Real platforms usually leave a firm's own user administration to the firm (complementary user entity controls in a SOC 1 / ISAE 3402 report), and add controls outside the application: a payment released at the bank with the bank's own credentials, and a notice to the Customer when a bank account is added. This platform has neither yet.

## What still holds

- Two different accounts always act. `Withdrawals.RequestedByUserId`, `Withdrawals.DecisionByUserId`, `NominatedBankAccounts.AddedByUserId`, `NominatedBankAccounts.ReviewedByUserId` and `Users.CreatedBy` record who did what.
- The bank reconciliation runs every day ([bank-reconciliation.md](../features/bank-reconciliation.md)).

## How to check

On the VPS, in the folder that holds `compose.yml` and `.env`, list the approved withdrawals where the approver also verified the nominated bank account and created the requesting person. It is a list to review by hand, not proof of misuse. In a Tenant with one TenantAdmin every withdrawal an Adviser requested matches, which is the gap itself.

```bash
SA=$(grep '^SQL_SA_PASSWORD=' .env | cut -d= -f2-)
docker compose exec sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA" -C -d MyWealthDbV2 -Q "
SELECT w.PublicId, w.Amount, w.Currency, w.DecisionAt,
       approver.Email AS ApprovedBy, requester.Email AS RequestedBy
FROM Withdrawals w
JOIN NominatedBankAccounts n ON n.TenantId = w.TenantId AND n.Id = w.NominatedBankAccountId
JOIN Users approver ON approver.Id = w.DecisionByUserId
JOIN Users requester ON requester.Id = w.RequestedByUserId
WHERE w.DecisionAction = 0
  AND n.ReviewedByUserId = w.DecisionByUserId
  AND requester.CreatedBy = approver.IdentityUserId
ORDER BY w.DecisionAt DESC"
```

`DecisionAction = 0` is `Approved`. `Users.CreatedBy` holds the creator's identity user id.

## Planned fix

Direction decided on 2026-11-27, not yet a spec: once approvals are one shared flow, **creating an Adviser needs the approval of a second TenantAdmin**, so one TenantAdmin cannot add the other person alone.

The approval closes the gap only together with these:

1. **The creator does not know the new person's password.** Either an invitation with a set-password link (reserved in [identity-auth.md](../features/identity-auth.md) §2; `IEmailSender` is the seam), or a password change the person must make at first sign-in. Without it, an approved, real Adviser whose first password the creator chose can still be used by the creator.
2. **Advisers that exist when the fix lands** change their password at their next sign-in, for the same reason.
3. **A Tenant with one TenantAdmin** cannot create Advisers any more. It needs a second TenantAdmin from the SystemAdmin first.

When the fix lands, set this issue to `resolved` and keep the file.

## What we are not doing now

- A separate operations role that releases payments ([custodial-roadmap.md](../custodial-roadmap.md) D5).
- A release step for payments at the bank.
- A notice to the Customer when a nominated bank account is added. The platform sends no email yet.
- A waiting period before a new nominated bank account can be paid.

## Revisit when

- A real firm uses the platform, or a bank connection other than the simulator sends payments.
- Approvals become one shared flow.
