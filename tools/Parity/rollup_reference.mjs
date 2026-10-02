// Runs 1.x's own bill-screen JavaScript on the cases in rollup_cases.json and writes what it
// produces to rollup_expected.json, for RollupTests to compare against.
//
// The functions are lifted out of app/templates/work_orders/bill.html at run time, not
// copied, so this always tests the real 1.x code.
//
//   node tools/Parity/rollup_reference.mjs        (from the project root)

import { readFileSync, writeFileSync } from "node:fs";

const root = new URL("../../", import.meta.url);
const html = readFileSync(new URL("app/templates/work_orders/bill.html", root), "utf8");
const fixtures = new URL("tests/ProfitDjinn.Tests/Fixtures/", root);

function slice(from, to) {
  const a = html.indexOf(from), b = html.indexOf(to, a);
  if (a < 0 || b < 0) throw new Error(`bill.html no longer contains "${from}" ... "${to}"`);
  return html.slice(a, b);
}

const source =
  slice("var TYPE_ORDER", "var HINTS") +
  slice("function dateRange(lines)", "/* ── Roll-up generators") +
  slice("function buildRollup(mode, sel)", "/* ── Invoice line rows");
const buildRollup = new Function(source + "\nreturn buildRollup;")();

// Python strftime('%b %d'), which 1.x put in each line's `date`.
const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
const shortDate = (iso) => (iso ? `${MONTHS[+iso.slice(5, 7) - 1]} ${iso.slice(8, 10)}` : "");

// What addRow() puts in the text boxes, and what syncHidden()/updateTotal() then read back.
const qtyText = (qty) => String(qty !== undefined ? qty : 1);
const unitText = (unit) => (parseFloat(unit) || 0).toFixed(2);

const cases = JSON.parse(readFileSync(new URL("rollup_cases.json", fixtures), "utf8")).cases;
const out = cases.map((c) => {
  const sel = c.lines.map((l) => ({ ...l, date: shortDate(l.iso) }));
  const selectedTotal = sel.reduce((s, l) => s + (l.noCharge ? 0 : l.amount), 0);
  const modes = {};
  for (const mode of ["one", "type", "detailed"]) {
    const rows = buildRollup(mode, sel).map((r) => ({ description: r.desc, quantity: qtyText(r.qty), unit_price: unitText(r.unit) }));
    let total = 0;
    for (const r of rows) total += (parseFloat(r.quantity) || 1) * (parseFloat(r.unit_price) || 0);
    const diff = total - selectedTotal;
    modes[mode] = { rows, total, mismatch: Math.abs(diff) >= 0.01 && rows.length > 0 ? diff : null };
  }
  return { name: c.name, selected_total: selectedTotal, modes };
});

writeFileSync(new URL("rollup_expected.json", fixtures), JSON.stringify({ cases: out }, null, 1) + "\n");
console.log(`Wrote rollup_expected.json (${out.length} cases)`);
