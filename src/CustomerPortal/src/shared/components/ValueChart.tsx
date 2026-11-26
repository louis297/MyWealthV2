import { useState } from "react";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatAmount, formatMoney } from "@/shared/format/money";

/** One day of a chart: its total and, for an incomplete day, the reason. */
export type ChartPoint = { date: string; value: number; incomplete?: string | null };

const width = 640;
const height = 200;
const padLeft = 72;
const padRight = 16;
const padTop = 12;
const padBottom = 28;

/**
 * A line of daily totals as plain SVG (valuation-history §9): a vertical axis in the currency's major units, the dates
 * on the horizontal axis, a hollow marker on an incomplete day, the date and total of a point on hover or focus, and
 * every figure in a table under the chart.
 */
export function ValueChart({
  title,
  currency,
  decimalPlaces,
  points,
}: {
  title: string;
  currency: string;
  decimalPlaces?: number;
  points: ChartPoint[];
}) {
  const [active, setActive] = useState<string | null>(null);
  const label = (point: ChartPoint) =>
    `${point.date}: ${formatMoney(point.value, currency, decimalPlaces)}${point.incomplete ? `. ${point.incomplete}` : ""}`;
  const values = points.map((point) => point.value);
  const low = Math.floor(Math.min(...values, 0));
  const high = Math.max(Math.ceil(Math.max(...values)), low + 1);
  const x = (index: number) =>
    points.length === 1 ? padLeft + (width - padLeft - padRight) / 2 : padLeft + (index * (width - padLeft - padRight)) / (points.length - 1);
  const y = (value: number) => padTop + ((high - value) * (height - padTop - padBottom)) / (high - low);

  return (
    <figure className="flex flex-col gap-2">
      <svg role="img" aria-label={title} viewBox={`0 0 ${width} ${height}`} className="h-auto w-full text-primary">
        <line x1={padLeft} y1={padTop} x2={padLeft} y2={height - padBottom} className="stroke-border" />
        <line x1={padLeft} y1={height - padBottom} x2={width - padRight} y2={height - padBottom} className="stroke-border" />
        <text x={padLeft - 6} y={padTop + 4} textAnchor="end" className="fill-muted-foreground text-[10px]">
          {formatAmount(high, 0)}
        </text>
        <text x={padLeft - 6} y={height - padBottom} textAnchor="end" className="fill-muted-foreground text-[10px]">
          {formatAmount(low, 0)}
        </text>
        <text x={padLeft} y={height - 8} className="fill-muted-foreground text-[10px]">
          {points[0]?.date}
        </text>
        <text x={width - padRight} y={height - 8} textAnchor="end" className="fill-muted-foreground text-[10px]">
          {points[points.length - 1]?.date}
        </text>
        <polyline
          fill="none"
          stroke="currentColor"
          strokeWidth={2}
          points={points.map((point, index) => `${x(index)},${y(point.value)}`).join(" ")}
        />
        {points.map((point, index) => (
          <circle
            key={point.date}
            role="button"
            tabIndex={0}
            aria-label={label(point)}
            data-incomplete={point.incomplete ? "true" : "false"}
            cx={x(index)}
            cy={y(point.value)}
            r={point.incomplete ? 4 : 3}
            fill={point.incomplete ? "none" : "currentColor"}
            stroke="currentColor"
            strokeWidth={point.incomplete ? 2 : 1}
            onMouseEnter={() => setActive(label(point))}
            onMouseLeave={() => setActive(null)}
            onFocus={() => setActive(label(point))}
            onBlur={() => setActive(null)}
          />
        ))}
      </svg>
      <p role="status" className="min-h-5 text-sm text-muted-foreground">
        {active}
      </p>
      <details>
        <summary className="cursor-pointer text-sm">Show figures</summary>
        <Table aria-label={title}>
          <TableHeader>
            <TableRow>
              <TableHead>Date</TableHead>
              <TableHead>Total</TableHead>
              <TableHead>Note</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {points.map((point) => (
              <TableRow key={point.date}>
                <TableCell>{point.date}</TableCell>
                <TableCell>{formatMoney(point.value, currency, decimalPlaces)}</TableCell>
                <TableCell>{point.incomplete ?? ""}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </details>
    </figure>
  );
}
