// Account custody mode labels for Customers (customer-portal amendment 2026-11-17).
// A value the portal does not know is shown as received.
const custodyLabels: Record<string, string> = {
  External: "Held elsewhere",
  Custodial: "Held on platform",
};

// The note on figures the platform has not checked (custody-mode-grouping R15). The same sentence is in the
// Customer Transactions PDF; keep the two the same.
export const externalNote =
  "Held elsewhere: recorded by your adviser from outside statements. The platform has not checked these figures.";

export function custodyLabel(custodyMode: string) {
  return custodyLabels[custodyMode] ?? custodyMode;
}
