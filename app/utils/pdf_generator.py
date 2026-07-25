"""Generate invoice PDFs using fpdf2 (pure Python, no system deps)."""
try:
    from fpdf import FPDF, XPos, YPos
    _FPDF_AVAILABLE = True
except ImportError:
    _FPDF_AVAILABLE = False

# Dark navy matching the old invoice table header
_NAV_R, _NAV_G, _NAV_B = 28, 52, 88

# Line-item row metrics. A single-line row works out to 7mm, matching the
# original fixed-height look; extra wrapped lines grow the row from there.
_LINE_H = 5.5
_ROW_PAD = 1.5
_ROW_MIN_H = 7.0
# Keep content clear of the "Thank You" footer, which sits at page height - 20.
_FOOTER_RESERVE = 30.0


def _table_bottom(pdf) -> float:
    return pdf.h - _FOOTER_RESERVE


def _ensure_space(pdf, needed: float) -> None:
    """Start a new page if `needed` mm won't fit above the footer."""
    if pdf.get_y() + needed > _table_bottom(pdf):
        pdf.add_page()

# fpdf2's built-in fonts are latin-1 only; anything outside that range raises
# FPDFUnicodeEncodingException mid-render. Curly quotes and dashes arrive
# constantly via copy-paste from email and Word, so map the common typographic
# characters to sensible ASCII rather than letting a PDF fail to generate.
_UNICODE_FALLBACKS = {
    "‐": "-",  "‑": "-",  "‒": "-",  "–": "-",   # hyphens / en dash
    "—": "-",  "―": "-",                                   # em dash / horizontal bar
    "‘": "'",  "’": "'",  "‚": ",",  "‛": "'",   # single quotes
    "“": '"',  "”": '"',  "„": '"',  "‟": '"',   # double quotes
    "…": "...",                                                 # ellipsis
    "•": "*",  "‣": "*",  "●": "*",  "▪": "*",   # bullets
    " ": " ",  " ": " ",  " ": " ",  " ": " ",   # spaces
    "​": "",   "﻿": "",                                    # zero-width
    "←": "<-", "→": "->", "⇒": "=>",                  # arrows
    "≤": "<=", "≥": ">=", "≠": "!=", "≈": "~",   # math
    "⁄": "/",  "−": "-",                                   # fraction slash / minus
    "™": "(TM)", "℗": "(P)",                               # marks
    "€": "EUR", "′": "'", "″": '"',                   # euro / prime
}


def _safe(text) -> str:
    """Render any text safely with fpdf2's latin-1 core fonts.

    Substitutes common Unicode punctuation, then replaces whatever is still
    unencodable. A PDF that shows "?" for one exotic glyph beats a 500 error
    on an invoice the customer is waiting for.
    """
    if not text:
        return ""
    text = str(text)
    for bad, good in _UNICODE_FALLBACKS.items():
        if bad in text:
            text = text.replace(bad, good)
    return text.encode("latin-1", errors="replace").decode("latin-1")


def generate_invoice_pdf(invoice, company: dict) -> bytes:
    """Return PDF bytes for the given Invoice ORM object."""
    if not _FPDF_AVAILABLE:
        raise ImportError("fpdf2 is required: pip install fpdf2")

    pdf = FPDF()
    pdf.add_page()
    pdf.set_margins(15, 15, 15)
    eff_w = pdf.epw
    half = eff_w * 0.55
    right_x = pdf.l_margin + half

    # ── Two-column header ─────────────────────────────────────────────────────
    top_y = pdf.get_y()

    # Left: company name
    pdf.set_xy(pdf.l_margin, top_y)
    pdf.set_font("Helvetica", "B", 18)
    pdf.set_text_color(0, 0, 0)
    pdf.cell(half, 10, _safe(company.get("company_name", "")))

    # Right: INVOICE label
    pdf.set_xy(right_x, top_y)
    pdf.set_font("Helvetica", "B", 20)
    pdf.cell(eff_w - half, 10, "INVOICE", align="R")

    # Left: address lines
    addr_lines = []
    if company.get("company_address"):
        addr_lines.append(company["company_address"])
    city_state = (
        f"{company.get('company_city', '')}, "
        f"{company.get('company_state', '')} "
        f"{company.get('company_zip', '')}"
    ).strip(", ").strip()
    if city_state:
        addr_lines.append(city_state)
    if company.get("company_email"):
        addr_lines.append(company["company_email"])
    if company.get("company_phone"):
        addr_lines.append(company["company_phone"])

    pdf.set_font("Helvetica", "", 10)
    left_y = top_y + 11
    for line in addr_lines:
        if line.strip():
            pdf.set_xy(pdf.l_margin, left_y)
            pdf.cell(half, 5, _safe(line))
            left_y += 5

    # Right: Invoice # and Date
    pdf.set_font("Helvetica", "", 10)
    pdf.set_xy(right_x, top_y + 12)
    pdf.cell(eff_w - half, 6, _safe(f"Invoice #: {invoice.invoice_number}"), align="R")
    pdf.set_xy(right_x, top_y + 18)
    pdf.cell(eff_w - half, 6, f"Date: {invoice.date.strftime('%Y-%m-%d')}", align="R")

    # Move below both columns + thin rule
    pdf.set_y(max(left_y + 3, top_y + 28))
    pdf.set_draw_color(180, 180, 180)
    pdf.line(pdf.l_margin, pdf.get_y(), pdf.l_margin + eff_w, pdf.get_y())
    pdf.ln(5)

    # ── Bill To ───────────────────────────────────────────────────────────────
    pdf.set_font("Helvetica", "B", 10)
    pdf.cell(0, 6, "Bill To:", new_x=XPos.LMARGIN, new_y=YPos.NEXT)
    pdf.ln(1)

    pdf.set_font("Helvetica", "", 10)
    c = invoice.customer
    bill_lines = [c.name]
    if c.attn:
        bill_lines.append(f"Attn: {c.attn}")
    if c.address:
        bill_lines.append(c.address)
    city_line = ", ".join(p for p in [c.city, c.state, c.zip_code] if p)
    if city_line:
        bill_lines.append(city_line)
    for line in bill_lines:
        pdf.cell(0, 5.5, _safe(line), new_x=XPos.LMARGIN, new_y=YPos.NEXT)

    pdf.ln(7)

    # ── Line items table ──────────────────────────────────────────────────────
    col_num  = eff_w * 0.08
    col_desc = eff_w * 0.44
    col_qty  = eff_w * 0.11
    col_up   = eff_w * 0.18
    col_tot  = eff_w - col_num - col_desc - col_qty - col_up

    def _draw_table_header():
        pdf.set_fill_color(_NAV_R, _NAV_G, _NAV_B)
        pdf.set_text_color(255, 255, 255)
        pdf.set_font("Helvetica", "B", 9)
        pdf.cell(col_num,  7.5, "Line #",      fill=True, align="C")
        pdf.cell(col_desc, 7.5, "Description", fill=True)
        pdf.cell(col_qty,  7.5, "Qty",         fill=True, align="C")
        pdf.cell(col_up,   7.5, "Unit Price",  fill=True, align="R")
        pdf.cell(col_tot,  7.5, "Total",       fill=True, align="R")
        pdf.ln()
        pdf.set_text_color(0, 0, 0)
        pdf.set_font("Helvetica", "", 10)

    _draw_table_header()

    # Rows are laid out by hand so long descriptions wrap instead of running
    # past the column. Take over pagination too — auto page-break would split a
    # row's background from its text.
    pdf.set_auto_page_break(False)
    pdf.set_draw_color(220, 220, 220)

    for i, item in enumerate(invoice.line_items, start=1):
        desc = _safe(item.description)
        # Measure first: how many lines will this description need?
        wrapped = pdf.multi_cell(col_desc, _LINE_H, desc,
                                 dry_run=True, output="LINES") or [""]
        row_h = max(_ROW_MIN_H, len(wrapped) * _LINE_H + _ROW_PAD)

        # Start a new page if this row won't fit above the footer.
        if pdf.get_y() + row_h > _table_bottom(pdf):
            pdf.add_page()
            _draw_table_header()

        x0, y0 = pdf.l_margin, pdf.get_y()

        # 1. Paint the row background and bottom rule at full height, so every
        #    column lines up regardless of how tall the description made it.
        bg = (249, 250, 251) if i % 2 == 0 else (255, 255, 255)
        pdf.set_fill_color(*bg)
        pdf.set_xy(x0, y0)
        for w in (col_num, col_desc, col_qty, col_up, col_tot):
            pdf.cell(w, row_h, "", fill=True, border="B")

        # 2. Overlay the text, top-aligned so wrapped rows read cleanly.
        ty = y0 + _ROW_PAD / 2
        pdf.set_xy(x0, ty)
        pdf.cell(col_num, _LINE_H, str(i), align="C")
        pdf.set_xy(x0 + col_num, ty)
        pdf.multi_cell(col_desc, _LINE_H, desc, align="L")
        pdf.set_xy(x0 + col_num + col_desc, ty)
        pdf.cell(col_qty, _LINE_H, f"{item.quantity:g}", align="C")
        pdf.cell(col_up,  _LINE_H, f"${item.unit_price:,.2f}", align="R")
        pdf.cell(col_tot, _LINE_H, f"${item.amount:,.2f}", align="R")

        pdf.set_xy(x0, y0 + row_h)

    pdf.ln(5)

    # Total(s) — right-aligned below table (matching old invoice style)
    credit_applied = invoice.credit_applied or 0.0
    _ensure_space(pdf, 21 if credit_applied > 0 else 7)
    if credit_applied > 0:
        pdf.set_font("Helvetica", "", 10)
        pdf.cell(eff_w - col_up - col_tot, 6, "")
        pdf.cell(col_up, 6, "Subtotal:", align="R")
        pdf.cell(col_tot, 6, f"${invoice.total:,.2f}", align="R")
        pdf.ln()
        pdf.cell(eff_w - col_up - col_tot, 6, "")
        pdf.cell(col_up, 6, "Credit:", align="R")
        pdf.cell(col_tot, 6, f"-${credit_applied:,.2f}", align="R")
        pdf.ln()
        pdf.set_font("Helvetica", "B", 11)
        pdf.cell(eff_w - col_up - col_tot, 7, "")
        pdf.cell(col_up, 7, "Amount Due:", align="R")
        pdf.cell(col_tot, 7, f"${invoice.net_total:,.2f}", align="R")
        pdf.ln()
    else:
        pdf.set_font("Helvetica", "B", 11)
        pdf.cell(eff_w - col_up - col_tot, 7, "")
        pdf.cell(col_up, 7, "Total:", align="R")
        pdf.cell(col_tot, 7, f"${invoice.total:,.2f}", align="R")
        pdf.ln()

    pdf.ln(8)

    # ── Notes & terms ─────────────────────────────────────────────────────────
    if invoice.notes:
        notes = _safe(invoice.notes)
        pdf.set_font("Helvetica", "", 9)
        n_lines = len(pdf.multi_cell(0, 5, notes, dry_run=True, output="LINES") or [""])
        _ensure_space(pdf, 5 + n_lines * 5 + 3)
        pdf.set_font("Helvetica", "B", 9)
        pdf.cell(0, 5, "Notes:", new_x=XPos.LMARGIN, new_y=YPos.NEXT)
        pdf.set_font("Helvetica", "", 9)
        pdf.multi_cell(0, 5, notes)
        pdf.ln(3)

    pdf.set_font("Helvetica", "", 9)
    pdf.set_text_color(80, 80, 80)
    if invoice.term1 or invoice.term2:
        _ensure_space(pdf, 10)
    if invoice.term1:
        pdf.cell(0, 5, _safe(invoice.term1), new_x=XPos.LMARGIN, new_y=YPos.NEXT)
    if invoice.term2:
        pdf.cell(0, 5, _safe(invoice.term2), new_x=XPos.LMARGIN, new_y=YPos.NEXT)

    # ── Footer ────────────────────────────────────────────────────────────────
    pdf.set_y(pdf.h - 20)
    pdf.set_font("Helvetica", "B", 11)
    pdf.set_text_color(0, 0, 0)
    pdf.cell(0, 10, "Thank You for Your Business!", align="C")

    return bytes(pdf.output())
