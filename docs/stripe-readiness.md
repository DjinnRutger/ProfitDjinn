# Stripe readiness

ProfitDjinn does not talk to Stripe. This note records how 2.4's recurring invoices were
shaped so that an **optional** Stripe integration (off by default, turned on per user) can be
added later without undoing anything. Check every Stripe detail below against Stripe's
current API reference when the work starts; it is a design aid, not a spec.

## What was built in on purpose (2.4)

- **One choke point.** A schedule becomes a real invoice only in
  `RecurringInvoiceService.Issue` / `IssueIn` (`src/ProfitDjinn.Core/Services`). Generation at
  start, Issue Now, early Print/PDF and the tests all go through it. A provider hooks in there;
  no screen creates invoices from a schedule on its own.
- **Stripe's vocabulary in the schema.** `recurring_invoices.interval` is `month` / `year`
  and `interval_count` is stored (always 1 today): the shape of a Stripe Price's
  `recurring.interval` and `recurring.interval_count`. `collection_method` holds Stripe's
  values: `send_invoice` (what ProfitDjinn does now: create it, the user sends it) and, later,
  `charge_automatically`.
- **Billing anchor and end.** `start_date` + `day_of_month` play the part of a subscription's
  billing cycle anchor, `end_date` of `cancel_at`, and pause / resume of pausing collection.
- **Payments already have one door.** Every payment goes through
  `InvoiceService.RecordPayment`. A Stripe payment would arrive (webhook or poll) and be
  recorded there with a new method value, so balances, account credit, the Revenue page and
  the Profit & Loss keep working unchanged.

## What was deliberately not built

- **No Stripe columns on existing tables.** Adding provider ids to `customers` or `invoices`
  would touch 1.x tables and would need undoing if the design changed. Instead, add one
  generic table in the Stripe phase, for example:

  ```
  external_links (id, provider, entity, local_id, external_id, data, synced_at, created_at)
    unique (provider, entity, local_id), unique (provider, external_id)
  ```

  entity = `customer` | `invoice` | `payment` | `recurring_invoice`; external_id = `cus_...`,
  `in_...`, `pi_...` / `ch_...`, `sub_...`. A new table is free in this schema
  (`Schema.Ensure` creates it; 1.x ignores it).
- **No settings rows yet.** The on/off switch (`stripe_enabled`) and the key belong to the
  Stripe phase. Raise the parity settings allowance in `ParityTests` when they are added.

## Known mismatches to handle in the Stripe phase

- **Quantities.** Stripe invoice item quantities are whole numbers; ProfitDjinn lines can be
  1.5 hours. Send such a line as quantity 1 with the line's total as the amount.
- **Money.** Stripe amounts are integer cents. Convert at the boundary with
  `PyMath.Round(x * 100)`; never change how ProfitDjinn stores money (FLOAT dollars, 1.x
  compatible).
- **Numbers.** Stripe numbers its own invoices. Keep ProfitDjinn's number as the local one
  and store Stripe's in `external_links.data`.
- **Who issues.** With `send_invoice` handed to Stripe, Stripe emails the invoice and hosts the
  payment page; with `charge_automatically` it charges a saved card. Either way ProfitDjinn
  should still create its own invoice row (through `Issue`) so reports stay complete.
- **Secrets.** A Stripe secret key must not live in the database file in plain text if
  backups are shared; store it with Windows DPAPI (per user) and keep it out of backups.

## Where to start

1. `external_links` table and a `StripeService` behind an interface, injected into `Store`.
2. A Settings > Payments section with the switch and the key, off by default.
3. In `IssueIn`, after the local invoice is written: when the schedule's
   `collection_method` is Stripe-handled and Stripe is on, create or finalize the Stripe
   invoice and link it. A failure must leave the local invoice in place and say so.
4. Payments in: map Stripe's paid events to `RecordPayment`, idempotent on the Stripe id.
