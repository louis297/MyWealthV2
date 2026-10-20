import { Button } from "@/components/ui/button";

export function Pager({
  page,
  pageSize,
  totalCount,
  onPage,
}: {
  page: number;
  pageSize: number;
  totalCount: number;
  onPage: (page: number) => void;
}) {
  const last = Math.max(1, Math.ceil(totalCount / pageSize) || 1);

  return (
    <div className="flex items-center gap-2">
      <Button type="button" variant="outline" size="sm" disabled={page <= 1} onClick={() => onPage(page - 1)}>
        Previous
      </Button>
      <span className="text-sm text-muted-foreground tabular-nums">
        {page} / {last}
      </span>
      <Button type="button" variant="outline" size="sm" disabled={page >= last} onClick={() => onPage(page + 1)}>
        Next
      </Button>
    </div>
  );
}
