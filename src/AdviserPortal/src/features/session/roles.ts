export const roles = {
  systemAdmin: "systemAdmin",
  tenantAdmin: "tenantAdmin",
  adviser: "adviser",
  customer: "customer",
} as const;

export type Role = (typeof roles)[keyof typeof roles];

export function isRole(value: string | null | undefined): value is Role {
  return value === roles.systemAdmin
    || value === roles.tenantAdmin
    || value === roles.adviser
    || value === roles.customer;
}

export function shouldLoadTenant(role: string | null | undefined, tenantCode: string | null | undefined) {
  return Boolean(tenantCode) && (role === roles.tenantAdmin || role === roles.adviser);
}
