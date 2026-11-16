// Account custody mode labels for Customers (customer-portal amendment 2026-11-17).
// A value the portal does not know is shown as received.
const custodyLabels: Record<string, string> = {
  External: "Held elsewhere",
  Custodial: "Held on platform",
};

export function custodyLabel(custodyMode: string) {
  return custodyLabels[custodyMode] ?? custodyMode;
}
