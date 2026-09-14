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
    <div className="flex gap-2 text-sm">
      <button type="button" disabled={page <= 1} onClick={() => onPage(page - 1)}>
        Previous
      </button>
      <span>
        {page} / {last}
      </span>
      <button type="button" disabled={page >= last} onClick={() => onPage(page + 1)}>
        Next
      </button>
    </div>
  );
}
