export const roles = {
  systemAdmin: "systemAdmin",
  tenantAdmin: "tenantAdmin",
  adviser: "adviser",
  customer: "customer",
} as const;

export function isCustomer(role: string | null | undefined) {
  return role === roles.customer;
}
