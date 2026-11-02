import { Children, cloneElement, isValidElement, type ComponentProps, type ReactNode } from "react";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Field, FieldLabel } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectGroup, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { cn } from "cn";

export function Page({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cn("mx-auto flex w-full max-w-6xl flex-col gap-6", className)}>{children}</div>;
}

export function PageHeading({
  title,
  description,
  actions,
}: {
  title: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div className="space-y-1">
        <h1 className="text-lg font-semibold tracking-tight">{title}</h1>
        {description ? <p className="text-sm text-muted-foreground">{description}</p> : null}
      </div>
      {actions ? <div className="flex flex-wrap items-center gap-2">{actions}</div> : null}
    </div>
  );
}

export function Section({
  title,
  actions,
  children,
}: {
  title: string;
  actions?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-base font-medium">{title}</h2>
        {actions}
      </div>
      {children}
    </section>
  );
}

export function Muted({ children }: { children: ReactNode }) {
  return <p className="text-sm text-muted-foreground">{children}</p>;
}

export function ErrorNotice({ children }: { children: ReactNode }) {
  return (
    <Alert variant="destructive">
      <AlertDescription>{children}</AlertDescription>
    </Alert>
  );
}

export function Messages({ messages }: { messages: string[] }) {
  if (messages.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-col gap-2">
      {messages.map((message) => (
        <ErrorNotice key={message}>{message}</ErrorNotice>
      ))}
    </div>
  );
}

export function ConflictNotice() {
  return (
    <Alert>
      <AlertDescription>This row changed. Reload and try again.</AlertDescription>
    </Alert>
  );
}

const quietStatuses = new Set(["closed", "disabled", "inactive", "failed"]);

export function StatusBadge({ status }: { status: string }) {
  return <Badge variant={quietStatuses.has(status.toLowerCase()) ? "outline" : "secondary"}>{status}</Badge>;
}

export function DetailGrid({ children }: { children: ReactNode }) {
  return <dl className="grid gap-4 sm:grid-cols-2">{children}</dl>;
}

export function DetailItem({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="space-y-1">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-sm break-all">{children}</dd>
    </div>
  );
}

export function FilterBar({ children }: { children: ReactNode }) {
  return <div className="flex flex-wrap items-end gap-3">{children}</div>;
}

export function TextField({ id, label, className, ...props }: ComponentProps<typeof Input> & { id: string; label: string }) {
  return (
    <Field>
      <FieldLabel htmlFor={id}>{label}</FieldLabel>
      <Input id={id} className={className} {...props} />
    </Field>
  );
}

function itemLabel(node: ReactNode): string {
  if (typeof node === "string" || typeof node === "number") {
    return String(node);
  }
  if (Array.isArray(node)) {
    return node.map(itemLabel).join("");
  }
  if (isValidElement<{ children?: ReactNode }>(node)) {
    return itemLabel(node.props.children);
  }
  return "";
}

function readItems(children: ReactNode) {
  const items: { label: string; value: string | null }[] = [];
  Children.forEach(children, (child) => {
    if (!isValidElement<{ value?: string | null; children?: ReactNode }>(child) || child.type !== SelectItem) {
      return;
    }
    const raw = child.props.value;
    items.push({
      label: itemLabel(child.props.children).trim(),
      value: raw === "" || raw == null ? null : String(raw),
    });
  });
  return items;
}

function withNullEmpty(children: ReactNode) {
  return Children.map(children, (child) => {
    if (!isValidElement<{ value?: string | null }>(child) || child.type !== SelectItem) {
      return child;
    }
    if (child.props.value === "" || child.props.value == null) {
      return cloneElement(child, { value: null });
    }
    return child;
  });
}

export function SelectField({
  id,
  label,
  className,
  value,
  onChange,
  required,
  children,
}: {
  id: string;
  label: string;
  className?: string;
  value?: string;
  onChange?: (event: { target: { value: string } }) => void;
  required?: boolean;
  children?: ReactNode;
}) {
  const selected = value === "" || value == null ? null : value;

  return (
    <Field>
      <FieldLabel htmlFor={id}>{label}</FieldLabel>
      <Select
        items={readItems(children)}
        value={selected}
        onValueChange={(next) => onChange?.({ target: { value: next == null ? "" : String(next) } })}
        required={required}
      >
        <SelectTrigger id={id} className={cn("w-full", className)}>
          <SelectValue />
        </SelectTrigger>
        <SelectContent alignItemWithTrigger={false}>
          <SelectGroup>{withNullEmpty(children)}</SelectGroup>
        </SelectContent>
      </Select>
    </Field>
  );
}

export function CheckField({ children, ...props }: ComponentProps<"input"> & { children: ReactNode }) {
  return (
    <label className="flex h-7 items-center gap-2 text-sm">
      <input
        {...props}
        type="checkbox"
        className="size-4 shrink-0 rounded-[4px] border border-input accent-primary"
      />
      {children}
    </label>
  );
}

export const recordLinkClass = "font-medium underline-offset-4 hover:underline";
