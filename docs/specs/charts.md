# Charts

Status: Implemented.

## Summary
Aggregate results (grouped sums, counts, averages) can be shown as a chart with a Table/Chart switch above the result. The chart type is chosen by code from the shape of the data, never by the model. It applies to aggregate results only; plain record lists get no chart.

## Behavior
- The chart opens by default when the question contains a chart-intent word (chart, graph, trend, distribution, over time, and Turkish equivalents such as "grafik", "dağılım"); otherwise the table opens. The table is always one click away, and meaning is never conveyed by color alone.
- If a chart was requested but cannot be drawn, a short note states why (`ChartBuilder.WhyNot`: list, too few points, unsupported shape; `AnswerOutcome.ChartReason`).
- Presentation requests such as "show as a line chart" or "show the table" are handled in the widget without a model call: short sentences made only of chart/table/bar/line words switch the last answer's view, add a note when the requested type does not fit, and consume no quota.
- Type selection (`Application/Charts/ChartBuilder`):
  - one category: bar chart, horizontal bars when labels exceed 14 characters; no pie charts;
  - a single date unit (day, week, month, quarter, year): line chart;
  - year plus month (or quarter): one line per year when the month or quarter repeats across years, otherwise one chronological line;
  - more than 10 categories: the largest 10 plus "Other" for sum and count; for avg, min and max no "Other" is made, the first 10 are shown and a note reports the omitted categories;
  - empty periods in a time series are filled with 0 across the minimum-to-maximum range;
  - two categories, a single point, or a measure count other than one: no chart.
- A truncated aggregate shows a "partial data" note.
- Labels use display names and option labels. Raw numeric values travel separately (`ResultTable.Raw`, only for aggregates) because the table cells are formatted text. Numbers and month names are formatted in the browser with `Intl` for the browser language. Currency symbols are not drawn.
- A "Download PNG" button exports the canvas (`canvas.toBlob`).

## Design
- **Date grouping** is the prerequisite for time series. `PlanValidator.CheckDateGrouping` accepts `dategrouping` of day, week, month, quarter or year, only on aggregate fetches, on a date-time attribute with `groupby="true"`. The plan prompt tells the model to group by month with two groupby entries (year and month) and to order by year, then month. `QueryExplainer` labels such groups ("Created On (month)"). Month labeling and ordering are done in code.
- A planner rule makes a chart or distribution request without a named dimension group by a single categorical field (status, priority, type, sector, city) while keeping the filters.
- `AnswerOutcome.Chart` is filled by the answer path; DTO `chart {kind, xKind, measure, fn, labels, series, hasOther, omitted, partial}`. Chain answers carry no chart.
- The chart renderer (`chartKit` block in `widget.js`, the same block in `result.js`) is a small built-in canvas drawer for line, bar and horizontal bar charts. No chart library and no CDN are used, and no inline script or style is needed (CSP-safe). The canvas has `role="img"` and an `aria-label` summary (type, highest and lowest values); series are separated by line pattern and direct labels; values are written on bars and points.
- Chart-intent detection and the presentation parser are pure functions with Node tests.

## Out of scope
Pie and donut charts, stacked bars, multiple axes, chart styling, saving to dashboards, a separate model call for charts, zero-filling from the question's date range (the minimum-to-maximum range is used), currency symbols.
